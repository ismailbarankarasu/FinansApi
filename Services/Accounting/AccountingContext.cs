using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinansApi.Auth;
using FinansApi.Data;
using FinansApi.Data.Accounting;
using FinansApi.Infrastructure.Errors;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Services.Accounting;

public sealed class AccountingContext(AppDbContext database, IHttpContextAccessor http, ILogger<AccountingContext> logger)
{
    public int UserId => http.HttpContext?.User.GetUserId() ?? throw new DomainException(401, "Giriş yapmalısınız.");

    public async Task<CompanyRole> Access(int companyId, string permission = "Read")
    {
        var member = await database.Set<CompanyMember>().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.UserId == UserId && x.IsActive);
        if (member is null
            || !await database.Set<Company>().AnyAsync(x => x.Id == companyId && (x.IsActive || permission == "Company.Manage")))
        {
            logger.LogWarning("Şirket erişimi reddedildi: kullanıcı {UserId}, şirket {CompanyId}", UserId, companyId);
            throw DomainException.Missing();
        }

        var allowed = FinansApi.Authorization.Permissions.IsGranted(member.Role, permission);
        if (!allowed)
        {
            logger.LogWarning("Yetkisiz işlem: kullanıcı {UserId}, şirket {CompanyId}, izin {Permission}", UserId, companyId, permission);
            throw new DomainException(403, "Bu işlem için yetkiniz yok.");
        }

        return member.Role;
    }

    public async Task<T> Find<T>(int companyId, int id)
    where T : CompanyEntity => await database.Set<T>().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id) ?? throw DomainException.Missing();

    public async Task<FiscalPeriod> Open(int companyId, int id, DateOnly date)
    {
        var period = await Find<FiscalPeriod>(companyId, id);
        if (period.Status != PeriodStatus.Open)
        {
            throw DomainException.Conflict("Mali dönem kilitli.");
        }

        if (date == default || date < period.StartDate || date > period.EndDate)
        {
            throw DomainException.Invalid("Tarih mali dönem dışında.", "date");
        }

        return period;
    }

    public void Audit(int companyId, string action, object entity, string? reason = null)
    {
        var id = database.AuditEntityId(entity);
        database.Set<AuditLog>().Add(new AuditLog
        {
            CompanyId = companyId,
            ActorUserId = UserId,
            Action = action,
            EntityType = entity.GetType().Name,
            EntityId = id,
            TimestampUtc = DateTimeOffset.UtcNow,
            CorrelationId = http.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString(),
            Reason = reason,
            Before = database.OriginalAuditValues(entity),
            After = action.StartsWith("Delete", StringComparison.Ordinal) ? null : database.SerializeAuditValues(entity)
        });
    }

    public async Task<T> Write<T>(int companyId, string permission, Func<Task<T>> action, string? key = null, object? request = null)
    {
        await using var transaction = await database.Database.BeginTransactionAsync();
        await Access(companyId, permission);
        string? hash = null;
        if (key is not null)
        {
            if (key.Length is < 8 or > 100)
            {
                throw DomainException.Invalid("İşlem anahtarı 8–100 karakter olmalıdır.", "idempotencyKey");
            }

            hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
            var old = await database.Set<OperationReceipt>().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Key == key);
            if (old is not null)
            {
                if (old.RequestHash != hash)
                {
                    throw DomainException.Conflict("İşlem anahtarı başka bir istek için kullanılmış.");
                }

                return JsonSerializer.Deserialize<T>(old.ResponseJson)!;
            }
        }

        var result = await action();
        await database.SaveChangesAsync();
        if (key is not null)
        {
            database.Set<OperationReceipt>().Add(new()
            {
                CompanyId = companyId,
                Key = key,
                RequestHash = hash!,
                ResponseJson = JsonSerializer.Serialize(result)
            });
            await database.SaveChangesAsync();
        }

        await transaction.CommitAsync();
        return result;
    }

    public static void Version(CompanyEntity entity, int version)
    {
        if (entity.Version != version)
        {
            throw DomainException.Conflict("Kayıt değişmiş. Güncel sürümü yükleyin.");
        }

        entity.Version++;
    }

    public static string Text(string? value, string field, int max = 160)
    {
        var text = value?.Trim() ?? "";
        if (text.Length == 0 || text.Length > max)
        {
            throw DomainException.Invalid($"{field}: 1–{max} karakter girin.", field);
        }

        return text;
    }

    public static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    public static void Positive(decimal value, string field = "amount")
    {
        if (value <= 0 || value > 100000000m || Money(value) != value)
        {
            throw DomainException.Invalid("Pozitif, en fazla iki ondalıklı ve 100.000.000 sınırında tutar girin.", field);
        }
    }

    public static string Number(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

}
