namespace FinansApi.Data;

public enum EntryType
{
    Income = 1,
    Expense = 2
}

public class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<Category> Categories { get; set; } = [];
    public List<TransactionEntry> Transactions { get; set; } = [];
}

public class Category
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public EntryType Type { get; set; }
    public User User { get; set; } = null!;
    public List<TransactionEntry> Transactions { get; set; } = [];
}

public class TransactionEntry
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int CategoryId { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string? Description { get; set; }
    public EntryType Type { get; set; }
    public User User { get; set; } = null!;
    public Category Category { get; set; } = null!;
}
