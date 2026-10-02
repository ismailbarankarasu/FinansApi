using System.Text.Json;
using FinansApi.Data.Accounting;
using FinansApi.Infrastructure.Errors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace FinansApi.Data;

public partial class AppDbContext
{
    private readonly Dictionary<object, string> originalAuditValues = new(ReferenceEqualityComparer.Instance);
    private void CaptureOriginalValues(object? sender, EntityTrackedEventArgs args)
    {
        if (args.Entry.State != EntityState.Added && args.Entry.Entity is not AuditLog)
        {
            originalAuditValues.TryAdd(args.Entry.Entity, SerializeAuditValues(args.Entry.Entity));
        }
    }

    public string? OriginalAuditValues(object entity) => originalAuditValues.GetValueOrDefault(entity);

    public string SerializeAuditValues(object entity)
    {
        var values = Entry(entity).Properties.ToDictionary(property => property.Metadata.Name, property => AuditValue(property.Metadata.Name, property.CurrentValue));
        return JsonSerializer.Serialize(values);
    }

    public string AuditEntityId(object entity)
    {
        var keys = Entry(entity).Properties.Where(property => property.Metadata.IsPrimaryKey());
        return string.Join("/", keys.Select(property => property.CurrentValue?.ToString() ?? ""));
    }

    private static object? AuditValue(string name, object? value)
    {
        // Audit records explain the financial change without copying credentials or contact data.
        var sensitive = name is "PasswordHash" or "Password" or "Token" or "Email" or "ContactEmail" or "Phone" or "Address" or "TaxNumber" or "Iban" or "Payload" or "ResponseJson";
        return sensitive && value is not null ? "[maskelendi]" : value;
    }

    private void ProtectAuditLog()
    {
        var changedLogs = ChangeTracker.Entries<AuditLog>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted);
        if (changedLogs)
        {
            throw DomainException.Conflict("Audit kayıtları değiştirilemez veya silinemez.");
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ProtectAuditLog();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ProtectAuditLog();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

}
