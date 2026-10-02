namespace FinansApi.Data.Accounting;

public enum CompanyRole
{
    Admin = 1,
    Accountant = 2,
    Sales = 3,
    Reader = 4
}

public enum PeriodStatus
{
    Open = 1,
    Locked = 2
}

public enum AccountClass
{
    Asset = 1,
    Liability = 2,
    Equity = 3,
    Income = 4,
    Expense = 5
}

public enum PartyType
{
    Customer = 1,
    Supplier = 2,
    Both = 3
}

public enum JournalStatus
{
    Draft = 1,
    Posted = 2
}

public enum InvoiceType
{
    Sales = 1,
    Purchase = 2
}

public enum InvoiceStatus
{
    Draft = 1,
    Approved = 2,
    PartiallyPaid = 3,
    Paid = 4,
    Cancelled = 5
}

public enum TreasuryType
{
    Cash = 1,
    Bank = 2
}

public enum PaymentDirection
{
    Collection = 1,
    Disbursement = 2
}

public class Company
{
    public int Id
    {
        get; set;
    }
    public string Name { get; set; } = "";
    public string? TaxNumber
    {
        get; set;
    }
    public string CurrencyCode { get; set; } = "TRY";
    public bool IsActive { get; set; } = true;
    public string TimeZoneId { get; set; } = "Europe/Istanbul";
    public int ReminderHour { get; set; } = 9;
    public string? ContactEmail
    {
        get; set;
    }
}

public class CompanyMember
{
    public int CompanyId
    {
        get; set;
    }
    public int UserId
    {
        get; set;
    }
    public CompanyRole Role
    {
        get; set;
    }
    public bool IsActive { get; set; } = true;
}

public abstract class CompanyEntity
{
    public int Id
    {
        get; set;
    }
    public int CompanyId
    {
        get; set;
    }
    public int Version { get; set; } = 1;
}

public class FiscalPeriod : CompanyEntity
{
    public string Name { get; set; } = "";
    public DateOnly StartDate
    {
        get; set;
    }
    public DateOnly EndDate
    {
        get; set;
    }
    public PeriodStatus Status { get; set; } = PeriodStatus.Open;
    public DateTimeOffset? LockedAtUtc
    {
        get; set;
    }
    public int? LockedByUserId
    {
        get; set;
    }
}

public class Account : CompanyEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int? ParentId
    {
        get; set;
    }
    public AccountClass AccountClass
    {
        get; set;
    }
    public bool IsPostingAllowed { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public string? RequiredDimension
    {
        get; set;
    }
}

public class Counterparty : CompanyEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public PartyType Type
    {
        get; set;
    }
    public string? TaxNumber
    {
        get; set;
    }
    public string? Email
    {
        get; set;
    }
    public string? Phone
    {
        get; set;
    }
    public string? Address
    {
        get; set;
    }
    public int PaymentTermDays
    {
        get; set;
    }
    public bool IsActive { get; set; } = true;
}

public class TaxRate : CompanyEntity
{
    public string Name { get; set; } = "";
    public decimal Rate
    {
        get; set;
    }
    public bool IsActive { get; set; } = true;
}

public class Product : CompanyEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "Adet";
    public decimal SalesPrice
    {
        get; set;
    }
    public int DefaultTaxRateId
    {
        get; set;
    }
    public decimal CriticalStockQuantity
    {
        get; set;
    }
    public int? PreferredSupplierId
    {
        get; set;
    }
    public int InventoryAccountId
    {
        get; set;
    }
    public int CostAccountId
    {
        get; set;
    }
    public bool IsActive { get; set; } = true;
    public bool LowStockAlertActive
    {
        get; set;
    }
}

public class TreasuryAccount : CompanyEntity
{
    public string Name { get; set; } = "";
    public TreasuryType Type
    {
        get; set;
    }
    public int LedgerAccountId
    {
        get; set;
    }
    public string CurrencyCode { get; set; } = "TRY";
    public string? BankName
    {
        get; set;
    }
    public string? Iban
    {
        get; set;
    }
    public bool IsActive { get; set; } = true;
}

public class PostingProfile
{
    public int CompanyId
    {
        get; set;
    }
    public int ReceivableAccountId
    {
        get; set;
    }
    public int PayableAccountId
    {
        get; set;
    }
    public int RevenueAccountId
    {
        get; set;
    }
    public int InputVatAccountId
    {
        get; set;
    }
    public int OutputVatAccountId
    {
        get; set;
    }
    public int ResultAccountId
    {
        get; set;
    }
    public int RetainedEarningsAccountId
    {
        get; set;
    }
}

public class JournalEntry : CompanyEntity
{
    public int FiscalPeriodId
    {
        get; set;
    }
    public string Number { get; set; } = "";
    public DateOnly Date
    {
        get; set;
    }
    public string? Description
    {
        get; set;
    }
    public JournalStatus Status
    {
        get; set;
    }
    public string SourceType { get; set; } = "Manual";
    public string SourceId { get; set; } = Guid.NewGuid().ToString("N");
    public int? ReversalOfId
    {
        get; set;
    }
    public List<JournalLine> Lines { get; set; } = [];
}

