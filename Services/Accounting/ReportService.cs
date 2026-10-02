using FinansApi.Data;
using FinansApi.Data.Accounting;
using FinansApi.Infrastructure.Errors;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Services.Accounting;

public record LedgerRow(
    int JournalEntryId,
    string Number,
    DateOnly Date,
    string SourceType,
    int AccountId,
    string AccountCode,
    string AccountName,
    AccountClass AccountClass,
    int? CounterpartyId,
    int? TreasuryAccountId,
    decimal Debit,
    decimal Credit);

public record BalanceRow(
    int AccountId,
    string Code,
    string Name,
    AccountClass AccountClass,
    decimal Debit,
    decimal Credit,
    decimal DebitBalance,
    decimal CreditBalance);

public record VatSummaryResponse(
    decimal InputVat,
    decimal OutputVat,
    decimal NetVat,
    decimal InputDifference,
    decimal OutputDifference,
    string CurrencyCode = "TRY");

public record ReconciliationCheck(string Name, decimal Expected, decimal Actual, bool Passed);

public sealed class ReportService(AppDbContext database, AccountingContext context, ReconciliationService reconciliation)
{
    public async Task<List<LedgerRow>> Rows(
        int companyId,
        int periodId,
        DateOnly? fromDate = null,
        DateOnly? to = null,
        bool excludeClosing = false)
    {
        var p = await context.Find<FiscalPeriod>(companyId, periodId);
        if ((fromDate.HasValue && (fromDate < p.StartDate || fromDate > p.EndDate))
            || (to.HasValue && (to < p.StartDate || to > p.EndDate))
            || (fromDate.HasValue && to.HasValue && fromDate > to))
        {
            throw DomainException.Invalid("Rapor tarih aralığı geçersiz.");
        }

        var query =
        from e in database.Set<JournalEntry>()
        from l in e.Lines
        join a in database.Set<Account>() on l.AccountId equals a.Id
        where e.CompanyId == companyId && e.FiscalPeriodId == periodId && e.Status == JournalStatus.Posted
        && (!fromDate.HasValue || e.Date >= fromDate)
        && (!to.HasValue || e.Date <= to)
        && (!excludeClosing || (e.SourceType != "Closing" && e.SourceType != "ResultTransfer"))
        orderby e.Date, e.Id, l.Id
        select new LedgerRow(e.Id, e.Number, e.Date, e.SourceType, a.Id, a.Code, a.Name, a.AccountClass, l.CounterpartyId, l.TreasuryAccountId, l.Debit, l.Credit);
        return await query.AsNoTracking().ToListAsync();
    }

    public async Task<List<BalanceRow>> Trial(int companyId, int periodId, DateOnly? fromDate = null, DateOnly? to = null)
    {
        var rows = await Rows(companyId, periodId, fromDate, to);
        return rows.GroupBy(x => new { x.AccountId, x.AccountCode, x.AccountName, x.AccountClass }).Select(g =>
            {
                var d = g.Sum(x => x.Debit);
                var c = g.Sum(x => x.Credit);
                return new BalanceRow(g.Key.AccountId, g.Key.AccountCode, g.Key.AccountName, g.Key.AccountClass, d, c, Math.Max(d - c, 0), Math.Max(c - d, 0));
            }).OrderBy(x => x.Code).ToList();
    }

    public async Task<object> Income(int companyId, int periodId, DateOnly? fromDate, DateOnly? to)
    {
        var rows = await Rows(companyId, periodId, fromDate, to, true);
        var income = rows.Where(x => x.AccountClass == AccountClass.Income).Sum(x => x.Credit - x.Debit);
        var expense = rows.Where(x => x.AccountClass == AccountClass.Expense).Sum(x => x.Debit - x.Credit);
        return new
        {
            Income = income,
            Expense = expense,
            Result = income - expense,
            CurrencyCode = "TRY"
        };
    }

    public async Task<object> BalanceSheet(int companyId, int periodId, DateOnly? to)
    {
        var rows = await Rows(companyId, periodId, null, to);
        decimal Net(AccountClass c) => ReportAccountMapping.NetBalance(rows, c);
        var assets = Net(AccountClass.Asset);
        var liabilities = -Net(AccountClass.Liability);
        var unclosedResult = -Net(AccountClass.Income) - Net(AccountClass.Expense);
        var equity = -Net(AccountClass.Equity) + unclosedResult;
        return new
        {
            Assets = assets,
            Liabilities = liabilities,
            Equity = equity,
            CurrentUnclosedResult = unclosedResult,
            Difference = assets - liabilities - equity,
            CurrencyCode = "TRY"
        };
    }

