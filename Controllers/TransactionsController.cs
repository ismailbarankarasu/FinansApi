using FinansApi.Auth;
using FinansApi.Data;
using FinansApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Controllers;

[ApiController]
[Authorize]
[Route("api/transactions")]
public class TransactionsController(
    AppDbContext database,
    FinansApi.Services.Transactions.TransactionService service,
    FinansApi.Services.Accounting.LegacyScope scope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<TransactionResponse>>> GetAll(
        [FromQuery, System.ComponentModel.DataAnnotations.EnumDataType(typeof(EntryType))] EntryType? type,
        [FromQuery] int? categoryId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var userId = User.GetUserId();
        var companyId = await scope.Company(userId);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var query = database.Transactions.Include(x => x.Category).Where(x => x.CompanyId == companyId);
        if (type.HasValue)
        {
            query = query.Where(x => x.Type == type);
        }

        if (categoryId.HasValue)
        {
            query = query.Where(x => x.CategoryId == categoryId);
        }

        if (from.HasValue)
        {
            query = query.Where(x => x.Date >= from.Value.Date);
        }

        if (to.HasValue)
        {
            query = query.Where(x => x.Date <= to.Value.Date);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(x => x.Description != null && x.Description.Contains(q));
        }

        if (from > to)
            throw FinansApi.Infrastructure.Errors.DomainException.Invalid("Başlangıç tarihi bitiş tarihinden sonra olamaz.");
        var totalCount = await query.CountAsync();
        page = Math.Min(page, Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize)));
        var items = await query.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).Select(x => new TransactionResponse(x.Id, x.CategoryId, x.Category.Name, x.Amount, x.Date, x.Description, x.Type)).ToListAsync();
        return Ok(new PagedResponse<TransactionResponse>(items, totalCount, page, pageSize));
    }

    [HttpPost]
    public async Task<ActionResult<TransactionResponse>> Create(TransactionRequest request) => CreatedAtAction(nameof(GetAll), await service.SaveAsync(User.GetUserId(), null, request));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<TransactionResponse>> Update(int id, TransactionRequest request) => Ok(await service.SaveAsync(User.GetUserId(), id, request));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await service.DeleteAsync(User.GetUserId(), id);
        return NoContent();
    }

}
