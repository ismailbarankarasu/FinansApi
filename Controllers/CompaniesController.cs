using FinansApi.Data;
using FinansApi.Data.Accounting;
using FinansApi.Dtos;
using FinansApi.Services.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Controllers;

[ApiController, Authorize, Route("api/companies")]
public sealed class CompaniesController(AppDbContext database, AccountingContext context, CompanyService service, ClosingService closing) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await service.List());

    [HttpPost]
    public async Task<IActionResult> Create(CompanyRequest request) => Ok(await service.Create(request));

    [HttpGet("{companyId:int}")]
    public async Task<IActionResult> Get(int companyId)
    {
        await context.Access(companyId, "Sales.Read");
        return Ok(await database.Set<Company>().FindAsync(companyId));
    }

    [HttpPut("{companyId:int}")]
    public async Task<IActionResult> Update(int companyId, CompanyRequest request) => Ok(await service.Update(companyId, request));

    [HttpGet("{companyId:int}/periods")]
    public async Task<IActionResult> Periods(int companyId)
    {
        await context.Access(companyId, "Sales.Read");
        return Ok(await database.Set<FiscalPeriod>().Where(x => x.CompanyId == companyId).OrderBy(x => x.StartDate).ToListAsync());
    }

    [HttpGet("{companyId:int}/periods/{id:int}")]
    public async Task<IActionResult> Period(int companyId, int id)
    {
        await context.Access(companyId, "Sales.Read");
        return Ok(await context.Find<FiscalPeriod>(companyId, id));
    }

    [HttpPost("{companyId:int}/periods")]
    public async Task<IActionResult> CreatePeriod(int companyId, PeriodRequest request) => Ok(await service.CreatePeriod(companyId, request));

    [HttpGet("{companyId:int}/members")]
    public async Task<IActionResult> Members(int companyId)
    {
        await context.Access(companyId, "Members.Manage");
        return Ok(await database.Set<CompanyMember>().Where(x => x.CompanyId == companyId).ToListAsync());
    }

    [HttpPost("{companyId:int}/members")]
    public async Task<IActionResult> AddMember(int companyId, MemberRequest request) => Ok(await service.Member(companyId, request.UserId, request));

    [HttpPut("{companyId:int}/members/{userId:int}")]
    public async Task<IActionResult> Member(int companyId, int userId, MemberRequest request) => Ok(await service.Member(companyId, userId, request));

    [HttpDelete("{companyId:int}/members/{userId:int}")]
    public async Task<IActionResult> RemoveMember(int companyId, int userId)
    {
        await context.Access(companyId, "Members.Manage");
        var member = await database.Set<CompanyMember>().FindAsync(companyId, userId);
        if (member is null)
        {
            return NotFound();
        }

        return Ok(await service.Member(companyId, userId, new(userId, member.Role, false)));
    }

    [HttpGet("{companyId:int}/audit-logs")]
    public async Task<IActionResult> Audit(
        int companyId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? entityType,
        int page = 1,
        int pageSize = 20)
    {
        await context.Access(companyId, "Audit.Read");
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var rows = await database.Set<AuditLog>().Where(x => x.CompanyId == companyId && (entityType == null || x.EntityType == entityType)).OrderByDescending(x => x.Id).ToListAsync();
        var query = rows.Where(x => (!from.HasValue || x.TimestampUtc >= from) && (!to.HasValue || x.TimestampUtc <= to)).ToList();
        return Ok(new PagedResponse<AuditLog>(query.Skip((page - 1) * pageSize).Take(pageSize).ToList(), query.Count, page, pageSize));
    }

    [HttpGet("{companyId:int}/periods/{id:int}/closing-preview")]
    public async Task<IActionResult> Preview(int companyId, int id, int? targetPeriodId)
    {
        await context.Access(companyId, "Period.Close");
        return Ok(await closing.Preview(companyId, id, targetPeriodId));
    }

    [HttpPost("{companyId:int}/periods/{id:int}/close-and-carry-forward")]
    public async Task<IActionResult> Close(int companyId, int id, ClosingRequest request) => Ok(await closing.Close(companyId, id, request));

    [HttpGet("{companyId:int}/periods/{id:int}/carry-forward-result")]
    public async Task<IActionResult> Result(int companyId, int id)
    {
        await context.Access(companyId, "Period.Close");
        return Ok(await database.Set<CarryForwardRun>().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SourcePeriodId == id));
    }

}
