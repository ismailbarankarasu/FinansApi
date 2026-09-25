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
public class TransactionsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<TransactionResponse>>> GetAll(
        [FromQuery] EntryType? type,
        [FromQuery] int? categoryId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var userId = User.GetUserId();
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = db.Transactions
            .Include(x => x.Category)
            .Where(x => x.UserId == userId);

        if (type.HasValue)
            query = query.Where(x => x.Type == type);
        if (categoryId.HasValue)
            query = query.Where(x => x.CategoryId == categoryId);
        if (from.HasValue)
            query = query.Where(x => x.Date >= from.Value.Date);
        if (to.HasValue)
            query = query.Where(x => x.Date <= to.Value.Date);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => x.Description != null && x.Description.Contains(q));

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new TransactionResponse(
                x.Id, x.CategoryId, x.Category.Name, x.Amount, x.Date, x.Description, x.Type))
            .ToListAsync();

        return Ok(new PagedResponse<TransactionResponse>(items, totalCount, page, pageSize));
    }

    [HttpPost]
    public async Task<ActionResult<TransactionResponse>> Create(TransactionRequest request)
    {
        var result = await BuildEntry(0, request);
        if (result.Error is not null)
            return result.Error;

        db.Transactions.Add(result.Entry!);
        await db.SaveChangesAsync();
        await db.Entry(result.Entry!).Reference(x => x.Category).LoadAsync();

        return CreatedAtAction(nameof(GetAll), Map(result.Entry!));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<TransactionResponse>> Update(int id, TransactionRequest request)
    {
        var existing = await db.Transactions
            .Include(x => x.Category)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == User.GetUserId());
        if (existing is null)
            return NotFound();

        var result = await BuildEntry(id, request);
        if (result.Error is not null)
            return result.Error;

        existing.CategoryId = result.Entry!.CategoryId;
        existing.Amount = result.Entry.Amount;
        existing.Date = result.Entry.Date;
        existing.Description = result.Entry.Description;
        existing.Type = result.Entry.Type;
        await db.SaveChangesAsync();
        await db.Entry(existing).Reference(x => x.Category).LoadAsync();

        return Ok(Map(existing));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entry = await db.Transactions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == User.GetUserId());
        if (entry is null)
            return NotFound();

        db.Transactions.Remove(entry);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<(TransactionEntry? Entry, ActionResult? Error)> BuildEntry(int id, TransactionRequest request)
    {
        var userId = User.GetUserId();
        var category = await db.Categories.FirstOrDefaultAsync(x => x.Id == request.CategoryId && x.UserId == userId);
        if (category is null)
            return (null, BadRequest(new { message = "Kategori bulunamadı." }));
        if (category.Type != request.Type)
            return (null, BadRequest(new { message = "Kategori tipi ile işlem tipi uyuşmuyor." }));

        var entry = new TransactionEntry
        {
            Id = id,
            UserId = userId,
            CategoryId = category.Id,
            Amount = decimal.Round(request.Amount, 2),
            Date = request.Date.Date,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Type = request.Type,
            Category = category
        };
        return (entry, null);
    }

    private static TransactionResponse Map(TransactionEntry x) =>
        new(x.Id, x.CategoryId, x.Category.Name, x.Amount, x.Date, x.Description, x.Type);
}