    public async Task<object> Statement(int companyId, int periodId, int? party, int? treasury, DateOnly? fromDate, DateOnly? to)
    {
        await Rows(companyId, periodId, fromDate, to); // Validate both ends before calculating the opening balance.
        var rows = (await Rows(companyId, periodId, null, to)).Where(x => (!party.HasValue || x.CounterpartyId == party) && (!treasury.HasValue || x.TreasuryAccountId == treasury)).ToList();
        var opening = rows.Where(x => fromDate.HasValue && x.Date < fromDate).Sum(x => x.Debit - x.Credit);
        var balance = opening;
        var items = rows.Where(x => !fromDate.HasValue || x.Date >= fromDate).Select(x =>
            {
                balance += x.Debit - x.Credit;
                return new
                {
                    Entry = x,
                    Balance = balance
                };
            }).ToList();
        var outstanding = new List<object>();
        if (party.HasValue)
        {
            var company = await database.Set<Company>().SingleAsync(company => company.Id == companyId);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(company.TimeZoneId)).DateTime);
            var invoices = await database.Set<Invoice>().Where(invoice => invoice.CompanyId == companyId && invoice.CounterpartyId == party
                && (invoice.Status == InvoiceStatus.Approved || invoice.Status == InvoiceStatus.PartiallyPaid)).ToListAsync();
            foreach (var invoice in invoices)
            {
                var allocations = await database.Set<Payment>().Where(payment => payment.CompanyId == companyId && !payment.IsReversed).SelectMany(payment => payment.Allocations).Where(allocation => allocation.InvoiceId == invoice.Id).Select(allocation => allocation.Amount).ToListAsync();
                var prefix = $"due:{companyId}:{invoice.Id}:";
                var lastReminder = await database.Set<OutboxMessage>().Where(message => message.CompanyId == companyId && message.DeduplicationKey.StartsWith(prefix) && message.Status == "Sent").OrderByDescending(message => message.Id).Select(message => message.SentAtUtc).FirstOrDefaultAsync();
                outstanding.Add(new
                {
                    invoice.Id,
                    invoice.Number,
                    invoice.Type,
                    invoice.DueDate,
                    RemainingAmount = invoice.GrandTotal - allocations.Sum(),
                    IsOverdue = invoice.DueDate < today,
                    LastReminderAtUtc = lastReminder
                });
            }
        }

        return new
        {
            OpeningBalance = opening,
            Items = items,
            ClosingBalance = balance,
            BalanceConvention = "Borç pozitif; alacak negatif",
            CurrencyCode = "TRY",
            CurrentOutstandingInvoices = outstanding
        };
    }

    public async Task<List<ReconciliationCheck>> Reconcile(int companyId, int periodId)
    {
        var rows = await Rows(companyId, periodId);
        var checks = await reconciliation.CheckAsync(companyId, periodId, rows);
        if (await database.Set<PostingProfile>().AnyAsync(profile => profile.CompanyId == companyId))
        {
            var vat = (VatSummaryResponse)await Vat(companyId, periodId, null, null);
            checks.Add(new("İndirilecek KDV mutabakatı", 0, vat.InputDifference, vat.InputDifference == 0));
            checks.Add(new("Hesaplanan KDV mutabakatı", 0, vat.OutputDifference, vat.OutputDifference == 0));
        }

        return checks;
    }

    public async Task<object> Vat(int companyId, int periodId, DateOnly? fromDate, DateOnly? to)
    {
        var rows = await Rows(companyId, periodId, fromDate, to);
        var profile = await database.Set<PostingProfile>().FindAsync(companyId) ?? throw DomainException.Invalid("Hesap eşlemesi eksik.");
        var ids = rows.Where(x => x.SourceType == "Invoice").Select(x => x.JournalEntryId).Distinct().ToList();
        var invoices = await database.Set<Invoice>().Include(x => x.Lines).Where(x => x.CompanyId == companyId && x.JournalEntryId.HasValue && ids.Contains(x.JournalEntryId.Value)).ToListAsync();
        var reversedIds = await database.Set<JournalEntry>().Where(x => x.CompanyId == companyId && x.FiscalPeriodId == periodId && x.ReversalOfId.HasValue
            && (!fromDate.HasValue || x.Date >= fromDate)
            && (!to.HasValue || x.Date <= to)).Select(x => x.ReversalOfId!.Value).ToListAsync();
        var cancelled = await database.Set<Invoice>().Where(x => x.CompanyId == companyId && x.JournalEntryId.HasValue && reversedIds.Contains(x.JournalEntryId.Value)).ToListAsync();
        decimal Tax(InvoiceType type) => invoices.Where(x => x.Type == type).Sum(x => x.TaxTotal) - cancelled.Where(x => x.Type == type).Sum(x => x.TaxTotal);
        var input = Tax(InvoiceType.Purchase);
        var output = Tax(InvoiceType.Sales);
        var ledgerInput = rows.Where(x => x.AccountId == profile.InputVatAccountId && x.SourceType != "Opening").Sum(x => x.Debit - x.Credit);
        var ledgerOutput = rows.Where(x => x.AccountId == profile.OutputVatAccountId && x.SourceType != "Opening").Sum(x => x.Credit - x.Debit);
        return new VatSummaryResponse(input, output, output - input, input - ledgerInput, output - ledgerOutput);
    }

}
