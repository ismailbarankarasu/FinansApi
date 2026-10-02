using FinansApi.Data;
using FinansApi.Dtos;
using FinansApi.Infrastructure.Errors;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Services.Categories;

public sealed class CategoryService(
    AppDbContext database,
    FinansApi.Services.Accounting.LegacyScope scope,
    FinansApi.Services.Accounting.AccountingContext context)
{
    public async Task<CategoryResponse> SaveAsync(int userId, int? id, CategoryRequest request)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length is < 2 or > 80)
        {
            throw DomainException.Invalid("Kategori adı 2–80 karakter olmalıdır.", "name");
        }

        if (!Enum.IsDefined(request.Type))
        {
            throw DomainException.Invalid("Geçersiz işlem türü.", "type");
        }

        await using var transaction = await database.Database.BeginTransactionAsync();
        var companyId = await scope.Company(userId, "Journal.Post", request.CompanyId);
        var category = id.HasValue ? await database.Categories.SingleOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId) ?? throw DomainException.Missing() : new Category
        {
            UserId = userId,
            CompanyId = companyId
        };
        if (await database.Categories.AnyAsync(x => x.CompanyId == companyId && x.Id != category.Id && x.Name == name && x.Type == request.Type))
        {
            throw DomainException.Conflict("Bu kategori zaten var.");
        }

        if (id.HasValue && category.Type != request.Type && await database.Transactions.AnyAsync(x => x.CategoryId == id))
        {
            throw DomainException.Conflict("İşlemlerde kullanılan kategorinin türü değiştirilemez.");
        }

        category.Name = name;
        category.Type = request.Type;
        if (!id.HasValue)
        {
            database.Categories.Add(category);
        }

        await database.SaveChangesAsync();
        context.Audit(companyId, "CategoryChange", category);
        await database.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(category.Id, category.Name, category.Type);
    }

    public async Task DeleteAsync(int userId, int id)
    {
        await using var transaction = await database.Database.BeginTransactionAsync();
        var companyId = await scope.Company(userId, "Journal.Post");
        var category = await database.Categories.SingleOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId) ?? throw DomainException.Missing();
        if (await database.Transactions.AnyAsync(x => x.CategoryId == id))
        {
            throw DomainException.Conflict("Bu kategoriye bağlı işlemler var, silemezsiniz.");
        }

        context.Audit(companyId, "DeleteCategory", category);
        database.Categories.Remove(category);
        await database.SaveChangesAsync();
        await transaction.CommitAsync();
    }

}
