using FinansApi.Auth;
using FinansApi.Data;
using FinansApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Controllers;

[ApiController]
[Authorize]
[Route("api/categories")]
public class CategoriesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CategoryResponse>>> GetAll([FromQuery] EntryType? type)
    {
        var userId = User.GetUserId();
        var query = db.Categories.Where(x => x.UserId == userId);
        if (type.HasValue)
            query = query.Where(x => x.Type == type);

        var items = await query
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Name)
            .Select(x => new CategoryResponse(x.Id, x.Name, x.Type))
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost]
    public async Task<ActionResult<CategoryResponse>> Create(CategoryRequest request)
    {
        var userId = User.GetUserId();
        var name = request.Name.Trim();
        var exists = await db.Categories.AnyAsync(x =>
            x.UserId == userId && x.Name == name && x.Type == request.Type);
        if (exists)
            return Conflict(new { message = "Bu kategori zaten var." });

        var category = new Category
        {
            UserId = userId,
            Name = name,
            Type = request.Type
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), new CategoryResponse(category.Id, category.Name, category.Type));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CategoryResponse>> Update(int id, CategoryRequest request)
    {
        var userId = User.GetUserId();
        var category = await db.Categories.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (category is null)
            return NotFound();

        category.Name = request.Name.Trim();
        category.Type = request.Type;
        await db.SaveChangesAsync();

        return Ok(new CategoryResponse(category.Id, category.Name, category.Type));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = User.GetUserId();
        var category = await db.Categories.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (category is null)
            return NotFound();

        var hasTransactions = await db.Transactions.AnyAsync(x => x.CategoryId == id);
        if (hasTransactions)
            return BadRequest(new { message = "Bu kategoriye bağlı işlemler var, silemezsiniz." });

        db.Categories.Remove(category);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
