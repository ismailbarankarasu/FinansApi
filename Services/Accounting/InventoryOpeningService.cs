using FinansApi.Data;
using FinansApi.Data.Accounting;
using FinansApi.Infrastructure.Errors;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Services.Accounting;

public record InventoryOpeningLine(int ProductId, decimal Quantity, decimal TotalCost);

public record InventoryOpeningRequest(
    int FiscalPeriodId,
    DateOnly Date,
    int EquityAccountId,
    List<InventoryOpeningLine> Lines,
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(100, MinimumLength = 8)] string IdempotencyKey);

public record PostingProfileRequest(
    int ReceivableAccountId,
    int PayableAccountId,
    int RevenueAccountId,
    int InputVatAccountId,
    int OutputVatAccountId,
    int ResultAccountId,
    int RetainedEarningsAccountId);

public sealed class InventoryOpeningService(
    AppDbContext database,
    AccountingContext context,
    JournalService journal,
    MasterDataService masters)
{
    public Task<JournalEntry> Create(int companyId, InventoryOpeningRequest request) => context.Write(
        companyId,
        "Journal.Post",
        async () =>
        {
            var period = await context.Open(companyId, request.FiscalPeriodId, request.Date);
            if (request.Date != period.StartDate)
            {
                throw DomainException.Invalid("Stok açılışı dönem başlangıcında olmalıdır.");
            }

            if (request.Lines is null || request.Lines.Count is < 1 or > 200
                || request.Lines.Select(x => x.ProductId).Distinct().Count() != request.Lines.Count)
            {
                throw DomainException.Invalid("Açılış satırları geçersiz.");
            }

            var equity = await masters.PostingAccount(companyId, request.EquityAccountId);
            if (equity.AccountClass != AccountClass.Equity || equity.RequiredDimension is not null)
            {
                throw DomainException.Invalid("Açılış karşı hesabı özkaynak hesabı olmalıdır.");
            }

            var lines = new List<JournalLine>();
            foreach (var item in request.Lines)
            {
                if (item.Quantity <= 0 || item.Quantity > 1000000 || decimal.Round(item.Quantity, 4) != item.Quantity)
                {
                    throw DomainException.Invalid("Miktar geçersiz.");
                }

                AccountingContext.Positive(item.TotalCost);
                var product = await context.Find<Product>(companyId, item.ProductId);
                if (!product.IsActive)
                {
                    throw DomainException.Invalid("Ürün pasif.");
                }

                if (await database.Set<StockMovement>().AnyAsync(x => x.CompanyId == companyId && x.ProductId == item.ProductId))
                {
                    throw DomainException.Conflict("Hareketi bulunan ürün için ilk açılış yapılamaz; dönem devrini kullanın.");
                }

                product.Version++;
                database.Add(new StockMovement
                {
                    CompanyId = companyId,
                    FiscalPeriodId = period.Id,
                    ProductId = product.Id,
                    Date = request.Date,
                    Quantity = item.Quantity,
                    TotalCost = item.TotalCost,
                    UnitCost = decimal.Round(item.TotalCost / item.Quantity, 4, MidpointRounding.AwayFromZero),
                    SourceType = "InitialOpening",
                    SourceId = request.IdempotencyKey
                });
                lines.Add(new()
                {
                    AccountId = product.InventoryAccountId,
                    Debit = item.TotalCost
                });
            }

            lines.Add(new()
            {
                AccountId = equity.Id,
                Credit = request.Lines.Sum(x => x.TotalCost)
            });
            return await journal.CreatePosted(companyId, period.Id, request.Date, "InitialOpening", request.IdempotencyKey, lines);
        },
        request.IdempotencyKey,
        new
        {
            Action = "Inventory.Opening",
            request
        });

    public Task<PostingProfile> Profile(int companyId, PostingProfileRequest request) => context.Write(
        companyId,
        "Journal.Post",
        async () =>
        {
            if (await database.Set<JournalEntry>().AnyAsync(x => x.CompanyId == companyId && x.Status == JournalStatus.Posted))
            {
                throw DomainException.Conflict("Hareket sonrası hesap eşlemeleri değiştirilemez.");
            }

            var definitions = new[]
            {
                (request.ReceivableAccountId, AccountClass.Asset, "Counterparty"),
                (request.PayableAccountId, AccountClass.Liability, "Counterparty"),
                (request.RevenueAccountId, AccountClass.Income, (string? )null),
                (request.InputVatAccountId, AccountClass.Asset, null),
                (request.OutputVatAccountId, AccountClass.Liability, null),
                (request.ResultAccountId, AccountClass.Equity, null),
                (request.RetainedEarningsAccountId, AccountClass.Equity, null)

            };
            if (definitions.Select(x => x.Item1).Distinct().Count() != definitions.Length)
            {
                throw DomainException.Invalid("Hesap eşlemeleri farklı hesaplar olmalıdır.");
            }

            foreach (var (id, kind, dimension) in definitions)
            {
                var a = await masters.PostingAccount(companyId, id);
                if (a.AccountClass != kind || a.RequiredDimension != dimension)
                {
                    throw DomainException.Invalid("Hesap sınıfı/boyutu eşlemeye uygun değil.");
                }
            }

            var p = await database.Set<PostingProfile>().FindAsync(companyId);
            if (p is null)
            {
                p = new()
                {
                    CompanyId = companyId
                };
                database.Add(p);
            }

            p.ReceivableAccountId = request.ReceivableAccountId;
            p.PayableAccountId = request.PayableAccountId;
            p.RevenueAccountId = request.RevenueAccountId;
            p.InputVatAccountId = request.InputVatAccountId;
            p.OutputVatAccountId = request.OutputVatAccountId;
            p.ResultAccountId = request.ResultAccountId;
            p.RetainedEarningsAccountId = request.RetainedEarningsAccountId;
            context.Audit(companyId, "PostingProfile", p);
            return p;
        });

}
