using System.ComponentModel.DataAnnotations;
using FinansApi.Data;

namespace FinansApi.Dtos;

public class RegisterRequest
{
    [Required, MinLength(2), MaxLength(120)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(160)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(6), MaxLength(80)]
    public string Password { get; set; } = string.Empty;
}

public class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public record AuthResponse(string Token, DateTime ExpiresAt, UserResponse User);
public record UserResponse(int Id, string FullName, string Email);

public class CategoryRequest
{
    [Required, MinLength(2), MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public EntryType Type { get; set; }
}

public record CategoryResponse(int Id, string Name, EntryType Type);

public class TransactionRequest
{
    [Required]
    public int CategoryId { get; set; }

    [Range(0.01, 100000000)]
    public decimal Amount { get; set; }

    [Required]
    public DateTime Date { get; set; }

    [MaxLength(250)]
    public string? Description { get; set; }

    [Required]
    public EntryType Type { get; set; }
}

public record TransactionResponse(
    int Id,
    int CategoryId,
    string CategoryName,
    decimal Amount,
    DateTime Date,
    string? Description,
    EntryType Type);

public record PagedResponse<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

public record SummaryResponse(decimal TotalIncome, decimal TotalExpense, decimal Balance, int TransactionCount);

public record CategoryReportItem(int CategoryId, string CategoryName, EntryType Type, decimal Total);

public record MonthlyReportItem(int Month, string MonthName, decimal Income, decimal Expense);
