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
public class CategoriesController(
    AppDbContext database,
    FinansApi.Services.Categories.CategoryService service,
    FinansApi.Services.Accounting.LegacyScope scope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CategoryResponse>>> GetAll([FromQuery, System.ComponentModel.DataAnnotations.EnumDataType(typeof(EntryType))] EntryType? type)
    {
        var userId = User.GetUserId();
        var companyId = await scope.Company(userId);
        var query = database.Categories.Where(x => x.CompanyId == companyId);
        if (type.HasValue)
        {
            query = query.Where(x => x.Type == type);
        }

        var items = await query.OrderBy(x => x.Type).ThenBy(x => x.Name).Select(x => new CategoryResponse(x.Id, x.Name, x.Type)).ToListAsync();
        return Ok(items);
    }

    [HttpPost]
    public async Task<ActionResult<CategoryResponse>> Create(CategoryRequest request) => CreatedAtAction(nameof(GetAll), await service.SaveAsync(User.GetUserId(), null, request));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CategoryResponse>> Update(int id, CategoryRequest request) => Ok(await service.SaveAsync(User.GetUserId(), id, request));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await service.DeleteAsync(User.GetUserId(), id);
        return NoContent();
    }

}
