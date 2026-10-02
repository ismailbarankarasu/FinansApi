using FinansApi.Data.Accounting;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Data;

public static class LegacyCompanyMapping
{
    public static async Task Apply(AppDbContext database, int? userId = null)
    {
        await using var transaction = database.Database.CurrentTransaction is null ? await database.Database.BeginTransactionAsync() : null;
        foreach (var user in await database.Users.Where(user => !userId.HasValue || user.Id == userId).ToListAsync())
        {
            if (!user.DefaultCompanyId.HasValue)
            {
                var c = new Company
                {
                    Name = user.FullName + " şirketi"
                };
                database.Add(c);
                await database.SaveChangesAsync();
                user.DefaultCompanyId = c.Id;
                database.Add(new CompanyMember { CompanyId = c.Id, UserId = user.Id, Role = CompanyRole.Admin });
            }

            var companyId = user.DefaultCompanyId.Value;
            if (!await database.Set<FiscalPeriod>().AnyAsync(x => x.CompanyId == companyId))
            {
                database.Add(new FiscalPeriod
                {
                    CompanyId = companyId,
                    Name = DateTime.UtcNow.Year.ToString(),
                    StartDate = new DateOnly(DateTime.UtcNow.Year, 1, 1),
                    EndDate = new DateOnly(DateTime.UtcNow.Year, 12, 31)
                });
            }

            await database.SaveChangesAsync();
            foreach (var category in await database.Categories.Where(x => x.UserId == user.Id && x.CompanyId == null).ToListAsync())
            {
                category.CompanyId = companyId;
            }

            var entries = await database.Transactions.Where(x => x.UserId == user.Id && (x.CompanyId == null || x.FiscalPeriodId == null)).ToListAsync();
            foreach (var group in entries.GroupBy(x => x.Date.Year))
            {
                var start = new DateOnly(group.Key, 1, 1);
                var end = new DateOnly(group.Key, 12, 31);
                var period = await database.Set<FiscalPeriod>().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.StartDate == start && x.EndDate == end);
                if (period is null)
                {
                    period = new FiscalPeriod
                    {
                        CompanyId = companyId,
                        Name = group.Key.ToString(),
                        StartDate = start,
                        EndDate = end

                    };
                    database.Add(period);
                    await database.SaveChangesAsync();
                }

                foreach (var e in group)
                {
                    e.CompanyId = companyId;
                    e.FiscalPeriodId = period.Id;
                }
            }
        }

        await database.SaveChangesAsync();
        if (transaction is not null)
        {
            await transaction.CommitAsync();
        }
    }

}
