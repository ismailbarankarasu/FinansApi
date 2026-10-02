using FinansApi.Data;
using FinansApi.Data.Accounting;
using FinansApi.Infrastructure.Errors;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Services.Accounting;

public sealed class LegacyScope(AppDbContext database, AccountingContext context, IHttpContextAccessor http)
{
    public async Task<int> Company(int userId, string permission = "Read", int? requested = null)
    {
        if (!requested.HasValue && http.HttpContext?.Request.Query.TryGetValue("companyId", out var query) == true)
        {
            if (!int.TryParse(query, out var parsed))
            {
                throw DomainException.Invalid("Şirket kimliği geçersiz.", "companyId");
            }

            requested = parsed;
        }

        var companyId = requested ?? (await database.Users.SingleAsync(x => x.Id == userId)).DefaultCompanyId ?? throw DomainException.Invalid("Önce şirket oluşturun.");
        await context.Access(companyId, permission);
        return companyId;
    }

    public async Task<int> Period(int companyId, DateTime date, int? requested = null)
    {
        var day = DateOnly.FromDateTime(date);
        var p = requested.HasValue ? await context.Find<FiscalPeriod>(companyId, requested.Value) : await database.Set<FiscalPeriod>().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.StartDate <= day && x.EndDate >= day);
        if (p is null)
        {
            throw DomainException.Invalid("İşlem tarihini kapsayan mali dönem oluşturun.", "date");
        }

        await context.Open(companyId, p.Id, day);
        return p.Id;
    }

}
