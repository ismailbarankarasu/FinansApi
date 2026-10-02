using FinansApi.Data;
using FinansApi.Data.Accounting;
using FinansApi.Dtos;
using FinansApi.Services.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Controllers;

[ApiController, Authorize, Route("api/companies/{companyId:int}")]
public sealed class MasterDataController(AppDbContext database, AccountingContext context, MasterDataService service, ReportService reports) : ControllerBase
{
    [HttpGet("accounts")]
    public async Task<IActionResult> Accounts(int companyId)
    {
        await context.Access(companyId);
        return Ok(await database.Set<Account>().Where(x => x.CompanyId == companyId).OrderBy(x => x.Code).ToListAsync());
    }

    [HttpPost("accounts/seed")]
    public async Task<IActionResult> Seed(int companyId) => Ok(await service.SeedAccounts(companyId));

    [HttpPost("accounts")]
    public async Task<IActionResult> Account(int companyId, AccountRequest request) => Ok(await service.Account(companyId, null, request));

    [HttpPut("accounts/{id:int}")]
    public async Task<IActionResult> Account(int companyId, int id, AccountRequest request) => Ok(await service.Account(companyId, id, request));

    [HttpGet("counterparties")]
    public async Task<IActionResult> Parties(int companyId, PartyType? type, string? q, int page = 1, int pageSize = 20)
    {
        var role = await context.Access(companyId, "Sales.Read");
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = database.Set<Counterparty>().Where(x => x.CompanyId == companyId && (!type.HasValue || x.Type == type)
            && (q == null || x.Name.Contains(q) || x.Code.Contains(q)));
        if (role == CompanyRole.Sales)
        {
            query = query.Where(x => x.Type != PartyType.Supplier);
        }

        return Ok(new PagedResponse<Counterparty>(await query.OrderBy(x => x.Code).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(), await query.CountAsync(), page, pageSize));
    }

    [HttpGet("counterparties/{id:int}")]
    public async Task<IActionResult> Party(int companyId, int id)
    {
        var role = await context.Access(companyId, "Sales.Read");
        var p = await context.Find<Counterparty>(companyId, id);
        if (role == CompanyRole.Sales && p.Type == PartyType.Supplier)
        {
            return NotFound();
        }

        return Ok(p);
    }

    [HttpPost("counterparties")]
    public async Task<IActionResult> Party(int companyId, CounterpartyRequest request) => Ok(await service.Party(companyId, null, request));

    [HttpPut("counterparties/{id:int}")]
    public async Task<IActionResult> Party(int companyId, int id, CounterpartyRequest request) => Ok(await service.Party(companyId, id, request));

    [HttpDelete("counterparties/{id:int}")]
    public async Task<IActionResult> DeactivateParty(int companyId, int id)
    {
        return Ok(await context.Write(
                companyId,
                "Journal.Post",
                async () =>
                {
                    var p = await context.Find<Counterparty>(companyId, id);
                    p.IsActive = false;
                    p.Version++;
                    context.Audit(companyId, "Deactivate", p);
                    return p;
                }));
    }

    [HttpGet("counterparties/{id:int}/statement")]
    public async Task<IActionResult> PartyStatement(int companyId, int id, int periodId, DateOnly? from, DateOnly? to)
    {
        await context.Access(companyId, "Reports.Read");
        await context.Find<Counterparty>(companyId, id);
        return Ok(await reports.Statement(companyId, periodId, id, null, from, to));
    }

    [HttpGet("products")]
    public async Task<IActionResult> Products(int companyId)
    {
        await context.Access(companyId, "Sales.Read");
        return Ok(await database.Set<Product>().Where(x => x.CompanyId == companyId).OrderBy(x => x.Code).ToListAsync());
    }

    [HttpGet("products/{id:int}")]
    public async Task<IActionResult> Product(int companyId, int id)
    {
        await context.Access(companyId, "Sales.Read");
        return Ok(await context.Find<Product>(companyId, id));
    }

    [HttpPost("products")]
    public async Task<IActionResult> Product(int companyId, ProductRequest request) => Ok(await service.Product(companyId, null, request));

