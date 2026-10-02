using System.Globalization;
using FinansApi.Auth;
using FinansApi.Data;
using FinansApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Controllers;

[ApiController]
[Authorize]
[Route("api/reports")]
public class ReportsController(AppDbContext database, FinansApi.Services.Accounting.LegacyScope scope) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<SummaryResponse>> Summary([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var amounts = await Filter(await scope.Company(User.GetUserId()), from, to).Select(x => new { x.Type, x.Amount }).ToListAsync();
        var income = amounts.Where(x => x.Type == EntryType.Income).Sum(x => x.Amount);
        var expense = amounts.Where(x => x.Type == EntryType.Expense).Sum(x => x.Amount);
        return Ok(new SummaryResponse(income, expense, income - expense, amounts.Count));
    }

    [HttpGet("by-category")]
    public async Task<ActionResult<IEnumerable<CategoryReportItem>>> ByCategory([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery, System.ComponentModel.DataAnnotations.EnumDataType(typeof(EntryType))] EntryType? type)
    {
        var query = Filter(await scope.Company(User.GetUserId()), from, to);
        if (type.HasValue)
        {
            query = query.Where(x => x.Type == type);
        }

        var rows = await query.Select(x => new { x.CategoryId, CategoryName = x.Category.Name, x.Type, x.Amount }).ToListAsync();
        var items = rows.GroupBy(x => new { x.CategoryId, x.CategoryName, x.Type }).Select(g => new CategoryReportItem(g.Key.CategoryId, g.Key.CategoryName, g.Key.Type, g.Sum(x => x.Amount))).OrderByDescending(x => x.Total).ToList();
        return Ok(items);
    }

    [HttpGet("monthly")]
    public async Task<ActionResult<IEnumerable<MonthlyReportItem>>> Monthly([FromQuery, System.ComponentModel.DataAnnotations.Range(1, 9999)] int? year)
    {
        var selectedYear = year ?? DateTime.Today.Year;
        var userId = User.GetUserId();
        var companyId = await scope.Company(userId);
        var culture = new CultureInfo("tr-TR");
        var start = new DateTime(selectedYear, 1, 1);
        var end = new DateTime(selectedYear, 12, 31);
        var rows = await database.Transactions.Where(x => x.CompanyId == companyId && x.Date >= start && x.Date <= end).ToListAsync();
        var result = Enumerable.Range(1, 12).Select(month =>
            {
                var monthRows = rows.Where(x => x.Date.Month == month).ToList();
                return new MonthlyReportItem(
                    month,
                    culture.DateTimeFormat.GetMonthName(month),
                    monthRows.Where(x => x.Type == EntryType.Income).Sum(x => x.Amount),
                    monthRows.Where(x => x.Type == EntryType.Expense).Sum(x => x.Amount));
            }).ToList();
        return Ok(result);
    }

    [HttpGet("recent")]
    public async Task<ActionResult<IEnumerable<TransactionResponse>>> Recent([FromQuery] int take = 5)
    {
        var companyId = await scope.Company(User.GetUserId());
        take = Math.Clamp(take, 1, 20);
        var items = await database.Transactions.Include(x => x.Category).Where(x => x.CompanyId == companyId).OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).Take(take).Select(x => new TransactionResponse(x.Id, x.CategoryId, x.Category.Name, x.Amount, x.Date, x.Description, x.Type)).ToListAsync();
        return Ok(items);
    }

    private IQueryable<TransactionEntry> Filter(int companyId, DateTime? from, DateTime? to)
    {
        if (from > to)
            throw FinansApi.Infrastructure.Errors.DomainException.Invalid("Başlangıç tarihi bitiş tarihinden sonra olamaz.");
        var query = database.Transactions.Include(x => x.Category).Where(x => x.CompanyId == companyId);
        if (from.HasValue)
        {
            query = query.Where(x => x.Date >= from.Value.Date);
        }

        if (to.HasValue)
        {
            query = query.Where(x => x.Date <= to.Value.Date);
        }

        return query;
    }

}
