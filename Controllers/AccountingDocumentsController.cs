using FinansApi.Data;
using FinansApi.Data.Accounting;
using FinansApi.Dtos;
using FinansApi.Services.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Controllers;

[ApiController, Authorize, Route("api/companies/{companyId:int}")]
public sealed class AccountingDocumentsController(
    AppDbContext database,
    AccountingContext context,
    JournalService journal,
    InvoiceService invoices,
    PaymentService payments) : ControllerBase
{
    [HttpGet("journal-entries")]
    public async Task<IActionResult> Journals(
        int companyId,
        int? periodId,
        JournalStatus? status,
        DateOnly? from,
        DateOnly? to,
        int page = 1,
        int pageSize = 20)
    {
        await context.Access(companyId);
        var q = database.Set<JournalEntry>().Where(x => x.CompanyId == companyId && (!periodId.HasValue || x.FiscalPeriodId == periodId)
            && (!status.HasValue || x.Status == status)
            && (!from.HasValue || x.Date >= from)
            && (!to.HasValue || x.Date <= to));
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        return Ok(new PagedResponse<JournalEntry>(await q.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(), await q.CountAsync(), page, pageSize));
    }

    [HttpGet("journal-entries/{id:int}")]
    public async Task<IActionResult> Journal(int companyId, int id)
    {
        await context.Access(companyId);
        return Ok(await journal.Load(companyId, id));
    }

    [HttpPost("journal-entries")]
    public async Task<IActionResult> Journal(int companyId, JournalRequest request) => Ok(await journal.Save(companyId, null, request));

    [HttpPut("journal-entries/{id:int}")]
    public async Task<IActionResult> Journal(int companyId, int id, JournalRequest request) => Ok(await journal.Save(companyId, id, request));

    [HttpDelete("journal-entries/{id:int}")]
    public async Task<IActionResult> DeleteJournal(int companyId, int id)
    {
        await journal.Delete(companyId, id);
        return NoContent();
    }

    [HttpPost("journal-entries/{id:int}/post")]
    public async Task<IActionResult> PostJournal(int companyId, int id, OperationRequest request) => Ok(await journal.Post(companyId, id, request));

    [HttpPost("journal-entries/{id:int}/reverse")]
    public async Task<IActionResult> ReverseJournal(int companyId, int id, OperationRequest request) => Ok(await journal.Reverse(companyId, id, request));

    [HttpGet("invoices")]
    public async Task<IActionResult> Invoices(
        int companyId,
        int? periodId,
        InvoiceType? type,
        InvoiceStatus? status,
        int? counterpartyId,
        DateOnly? from,
        DateOnly? to,
        int page = 1,
        int pageSize = 20)
    {
        var role = await context.Access(companyId, "Sales.Read");
        var q = database.Set<Invoice>().Where(x => x.CompanyId == companyId && (!periodId.HasValue || x.FiscalPeriodId == periodId) && (!type.HasValue || x.Type == type)
            && (!status.HasValue || x.Status == status)
            && (!counterpartyId.HasValue || x.CounterpartyId == counterpartyId)
            && (!from.HasValue || x.Date >= from)
            && (!to.HasValue || x.Date <= to));
        if (role == CompanyRole.Sales)
        {
            q = q.Where(x => x.Type == InvoiceType.Sales);
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        return Ok(new PagedResponse<Invoice>(await q.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(), await q.CountAsync(), page, pageSize));
    }

    [HttpGet("invoices/{id:int}")]
    public async Task<IActionResult> Invoice(int companyId, int id)
    {
        var role = await context.Access(companyId, "Sales.Read");
        var invoice = await invoices.Load(companyId, id);
        if (role == CompanyRole.Sales && invoice.Type != InvoiceType.Sales)
        {
            return NotFound();
        }

        var paid = await invoices.Paid(id);
        var reminderPrefix = $"due:{companyId}:{id}:";
        var lastReminder = await database.Set<OutboxMessage>().Where(message => message.CompanyId == companyId && message.DeduplicationKey.StartsWith(reminderPrefix) && message.Status == "Sent").OrderByDescending(message => message.Id).Select(message => message.SentAtUtc).FirstOrDefaultAsync();
        var paymentsForInvoice = await database.Set<Payment>().Where(payment => payment.CompanyId == companyId && payment.Allocations.Any(allocation => allocation.InvoiceId == id)).Select(payment => new { payment.Id, payment.Date, payment.Direction, payment.IsReversed, Allocations = payment.Allocations.Where(allocation => allocation.InvoiceId == id).Select(allocation => new { allocation.InvoiceId, allocation.Amount }) }).ToListAsync();
        return Ok(new
        {
            Invoice = invoice,
            PaidAmount = paid,
            RemainingAmount = invoice.GrandTotal - paid,
            LastReminderAtUtc = lastReminder,
            Payments = paymentsForInvoice
        });
    }

    [HttpPost("invoices/preview")]
    public async Task<IActionResult> Preview(int companyId, InvoiceRequest request)
    {
        await context.Access(companyId, request.Type == InvoiceType.Sales ? "Invoice.DraftSales" : "Journal.Post");
        return Ok(await invoices.Calculate(companyId, request));
    }

    [HttpPost("invoices")]
    public async Task<IActionResult> Invoice(int companyId, InvoiceRequest request) => Ok(await invoices.Save(companyId, null, request));

    [HttpPut("invoices/{id:int}")]
    public async Task<IActionResult> Invoice(int companyId, int id, InvoiceRequest request) => Ok(await invoices.Save(companyId, id, request));

    [HttpDelete("invoices/{id:int}")]
    public async Task<IActionResult> DeleteInvoice(int companyId, int id)
    {
        await invoices.Delete(companyId, id);
        return NoContent();
    }

    [HttpPost("invoices/{id:int}/approve")]
    public async Task<IActionResult> Approve(int companyId, int id, OperationRequest request) => Ok(await invoices.Approve(companyId, id, request));

    [HttpPost("invoices/{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int companyId, int id, OperationRequest request) => Ok(await invoices.Cancel(companyId, id, request));

    [HttpGet("payments")]
    public async Task<IActionResult> Payments(int companyId, int? periodId, int page = 1, int pageSize = 20)
    {
        await context.Access(companyId);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var q = database.Set<Payment>().Where(x => x.CompanyId == companyId && (!periodId.HasValue || x.FiscalPeriodId == periodId));
        return Ok(new PagedResponse<Payment>(await q.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(), await q.CountAsync(), page, pageSize));
    }

    [HttpGet("payments/{id:int}")]
    public async Task<IActionResult> Payment(int companyId, int id)
    {
        await context.Access(companyId);
        var p = await database.Set<Payment>().Include(x => x.Allocations).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id);
        return p is null ? NotFound() : Ok(p);
    }

    [HttpPost("payments")]
    public async Task<IActionResult> Payment(int companyId, PaymentRequest request) => Ok(await payments.Create(companyId, request));

    [HttpPost("payments/{id:int}/reverse")]
    public async Task<IActionResult> ReversePayment(int companyId, int id, OperationRequest request) => Ok(await payments.Reverse(companyId, id, request));

    [HttpGet("transfers")]
    public async Task<IActionResult> Transfers(int companyId, int? periodId)
    {
        await context.Access(companyId);
        return Ok(await database.Set<Transfer>().Where(x => x.CompanyId == companyId && (!periodId.HasValue || x.FiscalPeriodId == periodId)).OrderByDescending(x => x.Id).ToListAsync());
    }

    [HttpPost("transfers")]
    public async Task<IActionResult> Transfer(int companyId, TransferRequest request) => Ok(await payments.Transfer(companyId, request));

}