public class JournalLine
{
    public int Id
    {
        get; set;
    }
    public int JournalEntryId
    {
        get; set;
    }
    public int AccountId
    {
        get; set;
    }
    public decimal Debit
    {
        get; set;
    }
    public decimal Credit
    {
        get; set;
    }
    public int? CounterpartyId
    {
        get; set;
    }
    public int? TreasuryAccountId
    {
        get; set;
    }
    public string? Description
    {
        get; set;
    }
}

public class Invoice : CompanyEntity
{
    public int FiscalPeriodId
    {
        get; set;
    }
    public int CounterpartyId
    {
        get; set;
    }
    public InvoiceType Type
    {
        get; set;
    }
    public string Number { get; set; } = "";
    public DateOnly Date
    {
        get; set;
    }
    public DateOnly DueDate
    {
        get; set;
    }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public decimal NetTotal
    {
        get; set;
    }
    public decimal TaxTotal
    {
        get; set;
    }
    public decimal GrandTotal
    {
        get; set;
    }
    public int? JournalEntryId
    {
        get; set;
    }
    public List<InvoiceLine> Lines { get; set; } = [];
}

public class InvoiceLine
{
    public int Id
    {
        get; set;
    }
    public int InvoiceId
    {
        get; set;
    }
    public int ProductId
    {
        get; set;
    }
    public string DescriptionSnapshot { get; set; } = "";
    public decimal Quantity
    {
        get; set;
    }
    public decimal UnitPrice
    {
        get; set;
    }
    public decimal DiscountAmount
    {
        get; set;
    }
    public decimal TaxRateSnapshot
    {
        get; set;
    }
    public decimal NetAmount
    {
        get; set;
    }
    public decimal TaxAmount
    {
        get; set;
    }
    public decimal TotalAmount
    {
        get; set;
    }
    public decimal CostSnapshot
    {
        get; set;
    }
}

public class StockMovement : CompanyEntity
{
    public int FiscalPeriodId
    {
        get; set;
    }
    public int ProductId
    {
        get; set;
    }
    public DateOnly Date
    {
        get; set;
    }
    public decimal Quantity
    {
        get; set;
    }
    public decimal UnitCost
    {
        get; set;
    }
    public decimal TotalCost
    {
        get; set;
    }
    public string SourceType { get; set; } = "";
    public string SourceId { get; set; } = "";
    public int? ReversalOfId
    {
        get; set;
    }
}

public class Payment : CompanyEntity
{
    public int FiscalPeriodId
    {
        get; set;
    }
    public int CounterpartyId
    {
        get; set;
    }
    public int TreasuryAccountId
    {
        get; set;
    }
    public PaymentDirection Direction
    {
        get; set;
    }
    public DateOnly Date
    {
        get; set;
    }
    public decimal Amount
    {
        get; set;
    }
    public int JournalEntryId
    {
        get; set;
    }
    public bool IsReversed
    {
        get; set;
    }
    public List<PaymentAllocation> Allocations { get; set; } = [];
}

public class PaymentAllocation
{
    public int Id
    {
        get; set;
    }
    public int PaymentId
    {
        get; set;
    }
    public int InvoiceId
    {
        get; set;
    }
    public decimal Amount
    {
        get; set;
    }
}

public class Transfer : CompanyEntity
{
    public int FiscalPeriodId
    {
        get; set;
    }
    public int SourceAccountId
    {
        get; set;
    }
    public int TargetAccountId
    {
        get; set;
    }
    public DateOnly Date
    {
        get; set;
    }
    public decimal Amount
    {
        get; set;
    }
    public int JournalEntryId
    {
        get; set;
    }
}

public class AuditLog
{
    public long Id
    {
        get; set;
    }
    public int CompanyId
    {
        get; set;
    }
    public int ActorUserId
    {
        get; set;
    }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public DateTimeOffset TimestampUtc
    {
        get; set;
    }
    public string CorrelationId { get; set; } = "";
    public string? Before
    {
        get; set;
    }
    public string? After
    {
        get; set;
    }
    public string? Reason
    {
        get; set;
    }
}

public class OperationReceipt
{
    public int Id
    {
        get; set;
    }
    public int CompanyId
    {
        get; set;
    }
    public string Key { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string ResponseJson { get; set; } = "";
}

public class CarryForwardRun : CompanyEntity
{
    public int SourcePeriodId
    {
        get; set;
    }
    public int TargetPeriodId
    {
        get; set;
    }
    public int? OpeningJournalEntryId
    {
        get; set;
    }
    public int? ClosingJournalEntryId
    {
        get; set;
    }
    public DateTimeOffset CreatedAtUtc
    {
        get; set;
    }
    public int ActorUserId
    {
        get; set;
    }
}

public class OutboxMessage : CompanyEntity
{
    public string EventType { get; set; } = "";
    public string Payload { get; set; } = "";
    public string DeduplicationKey { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public int Attempts
    {
        get; set;
    }
    public DateTimeOffset NextAttemptAtUtc
    {
        get; set;
    }
    public DateTimeOffset? LockedUntilUtc
    {
        get; set;
    }
    public DateTimeOffset? SentAtUtc
    {
        get; set;
    }
    public string? LastError
    {
        get; set;
    }
}

public class ReminderRun
{
    public int CompanyId
    {
        get; set;
    }
    public DateOnly BusinessDate
    {
        get; set;
    }
}
