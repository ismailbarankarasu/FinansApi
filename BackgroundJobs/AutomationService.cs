using System.Text.Json;
using FinansApi.Data;
using FinansApi.Data.Accounting;
using FinansApi.Infrastructure.Email;
using FinansApi.Services.Accounting;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.BackgroundJobs;

public sealed class AutomationService(
    AppDbContext database,
    InvoiceService invoices,
    IEmailSender email,
    TimeProvider clock,
    ILogger<AutomationService> logger)
{
    public async Task Schedule(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var companies = await database.Set<Company>().AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        foreach (var company in companies)
        {
            var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(company.TimeZoneId));
            if (local.Hour < company.ReminderHour)
            {
                continue;
            }

            var today = DateOnly.FromDateTime(local.DateTime);
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            if (await database.Set<ReminderRun>().AnyAsync(x => x.CompanyId == company.Id && x.BusinessDate == today, cancellationToken))
            {
                continue;
            }

            var due = await database.Set<Invoice>().Where(x => x.CompanyId == company.Id && x.Type == InvoiceType.Sales && x.DueDate < today
                && (x.Status == InvoiceStatus.Approved || x.Status == InvoiceStatus.PartiallyPaid)).ToListAsync(cancellationToken);
            foreach (var i in due)
            {
                var remaining = i.GrandTotal - await invoices.Paid(i.Id);
                if (remaining <= 0)
                {
                    continue;
                }

                var party = await database.Set<Counterparty>().SingleAsync(x => x.Id == i.CounterpartyId && x.CompanyId == company.Id, cancellationToken);
                database.Add(new OutboxMessage
                {
                    CompanyId = company.Id,
                    EventType = "OverdueInvoice",
                    DeduplicationKey = $"due:{company.Id}:{i.Id}:{today:yyyy-MM-dd}",
                    NextAttemptAtUtc = now,
                    Payload = JsonSerializer.Serialize(new EmailPayload(
                                party.Email,
                                "Vadesi geçen fatura",
                                $"{company.Name}: {i.Number} numaralı faturanın vadesi {i.DueDate:yyyy-MM-dd}; kalan tutar {remaining:0.00} TRY.",
                                i.Id))
                });
            }

            database.Add(new ReminderRun { CompanyId = company.Id, BusinessDate = today });
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public async Task Dispatch(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var candidates = (await database.Set<OutboxMessage>().AsNoTracking().Where(x => x.Status == "Pending" || x.Status == "Processing").OrderBy(x => x.Id).ToListAsync(cancellationToken)).Where(x => x.NextAttemptAtUtc <= now && (x.LockedUntilUtc is null || x.LockedUntilUtc <= now)).Take(25).ToList();
        foreach (var item in candidates)
        {
            var lease = clock.GetUtcNow().AddMinutes(5);
            var claimed = await database.Set<OutboxMessage>().Where(x => x.Id == item.Id && x.Version == item.Version).ExecuteUpdateAsync(
                s => s.SetProperty(x => x.Status, "Processing").SetProperty(x => x.Attempts, x => x.Attempts + 1).SetProperty(x => x.LockedUntilUtc, lease).SetProperty(x => x.Version, x => x.Version + 1),
                cancellationToken);
            if (claimed == 0)
            {
                continue;
            }

            item.Version++;
            item.Attempts++;
            var status = "Sent";
            string? error = null;
            try
            {
                var payload = JsonSerializer.Deserialize<EmailPayload>(item.Payload) ?? throw new InvalidOperationException("Mesaj içeriği geçersiz.");
                if (payload.InvoiceId.HasValue)
                {
                    var invoice = await database.Set<Invoice>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == payload.InvoiceId && x.CompanyId == item.CompanyId, cancellationToken);
                    if (invoice is null || invoice.Status is InvoiceStatus.Cancelled or InvoiceStatus.Paid or InvoiceStatus.Draft
                        || invoice.GrandTotal - await invoices.Paid(invoice.Id) <= 0)
                    {
                        status = "Skipped";
                    }
                    else
                    {
                        payload = payload with
                        {
                            Body = $"{invoice.Number} numaralı faturanın kalan tutarı {invoice.GrandTotal - await invoices.Paid(invoice.Id):0.00} TRY. Vade: {invoice.DueDate:yyyy-MM-dd}."
                        };
                    }
                }

                if (status != "Skipped")
                {
                    await email.Send(payload, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("E-posta gönderilemedi: mesaj {MessageId}, hata türü {ErrorType}", item.Id, ex.GetType().Name);
                status = item.Attempts >= 5 ? "Failed" : "Pending";
                error = ex is InvalidOperationException ? "SMTP, alıcı veya mesaj yapılandırmasını kontrol edin." : "E-posta teslimi başarısız; yeniden denenecek.";
            }

            var next = clock.GetUtcNow().AddMinutes(Math.Pow(2, item.Attempts));
            DateTimeOffset? sent = status == "Sent" ? clock.GetUtcNow() : null;
            await database.Set<OutboxMessage>().Where(x => x.Id == item.Id && x.Version == item.Version).ExecuteUpdateAsync(
                s => s.SetProperty(x => x.Status, status).SetProperty(x => x.Attempts, item.Attempts).SetProperty(x => x.NextAttemptAtUtc, next).SetProperty(x => x.LockedUntilUtc, (DateTimeOffset?)null).SetProperty(x => x.SentAtUtc, sent).SetProperty(x => x.LastError, error).SetProperty(x => x.Version, x => x.Version + 1),
                cancellationToken);
        }
    }

}

public sealed class OutboxDispatcher(IServiceScopeFactory scopes, IConfiguration config, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Automation:Enabled", true))
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<AutomationService>();
                await service.Schedule(stoppingToken);
                await service.Dispatch(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Otomasyon turu başarısız; sonraki turda yeniden denenecek.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

}
