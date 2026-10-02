using FinansApi.Data;
using FinansApi.Data.Accounting;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Services.Accounting;

public sealed class ReconciliationService(AppDbContext database, AccountingContext context)
{
    public async Task<List<ReconciliationCheck>> CheckAsync(int companyId, int periodId, List<LedgerRow> rows)
    {
        var period = await context.Find<FiscalPeriod>(companyId, periodId);
        var checks = new List<ReconciliationCheck>();
        Add(checks, "Mizan denkliği", rows.Sum(row => row.Debit), rows.Sum(row => row.Credit));
        foreach (var entry in rows.GroupBy(row => row.JournalEntryId))
        {
            Add(checks, $"Fiş denkliği: {entry.First().Number}", entry.Sum(row => row.Debit), entry.Sum(row => row.Credit));
        }

        await CheckStockAsync(companyId, periodId, rows, checks);
        await CheckTreasuryAsync(companyId, rows, checks);
        await CheckCounterpartiesAsync(companyId, period.EndDate, rows, checks);
        return checks;
    }

    private async Task CheckStockAsync(int companyId, int periodId, List<LedgerRow> rows, List<ReconciliationCheck> checks)
    {
        var products = await database.Set<Product>().AsNoTracking().Where(product => product.CompanyId == companyId).ToListAsync();
        var stock = await database.Set<StockMovement>().AsNoTracking().Where(movement => movement.CompanyId == companyId && movement.FiscalPeriodId == periodId).ToListAsync();
        foreach (var accountProducts in products.GroupBy(product => product.InventoryAccountId))
        {
            var productIds = accountProducts.Select(product => product.Id).ToHashSet();
            var stockValue = stock.Where(movement => productIds.Contains(movement.ProductId)).Sum(movement => movement.TotalCost);
            var ledgerValue = rows.Where(row => row.AccountId == accountProducts.Key).Sum(row => row.Debit - row.Credit);
            Add(checks, $"Stok hesabı: {accountProducts.Key}", stockValue, ledgerValue);
        }

        foreach (var productMovements in stock.GroupBy(movement => movement.ProductId))
        {
            var quantity = productMovements.Sum(movement => movement.Quantity);
            var value = productMovements.Sum(movement => movement.TotalCost);
            var consistent = quantity >= 0 && value >= 0 && (quantity != 0 || value == 0);
            Add(checks, $"Stok miktar/değer tutarlılığı: {productMovements.Key}", 1, consistent ? 1 : 0);
        }
    }

    private async Task CheckTreasuryAsync(int companyId, List<LedgerRow> rows, List<ReconciliationCheck> checks)
    {
        var accounts = await database.Set<TreasuryAccount>().AsNoTracking().Where(account => account.CompanyId == companyId).ToListAsync();
        foreach (var ledgerGroup in accounts.GroupBy(account => account.LedgerAccountId))
        {
            var accountIds = ledgerGroup.Select(account => account.Id).ToHashSet();
            var ledgerBalance = rows.Where(row => row.AccountId == ledgerGroup.Key).Sum(row => row.Debit - row.Credit);
            var subledgerBalance = rows.Where(row => row.TreasuryAccountId.HasValue && accountIds.Contains(row.TreasuryAccountId.Value)).Sum(row => row.Debit - row.Credit);
            Add(checks, $"Kasa/banka hesabı: {ledgerGroup.Key}", ledgerBalance, subledgerBalance);
        }

        foreach (var account in accounts)
        {
            decimal runningBalance = 0;
            var nonnegative = true;
            var days = rows.Where(row => row.TreasuryAccountId == account.Id).GroupBy(row => row.Date).OrderBy(day => day.Key);
            foreach (var day in days)
            {
                runningBalance += day.Sum(row => row.Debit - row.Credit);
                nonnegative &= runningBalance >= 0;
            }

            Add(checks, $"Negatif bakiye kontrolü: {account.Name}", 1, nonnegative ? 1 : 0);
        }
    }

    private async Task CheckCounterpartiesAsync(int companyId, DateOnly asOfDate, List<LedgerRow> rows, List<ReconciliationCheck> checks)
    {
        var profile = await database.Set<PostingProfile>().FindAsync(companyId);
        if (profile is null)
        {
            Add(checks, "Muhasebe hesap eşlemeleri", 1, 0);
            return;
        }

        // A later reversal must not change the result of an earlier as-of report.
        var reversedEntryIds = await database.Set<JournalEntry>().AsNoTracking().Where(entry => entry.CompanyId == companyId && entry.Status == JournalStatus.Posted && entry.Date <= asOfDate
            && entry.ReversalOfId.HasValue).Select(entry => entry.ReversalOfId!.Value).ToListAsync();
        var reversedEntries = reversedEntryIds.ToHashSet();
        var invoices = await database.Set<Invoice>().AsNoTracking().Where(invoice => invoice.CompanyId == companyId && invoice.Date <= asOfDate && invoice.JournalEntryId.HasValue).ToListAsync();
        var payments = await database.Set<Payment>().AsNoTracking().Include(payment => payment.Allocations).Where(payment => payment.CompanyId == companyId && payment.Date <= asOfDate).ToListAsync();
        var manualEntries = await database.Set<JournalEntry>().AsNoTracking().Include(entry => entry.Lines).Where(entry => entry.CompanyId == companyId && entry.Status == JournalStatus.Posted && entry.Date <= asOfDate
            && entry.SourceType == "Manual").ToListAsync();
        var counterparties = await database.Set<Counterparty>().AsNoTracking().Where(party => party.CompanyId == companyId).ToListAsync();
        foreach (var party in counterparties)
        {
            foreach (var invoiceType in new[]
                {
                    InvoiceType.Sales,
                    InvoiceType.Purchase
                }

            )
            {
                var accountId = invoiceType == InvoiceType.Sales ? profile.ReceivableAccountId : profile.PayableAccountId;
                var sign = invoiceType == InvoiceType.Sales ? 1m : -1m;
                var partyInvoices = invoices.Where(invoice => invoice.CounterpartyId == party.Id && invoice.Type == invoiceType
                    && !reversedEntries.Contains(invoice.JournalEntryId!.Value)).ToList();
                var invoiceIds = partyInvoices.Select(invoice => invoice.Id).ToHashSet();
                var paid = payments.Where(payment => !reversedEntries.Contains(payment.JournalEntryId)).SelectMany(payment => payment.Allocations).Where(allocation => invoiceIds.Contains(allocation.InvoiceId)).Sum(allocation => allocation.Amount);
                var manualBalance = manualEntries.Where(entry => !reversedEntries.Contains(entry.Id)).SelectMany(entry => entry.Lines).Where(line => line.AccountId == accountId && line.CounterpartyId == party.Id).Sum(line => line.Debit - line.Credit);
                var expected = sign * (partyInvoices.Sum(invoice => invoice.GrandTotal) - paid) + manualBalance;
                var actual = rows.Where(row => row.AccountId == accountId && row.CounterpartyId == party.Id).Sum(row => row.Debit - row.Credit);
                Add(checks, $"Cari/fatura mutabakatı: {party.Code} ({invoiceType})", expected, actual);
            }
        }
    }

    private static void Add(List<ReconciliationCheck> checks, string name, decimal expected, decimal actual) => checks.Add(new ReconciliationCheck(name, expected, actual, expected == actual));

}
