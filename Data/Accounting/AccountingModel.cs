using FinansApi.Data.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FinansApi.Data;

public static class AccountingModel
{
    public static void Configure(ModelBuilder m)
    {
        m.Entity<Company>();
        m.Entity<CompanyMember>().HasKey(x => new { x.CompanyId, x.UserId });
        m.Entity<CompanyMember>().HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<CompanyMember>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<PostingProfile>().HasKey(x => x.CompanyId);
        m.Entity<PostingProfile>().HasOne<Company>().WithOne().HasForeignKey<PostingProfile>(x => x.CompanyId);
        m.Entity<FiscalPeriod>();
        m.Entity<Account>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        m.Entity<Account>().HasOne<Account>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Counterparty>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        m.Entity<TaxRate>();
        m.Entity<Product>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        m.Entity<Product>().HasOne<TaxRate>().WithMany().HasForeignKey(x => x.DefaultTaxRateId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Product>().HasOne<Counterparty>().WithMany().HasForeignKey(x => x.PreferredSupplierId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Product>().HasOne<Account>().WithMany().HasForeignKey(x => x.InventoryAccountId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Product>().HasOne<Account>().WithMany().HasForeignKey(x => x.CostAccountId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<TreasuryAccount>().HasOne<Account>().WithMany().HasForeignKey(x => x.LedgerAccountId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<JournalEntry>().HasIndex(x => new { x.CompanyId, x.FiscalPeriodId, x.Number }).IsUnique();
        m.Entity<JournalEntry>().HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceId }).IsUnique();
        m.Entity<JournalEntry>().HasIndex(x => x.ReversalOfId).IsUnique();
        m.Entity<JournalEntry>().HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.ReversalOfId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<JournalEntry>().HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<JournalEntry>().HasOne<FiscalPeriod>().WithMany().HasForeignKey(x => x.FiscalPeriodId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<JournalLine>().HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<JournalLine>().HasOne<Counterparty>().WithMany().HasForeignKey(x => x.CounterpartyId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<JournalLine>().HasOne<TreasuryAccount>().WithMany().HasForeignKey(x => x.TreasuryAccountId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Invoice>().HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
        m.Entity<Invoice>().HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<Invoice>().HasOne<FiscalPeriod>().WithMany().HasForeignKey(x => x.FiscalPeriodId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Invoice>().HasOne<Counterparty>().WithMany().HasForeignKey(x => x.CounterpartyId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Invoice>().HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<InvoiceLine>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<StockMovement>().HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceId, x.ProductId }).IsUnique();
        m.Entity<StockMovement>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<StockMovement>().HasOne<FiscalPeriod>().WithMany().HasForeignKey(x => x.FiscalPeriodId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Payment>().HasMany(x => x.Allocations).WithOne().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Payment>().HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Payment>().HasOne<FiscalPeriod>().WithMany().HasForeignKey(x => x.FiscalPeriodId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Payment>().HasOne<Counterparty>().WithMany().HasForeignKey(x => x.CounterpartyId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Payment>().HasOne<TreasuryAccount>().WithMany().HasForeignKey(x => x.TreasuryAccountId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<PaymentAllocation>().HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<PaymentAllocation>().HasIndex(x => new { x.PaymentId, x.InvoiceId }).IsUnique();
        m.Entity<Transfer>().HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Transfer>().HasOne<FiscalPeriod>().WithMany().HasForeignKey(x => x.FiscalPeriodId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Transfer>().HasOne<TreasuryAccount>().WithMany().HasForeignKey(x => x.SourceAccountId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Transfer>().HasOne<TreasuryAccount>().WithMany().HasForeignKey(x => x.TargetAccountId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<AuditLog>().HasIndex(x => new { x.CompanyId, x.Id });
        m.Entity<OperationReceipt>().HasIndex(x => new { x.CompanyId, x.Key }).IsUnique();
        m.Entity<CarryForwardRun>().HasIndex(x => x.SourcePeriodId).IsUnique();
        m.Entity<CarryForwardRun>().HasOne<FiscalPeriod>().WithMany().HasForeignKey(x => x.SourcePeriodId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<CarryForwardRun>().HasOne<FiscalPeriod>().WithMany().HasForeignKey(x => x.TargetPeriodId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<CarryForwardRun>().HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.OpeningJournalEntryId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<CarryForwardRun>().HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.ClosingJournalEntryId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<CarryForwardRun>().HasIndex(x => x.TargetPeriodId).IsUnique();
        m.Entity<OutboxMessage>().HasIndex(x => x.DeduplicationKey).IsUnique();
        m.Entity<ReminderRun>().HasKey(x => new { x.CompanyId, x.BusinessDate });
        var cents = new ValueConverter<decimal, long>(v => checked((long)decimal.Round(v * 100m, 0, MidpointRounding.AwayFromZero)), v => v / 100m);
        var units = new ValueConverter<decimal, long>(v => checked((long)decimal.Round(v * 10000m, 0, MidpointRounding.AwayFromZero)), v => v / 10000m);
        foreach (var entity in m.Model.GetEntityTypes().ToList())
        {
            if (typeof(CompanyEntity).IsAssignableFrom(entity.ClrType))
            {
                m.Entity(entity.ClrType).HasBaseType((Type?)null);
                m.Entity(entity.ClrType).HasOne(typeof(Company), null).WithMany().HasForeignKey("CompanyId").OnDelete(DeleteBehavior.Restrict);
                m.Entity(entity.ClrType).Property("Version").IsConcurrencyToken();
            }

            if (entity.ClrType.Namespace != typeof(Company).Namespace)
            {
                continue;
            }

            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType == typeof(decimal))
                {
                    property.SetValueConverter(property.Name.Contains("Quantity")
                        || property.Name is "UnitPrice" or "UnitCost" or "SalesPrice" or "Rate" or "TaxRateSnapshot" ? units : cents);
                }

                if (property.ClrType == typeof(string))
                {
                    property.SetMaxLength(property.Name is "Payload" or "ResponseJson" ? 65536 : 500);
                }
            }
        }
    }

}
