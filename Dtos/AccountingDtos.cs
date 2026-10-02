using System.ComponentModel.DataAnnotations;
using FinansApi.Data.Accounting;

namespace FinansApi.Dtos;

public record CompanyRequest(
    [Required, StringLength(160, MinimumLength = 2)] string Name,
    string? TaxNumber,
    bool IsActive = true);

public record PeriodRequest([Required, StringLength(80)] string Name, DateOnly StartDate, DateOnly EndDate);

public record MemberRequest(int UserId, CompanyRole Role, bool IsActive = true);

public record AccountRequest(
    string Code,
    string Name,
    AccountClass AccountClass,
    int? ParentId = null,
    bool IsPostingAllowed = true,
    bool IsActive = true,
    string? RequiredDimension = null,
    int Version = 1);

public record CounterpartyRequest(
    string Code,
    string Name,
    PartyType Type,
    string? TaxNumber = null,
    string? Email = null,
    string? Phone = null,
    string? Address = null,
    int PaymentTermDays = 0,
    bool IsActive = true,
    int Version = 1);

public record TaxRateRequest(string Name, decimal Rate, bool IsActive = true);

public record ProductRequest(
    string Code,
    string Name,
    string Unit,
    decimal SalesPrice,
    int DefaultTaxRateId,
    decimal CriticalStockQuantity,
    int? PreferredSupplierId,
    int InventoryAccountId,
    int CostAccountId,
    bool IsActive = true,
    int Version = 1);

public record TreasuryRequest(
    string Name,
    TreasuryType Type,
    int LedgerAccountId,
    string? BankName = null,
    string? Iban = null,
    bool IsActive = true,
    int Version = 1);

public record JournalLineRequest(
    int AccountId,
    decimal Debit,
    decimal Credit,
    int? CounterpartyId = null,
    int? TreasuryAccountId = null,
    string? Description = null);

public record JournalRequest(
    int FiscalPeriodId,
    DateOnly Date,
    string? Description,
    List<JournalLineRequest> Lines,
    int Version = 1);

public record OperationRequest(
    int Version,
    [Required, StringLength(100, MinimumLength = 8)] string IdempotencyKey,
    DateOnly? Date = null,
    int? FiscalPeriodId = null,
    string? Reason = null);

public record InvoiceLineRequest(int ProductId, decimal Quantity, decimal UnitPrice, decimal DiscountAmount = 0);

public record InvoiceRequest(
    int FiscalPeriodId,
    int CounterpartyId,
    InvoiceType Type,
    DateOnly Date,
    DateOnly DueDate,
    List<InvoiceLineRequest> Lines,
    int Version = 1);

public record AllocationRequest(int InvoiceId, decimal Amount);

public record PaymentRequest(
    int FiscalPeriodId,
    int CounterpartyId,
    int TreasuryAccountId,
    PaymentDirection Direction,
    DateOnly Date,
    decimal Amount,
    List<AllocationRequest> Allocations,
    [Required] string IdempotencyKey);

public record TransferRequest(
    int FiscalPeriodId,
    int SourceAccountId,
    int TargetAccountId,
    DateOnly Date,
    decimal Amount,
    [Required] string IdempotencyKey);

public record ClosingRequest(int TargetPeriodId, int Version, [Required] string IdempotencyKey);

public record NotificationSettingsRequest(string TimeZoneId, int ReminderHour, string? ContactEmail);
