using FinansApi.Data;
using FinansApi.Data.Accounting;
using FinansApi.Infrastructure.Errors;
using FinansApi.Infrastructure.Pdf;
using FinansApi.Services.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Controllers;

[ApiController, Authorize, Route("api/companies/{companyId:int}")]
public sealed class PdfController(
    AppDbContext database,
    AccountingContext context,
    InvoiceService invoices,
    ReportService reports,
    AccountingPdfService pdf) : ControllerBase
{
    [HttpGet("invoices/{id:int}/pdf")]
    public async Task<IActionResult> Invoice(int companyId, int id)
    {
        var role = await context.Access(companyId, "Sales.Read");
        var i = await invoices.Load(companyId, id);
        if (role == CompanyRole.Sales && i.Type != InvoiceType.Sales)
        {
            return NotFound();
        }

        var c = await database.Set<Company>().SingleAsync(x => x.Id == companyId);
        var p = await context.Find<Counterparty>(companyId, i.CounterpartyId);
        return File(pdf.Invoice(c, p, i), "application/pdf", $"fatura-{i.Number}.pdf");
    }

    [HttpGet("reports/{kind}/pdf")]
    public async Task<IActionResult> Report(int companyId, string kind, int periodId, DateOnly? from, DateOnly? to)
    {
        await context.Access(companyId, "Reports.Read");
        var c = await database.Set<Company>().SingleAsync(x => x.Id == companyId);
        var p = await context.Find<FiscalPeriod>(companyId, periodId);
        object data = kind switch
        {
            "trial-balance" => await reports.Trial(companyId, periodId, from, to),
            "income-statement" => await reports.Income(companyId, periodId, from, to),
            "balance-sheet" => await reports.BalanceSheet(companyId, periodId, to),
            "vat-summary" => await reports.Vat(companyId, periodId, from, to),
            "reconciliation" => await reports.Reconcile(companyId, periodId),
            _ => throw DomainException.Missing()
        };
        if (kind == "balance-sheet")
        {
            from = null;
        }

        if (kind == "reconciliation")
        {
            from = null;
            to = null;
        }

        return File(pdf.Report(c, p, kind, from, to, data), "application/pdf", $"{kind}-{periodId}.pdf");
    }

}