    [HttpPut("products/{id:int}")]
    public async Task<IActionResult> Product(int companyId, int id, ProductRequest request) => Ok(await service.Product(companyId, id, request));

    [HttpGet("products/{id:int}/stock-movements")]
    public async Task<IActionResult> Stock(int companyId, int id, int periodId, DateOnly? from, DateOnly? to)
    {
        await context.Access(companyId);
        await context.Find<Product>(companyId, id);
        await context.Find<FiscalPeriod>(companyId, periodId);
        return Ok(await database.Set<StockMovement>().Where(x => x.CompanyId == companyId && x.ProductId == id && x.FiscalPeriodId == periodId && (!from.HasValue || x.Date >= from)
                && (!to.HasValue || x.Date <= to)).OrderBy(x => x.Id).ToListAsync());
    }

    [HttpGet("inventory/balances")]
    public async Task<IActionResult> Balances(int companyId, int periodId)
    {
        await context.Access(companyId);
        await context.Find<FiscalPeriod>(companyId, periodId);
        var movements = await database.Set<StockMovement>().Where(x => x.CompanyId == companyId && x.FiscalPeriodId == periodId).ToListAsync();
        return Ok(movements.GroupBy(x => x.ProductId).Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity), Value = g.Sum(x => x.TotalCost) }));
    }

    [HttpGet("tax-rates")]
    public async Task<IActionResult> Taxes(int companyId)
    {
        await context.Access(companyId, "Sales.Read");
        return Ok(await database.Set<TaxRate>().Where(x => x.CompanyId == companyId).ToListAsync());
    }

    [HttpPost("tax-rates")]
    public async Task<IActionResult> Tax(int companyId, TaxRateRequest request) => Ok(await service.Tax(companyId, request));

    [HttpGet("treasury-accounts")]
    public async Task<IActionResult> Treasuries(int companyId)
    {
        await context.Access(companyId);
        return Ok(await database.Set<TreasuryAccount>().Where(x => x.CompanyId == companyId).ToListAsync());
    }

    [HttpGet("treasury-accounts/{id:int}")]
    public async Task<IActionResult> Treasury(int companyId, int id)
    {
        await context.Access(companyId);
        return Ok(await context.Find<TreasuryAccount>(companyId, id));
    }

    [HttpPost("treasury-accounts")]
    public async Task<IActionResult> Treasury(int companyId, TreasuryRequest request) => Ok(await service.Treasury(companyId, null, request));

    [HttpPut("treasury-accounts/{id:int}")]
    public async Task<IActionResult> Treasury(int companyId, int id, TreasuryRequest request) => Ok(await service.Treasury(companyId, id, request));

    [HttpGet("treasury-accounts/{id:int}/statement")]
    public async Task<IActionResult> TreasuryStatement(int companyId, int id, int periodId, DateOnly? from, DateOnly? to)
    {
        await context.Access(companyId, "Reports.Read");
        await context.Find<TreasuryAccount>(companyId, id);
        return Ok(await reports.Statement(companyId, periodId, null, id, from, to));
    }

    [HttpGet("treasury-accounts/{id:int}/reconciliation")]
    public async Task<IActionResult> TreasuryReconciliation(int companyId, int id, int periodId, DateOnly? date)
    {
        await context.Access(companyId, "Reports.Read");
        var t = await context.Find<TreasuryAccount>(companyId, id);
        var rows = await reports.Rows(companyId, periodId, null, date);
        var ledger = rows.Where(x => x.AccountId == t.LedgerAccountId).Sum(x => x.Debit - x.Credit);
        var sub = rows.Where(x => x.AccountId == t.LedgerAccountId && x.TreasuryAccountId.HasValue).Sum(x => x.Debit - x.Credit);
        return Ok(new
        {
            LedgerBalance = ledger,
            SubledgerBalance = sub,
            Difference = ledger - sub,
            AccountBalance = rows.Where(x => x.TreasuryAccountId == id).Sum(x => x.Debit - x.Credit)
        });
    }

}
