using FinansApi.Data;
using FinansApi.Data.Accounting;
using FinansApi.Services.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Controllers;

[ApiController, Authorize, Route("api/companies/{companyId:int}/reports")]
public sealed class FinancialReportsController(AppDbContext database, AccountingContext context, ReportService reports, InvoiceService invoices) : ControllerBase
{
    [HttpGet("trial-balance")]
    public async Task<IActionResult> Trial(int companyId, int periodId, DateOnly? from, DateOnly? to)
    {
        await context.Access(companyId, "Reports.Read");
        return Ok(await reports.Trial(companyId, periodId, from, to));
    }

    [HttpGet("income-statement")]
    public async Task<IActionResult> Income(int companyId, int periodId, DateOnly? from, DateOnly? to)
    {
        await context.Access(companyId, "Reports.Read");
        return Ok(await reports.Income(companyId, periodId, from, to));
    }

    [HttpGet("balance-sheet")]
    public async Task<IActionResult> Balance(int companyId, int periodId, DateOnly? to)
    {
        await context.Access(companyId, "Reports.Read");
        return Ok(await reports.BalanceSheet(companyId, periodId, to));
    }

    [HttpGet("vat-summary")]
    public async Task<IActionResult> Vat(int companyId, int periodId, DateOnly? from, DateOnly? to)
    {
        await context.Access(companyId, "Reports.Read");
        return Ok(await reports.Vat(companyId, periodId, from, to));
    }

    [HttpGet("reconciliation")]
    public async Task<IActionResult> Reconcile(int companyId, int periodId)
    {
        await context.Access(companyId, "Reports.Read");
        return Ok(await reports.Reconcile(companyId, periodId));
    }

    [HttpGet("overdue")]
    public async Task<IActionResult> Overdue(int companyId, InvoiceType? type)
    {
        await context.Access(companyId, "Reports.Read");
        var company = await database.Set<Company>().SingleAsync(x => x.Id == companyId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(company.TimeZoneId)).DateTime);
        var rows = await database.Set<Invoice>().Where(x => x.CompanyId == companyId && (!type.HasValue || x.Type == type) && x.DueDate < today
            && (x.Status == InvoiceStatus.Approved || x.Status == InvoiceStatus.PartiallyPaid)).ToListAsync();
        var result = new List<object>();
        foreach (var i in rows)
        {
            var remaining = i.GrandTotal - await invoices.Paid(i.Id);
            if (remaining > 0)
            {
                result.Add(new
                {
                    Invoice = i,
                    RemainingAmount = remaining,
                    DaysOverdue = today.DayNumber - i.DueDate.DayNumber
                });
            }
        }

        return Ok(result);
    }

}
