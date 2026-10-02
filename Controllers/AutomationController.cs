using FinansApi.Data;
using FinansApi.Data.Accounting;
using FinansApi.Dtos;
using FinansApi.Services.Accounting;
using FinansApi.Infrastructure.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Controllers;

[ApiController, Authorize, Route("api/companies/{companyId:int}/automation")]
public sealed class AutomationController(AppDbContext database, AccountingContext context) : ControllerBase
{
    [HttpGet("messages")]
    public async Task<IActionResult> Messages(int companyId, string? status, int page = 1, int pageSize = 20)
    {
        await context.Access(companyId, "Audit.Read");
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var q = database.Set<OutboxMessage>().Where(x => x.CompanyId == companyId && (status == null || x.Status == status));
        return Ok(new
        {
            Items = await q.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).Select(x => new { x.Id, x.EventType, x.Status, x.Attempts, x.NextAttemptAtUtc, x.SentAtUtc, x.LastError }).ToListAsync(),
            TotalCount = await q.CountAsync(),
            Page = page,
            PageSize = pageSize
        });
    }

    [HttpPost("messages/{id:int}/retry")]
    public async Task<IActionResult> Retry(int companyId, int id) => Ok(await context.Write(
            companyId,
            "Journal.Post",
            async () =>
            {
                var m = await context.Find<OutboxMessage>(companyId, id);
                if (m.Status != "Failed")
                {
                    throw DomainException.Conflict("Yalnız başarısız mesaj yeniden kuyruğa alınabilir.");
                }

                m.Status = "Pending";
                m.Attempts = 0;
                m.NextAttemptAtUtc = DateTimeOffset.UtcNow;
                m.LockedUntilUtc = null;
                m.Version++;
                context.Audit(companyId, "Retry", m);
                return new
                {
                    m.Id,
                    m.Status
                };
            }));

    [HttpGet("settings")]
    public async Task<IActionResult> Settings(int companyId)
    {
        await context.Access(companyId, "Company.Manage");
        var c = await database.Set<Company>().SingleAsync(x => x.Id == companyId);
        return Ok(new NotificationSettingsRequest(c.TimeZoneId, c.ReminderHour, c.ContactEmail));
    }

    [HttpPut("settings")]
    public async Task<IActionResult> Settings(int companyId, NotificationSettingsRequest request) => Ok(await context.Write(
            companyId,
            "Company.Manage",
            async () =>
            {
                if (request.ReminderHour is < 0 or > 23)
                {
                    throw DomainException.Invalid("Hatırlatma saati 0–23 arasında olmalıdır.");
                }

                try
                {
                    TimeZoneInfo.FindSystemTimeZoneById(request.TimeZoneId);
                }
                catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentNullException)
                {
                    throw DomainException.Invalid("Saat dilimi geçersiz.");
                }

                if (request.ContactEmail is not null
                    && !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(request.ContactEmail))
                {
                    throw DomainException.Invalid("E-posta geçersiz.");
                }

                var c = await database.Set<Company>().SingleAsync(x => x.Id == companyId);
                c.TimeZoneId = request.TimeZoneId;
                c.ReminderHour = request.ReminderHour;
                c.ContactEmail = request.ContactEmail;
                context.Audit(companyId, "NotificationSettings", c);
                return request;
            }));

}
