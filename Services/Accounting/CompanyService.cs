using FinansApi.Data;
using FinansApi.Data.Accounting;
using FinansApi.Dtos;
using FinansApi.Infrastructure.Errors;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Services.Accounting;

public record CompanySummary(int Id, string Name, string? TaxNumber, string CurrencyCode, bool IsActive, CompanyRole Role);

public sealed class CompanyService(AppDbContext database, AccountingContext context)
{
    public Task<List<CompanySummary>> List() => (
        from company in database.Set<Company>()
        join membership in database.Set<CompanyMember>() on company.Id equals membership.CompanyId
        where membership.UserId == context.UserId && membership.IsActive
        select new CompanySummary(company.Id, company.Name, company.TaxNumber, company.CurrencyCode, company.IsActive, membership.Role)).ToListAsync();

    public async Task<Company> Create(CompanyRequest request)
    {
        await using var transaction = await database.Database.BeginTransactionAsync();
        var c = new Company
        {
            Name = AccountingContext.Text(request.Name, "name"),
            TaxNumber = request.TaxNumber

        };
        database.Add(c);
        await database.SaveChangesAsync();
        database.Add(new CompanyMember { CompanyId = c.Id, UserId = context.UserId, Role = CompanyRole.Admin });
        context.Audit(c.Id, "Create", c);
        await database.SaveChangesAsync();
        await transaction.CommitAsync();
        return c;
    }

    public Task<Company> Update(int companyId, CompanyRequest request) => context.Write(
        companyId,
        "Company.Manage",
        async () =>
        {
            var c = await database.Set<Company>().SingleAsync(x => x.Id == companyId);
            c.Name = AccountingContext.Text(request.Name, "name");
            c.TaxNumber = request.TaxNumber;
            c.IsActive = request.IsActive;
            context.Audit(companyId, "Update", c);
            return c;
        });

    public Task<FiscalPeriod> CreatePeriod(int companyId, PeriodRequest request) => context.Write(
        companyId,
        "Period.Close",
        async () =>
        {
            if (request.StartDate == default || request.StartDate > request.EndDate)
            {
                throw DomainException.Invalid("Dönem tarih aralığı geçersiz.");
            }

            if (await database.Set<FiscalPeriod>().AnyAsync(x => x.CompanyId == companyId && x.StartDate <= request.EndDate && x.EndDate >= request.StartDate))
            {
                throw DomainException.Conflict("Dönemler çakışıyor.");
            }

            var p = new FiscalPeriod
            {
                CompanyId = companyId,
                Name = AccountingContext.Text(request.Name, "name"),
                StartDate = request.StartDate,
                EndDate = request.EndDate

            };
            database.Add(p);
            await database.SaveChangesAsync();
            context.Audit(companyId, "Create", p);
            return p;
        });

    public Task<CompanyMember> Member(int companyId, int userId, MemberRequest request) => context.Write(
        companyId,
        "Members.Manage",
        async () =>
        {
            if (!Enum.IsDefined(request.Role))
            {
                throw DomainException.Invalid("Geçersiz rol.", "role");
            }

            if (!await database.Users.AnyAsync(x => x.Id == userId))
            {
                throw DomainException.Invalid("Kayıtlı kullanıcı bulunamadı.", "userId");
            }

            var m = await database.Set<CompanyMember>().FindAsync(companyId, userId);
            if (m is { IsActive: true, Role: CompanyRole.Admin } && (!request.IsActive || request.Role != CompanyRole.Admin)
                && await database.Set<CompanyMember>().CountAsync(x => x.CompanyId == companyId && x.IsActive && x.Role == CompanyRole.Admin) == 1)
            {
                throw DomainException.Conflict("Son aktif yönetici kaldırılamaz.");
            }

            if (m is null)
            {
                m = new()
                {
                    CompanyId = companyId,
                    UserId = userId
                };
                database.Add(m);
            }

            m.Role = request.Role;
            m.IsActive = request.IsActive;
            context.Audit(companyId, "MemberChanged", m);
            return m;
        });

}
