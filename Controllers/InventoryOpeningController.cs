using FinansApi.Data;
using FinansApi.Data.Accounting;
using FinansApi.Services.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinansApi.Controllers;

[ApiController, Authorize, Route("api/companies/{companyId:int}")]
public sealed class InventoryOpeningController(AppDbContext database, AccountingContext context, InventoryOpeningService service) : ControllerBase
{
    [HttpPost("inventory/opening")]
    public async Task<IActionResult> Create(int companyId, InventoryOpeningRequest request) => Ok(await service.Create(companyId, request));

    [HttpGet("posting-profile")]
    public async Task<IActionResult> Profile(int companyId)
    {
        await context.Access(companyId);
        return Ok(await database.Set<PostingProfile>().FindAsync(companyId));
    }

    [HttpPut("posting-profile")]
    public async Task<IActionResult> Profile(int companyId, PostingProfileRequest request) => Ok(await service.Profile(companyId, request));

}
