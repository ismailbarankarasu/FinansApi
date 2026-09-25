using Microsoft.AspNetCore.Identity;

namespace FinansApi.Data;

public static class DbInitializer
{
    public static void Seed(AppDbContext db)
    {
        if (db.Users.Any())
            return;

        var hasher = new PasswordHasher<User>();
        var user = new User
        {
            FullName = "Demo Öğrenci",
            Email = "demo@kurs.com",
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = hasher.HashPassword(user, "Demo123!");
        db.Users.Add(user);
        db.SaveChanges();

        var categories = CreateDefaultCategories(user.Id);
        db.Categories.AddRange(categories);
        db.SaveChanges();

        db.Transactions.AddRange(CreateDemoTransactions(user.Id, categories));
        db.SaveChanges();
    }

    public static List<Category> CreateDefaultCategories(int userId) =>
    [
        new() { UserId = userId, Name = "Maaş", Type = EntryType.Income },
        new() { UserId = userId, Name = "Freelance", Type = EntryType.Income },
        new() { UserId = userId, Name = "Yatırım", Type = EntryType.Income },
        new() { UserId = userId, Name = "Diğer Gelir", Type = EntryType.Income },
        new() { UserId = userId, Name = "Market", Type = EntryType.Expense },
        new() { UserId = userId, Name = "Ulaşım", Type = EntryType.Expense },
        new() { UserId = userId, Name = "Faturalar", Type = EntryType.Expense },
        new() { UserId = userId, Name = "Eğlence", Type = EntryType.Expense },
        new() { UserId = userId, Name = "Sağlık", Type = EntryType.Expense },
        new() { UserId = userId, Name = "Eğitim", Type = EntryType.Expense }
    ];

    private static List<TransactionEntry> CreateDemoTransactions(int userId, List<Category> categories)
    {
        int Id(string name) => categories.First(c => c.Name == name).Id;
        var today = DateTime.Today;
        return
        [
            new() { UserId = userId, CategoryId = Id("Maaş"), Type = EntryType.Income, Amount = 45000, Date = today.AddMonths(-2), Description = "Aylık maaş" },
            new() { UserId = userId, CategoryId = Id("Freelance"), Type = EntryType.Income, Amount = 8500, Date = today.AddMonths(-2).AddDays(12), Description = "Landing page işi" },
            new() { UserId = userId, CategoryId = Id("Market"), Type = EntryType.Expense, Amount = 3200, Date = today.AddMonths(-2).AddDays(3), Description = "Haftalık alışveriş" },
            new() { UserId = userId, CategoryId = Id("Faturalar"), Type = EntryType.Expense, Amount = 1850, Date = today.AddMonths(-2).AddDays(5), Description = "Elektrik + internet" },
            new() { UserId = userId, CategoryId = Id("Ulaşım"), Type = EntryType.Expense, Amount = 740, Date = today.AddMonths(-2).AddDays(8), Description = "İstanbul kart" },
            new() { UserId = userId, CategoryId = Id("Maaş"), Type = EntryType.Income, Amount = 45000, Date = today.AddMonths(-1), Description = "Aylık maaş" },
            new() { UserId = userId, CategoryId = Id("Yatırım"), Type = EntryType.Income, Amount = 1200, Date = today.AddMonths(-1).AddDays(6), Description = "Temettü" },
            new() { UserId = userId, CategoryId = Id("Market"), Type = EntryType.Expense, Amount = 4100, Date = today.AddMonths(-1).AddDays(4), Description = "Market + ev" },
            new() { UserId = userId, CategoryId = Id("Eğlence"), Type = EntryType.Expense, Amount = 950, Date = today.AddMonths(-1).AddDays(14), Description = "Sinema ve yemek" },
            new() { UserId = userId, CategoryId = Id("Sağlık"), Type = EntryType.Expense, Amount = 600, Date = today.AddMonths(-1).AddDays(18), Description = "Eczane" },
            new() { UserId = userId, CategoryId = Id("Maaş"), Type = EntryType.Income, Amount = 45000, Date = today, Description = "Aylık maaş" },
            new() { UserId = userId, CategoryId = Id("Market"), Type = EntryType.Expense, Amount = 2800, Date = today.AddDays(-6), Description = "Haftalık alışveriş" },
            new() { UserId = userId, CategoryId = Id("Faturalar"), Type = EntryType.Expense, Amount = 2100, Date = today.AddDays(-4), Description = "Doğalgaz + telefon" },
            new() { UserId = userId, CategoryId = Id("Eğitim"), Type = EntryType.Expense, Amount = 1500, Date = today.AddDays(-2), Description = "Online kurs" }
        ];
    }
}
