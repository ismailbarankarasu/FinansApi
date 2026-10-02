using FinansApi.Data;
using FinansApi.Dtos;
using FinansApi.Infrastructure.Errors;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Services.Transactions;

public sealed class TransactionService(
    AppDbContext database,
    FinansApi.Services.Accounting.LegacyScope scope,
    FinansApi.Services.Accounting.AccountingContext context)
{
    public async Task<TransactionResponse> SaveAsync(int userId, int? id, TransactionRequest request)
    {
        if (!Enum.IsDefined(request.Type))
        {
            throw DomainException.Invalid("Geçersiz işlem türü.", "type");
        }

        if (request.Amount is < 0.01m or > 100000000m)
        {
            throw DomainException.Invalid("Tutar 0,01 ile 100.000.000 arasında olmalıdır.", "amount");
        }

        if (request.Date == default)
        {
            throw DomainException.Invalid("İşlem tarihi zorunludur.", "date");
        }

        if (request.Description?.Length > 250)
        {
            throw DomainException.Invalid("Açıklama en fazla 250 karakter olabilir.", "description");
        }

        await using var transaction = await database.Database.BeginTransactionAsync();
        var companyId = await scope.Company(userId, "Journal.Post", request.CompanyId);
        var periodId = await scope.Period(companyId, request.Date, request.FiscalPeriodId);
        var entry = id.HasValue ? await database.Transactions.SingleOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId) ?? throw DomainException.Missing() : new TransactionEntry
        {
            UserId = userId,
            CompanyId = companyId
        };
        var category = await database.Categories.SingleOrDefaultAsync(x => x.Id == request.CategoryId && x.CompanyId == companyId) ?? throw DomainException.Invalid("Kategori bulunamadı.", "categoryId");
        if (category.Type != request.Type)
        {
            throw DomainException.Invalid("Kategori tipi ile işlem tipi uyuşmuyor.", "type");
        }

        if (id.HasValue)
        {
            await scope.Period(companyId, entry.Date, entry.FiscalPeriodId);
        }

        entry.FiscalPeriodId = periodId;
        entry.CategoryId = category.Id;
        entry.Category = category;
        entry.Amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        entry.Date = DateTime.SpecifyKind(request.Date.Date, DateTimeKind.Unspecified);
        entry.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        entry.Type = request.Type;
        if (!id.HasValue)
        {
            database.Transactions.Add(entry);
        }

        await database.SaveChangesAsync();
        context.Audit(companyId, "TransactionSave", entry);
        await database.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(entry.Id, category.Id, category.Name, entry.Amount, entry.Date, entry.Description, entry.Type);
    }

    public async Task DeleteAsync(int userId, int id)
    {
        await using var transaction = await database.Database.BeginTransactionAsync();
        var companyId = await scope.Company(userId, "Journal.Post");
        var entry = await database.Transactions.SingleOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId) ?? throw DomainException.Missing();
        await scope.Period(companyId, entry.Date, entry.FiscalPeriodId);
        context.Audit(companyId, "TransactionDelete", entry);
        database.Transactions.Remove(entry);
        await database.SaveChangesAsync();
        await transaction.CommitAsync();
    }

}
