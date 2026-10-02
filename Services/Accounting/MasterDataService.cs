using FinansApi.Data;
using FinansApi.Data.Accounting;
using FinansApi.Dtos;
using FinansApi.Infrastructure.Errors;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Services.Accounting;

public sealed class MasterDataService(AppDbContext database, AccountingContext context)
{
    public Task<List<Account>> SeedAccounts(int companyId) => context.Write(
        companyId,
        "Journal.Post",
        async () =>
        {
            if (await database.Set<Account>().AnyAsync(x => x.CompanyId == companyId))
            {
                return await database.Set<Account>().Where(x => x.CompanyId == companyId).ToListAsync();
            }

            var definitions = new (string Code, string Name, AccountClass Class, string? Dimension)[]
            {
                ("100.01", "Kasa", AccountClass.Asset, "Treasury"),
                ("102.01", "Banka", AccountClass.Asset, "Treasury"),
                ("120.01", "Alıcılar", AccountClass.Asset, "Counterparty"),
                ("153.01", "Ticari mallar", AccountClass.Asset, null),
                ("191.01", "İndirilecek KDV", AccountClass.Asset, null),
                ("320.01", "Satıcılar", AccountClass.Liability, "Counterparty"),
                ("391.01", "Hesaplanan KDV", AccountClass.Liability, null),
                ("500.01", "Sermaye", AccountClass.Equity, null),
                ("570.01", "Geçmiş yıl sonucu", AccountClass.Equity, null),
                ("590.01", "Dönem sonucu", AccountClass.Equity, null),
                ("600.01", "Satışlar", AccountClass.Income, null),
                ("621.01", "Satılan mal maliyeti", AccountClass.Expense, null)

            };
            var accounts = definitions.Select(d => new Account
            {
                CompanyId = companyId,
                Code = d.Code,
                Name = d.Name,
                AccountClass = d.Class,
                RequiredDimension = d.Dimension
            }).ToList();
            database.AddRange(accounts);
            await database.SaveChangesAsync();
            int Id(string code) => accounts.Single(x => x.Code == code).Id;
            database.Add(new PostingProfile
            {
                CompanyId = companyId,
                ReceivableAccountId = Id("120.01"),
                PayableAccountId = Id("320.01"),
                RevenueAccountId = Id("600.01"),
                InputVatAccountId = Id("191.01"),
                OutputVatAccountId = Id("391.01"),
                ResultAccountId = Id("590.01"),
                RetainedEarningsAccountId = Id("570.01")
            });
            context.Audit(companyId, "SeedAccounts", accounts[0]);
            return accounts;
        });

    public Task<Account> Account(int companyId, int? id, AccountRequest request) => context.Write(
        companyId,
        "Journal.Post",
        async () =>
        {
            var code = AccountingContext.Text(request.Code, "code", 30);
            if ((code.StartsWith("120") || code.StartsWith("320")) && request.RequiredDimension != "Counterparty")
            {
                throw DomainException.Invalid("Cari kontrol hesaplarında cari boyutu zorunludur.");
            }

            if ((code.StartsWith("100") || code.StartsWith("102")) && request.RequiredDimension != "Treasury")
            {
                throw DomainException.Invalid("Kasa/banka hesaplarında alt hesap boyutu zorunludur.");
            }

            if (!Enum.IsDefined(request.AccountClass) || request.RequiredDimension is not (null or "Counterparty" or "Treasury"))
            {
                throw DomainException.Invalid("Geçersiz hesap sınıfı/boyutu.");
            }

            var a = id.HasValue ? await context.Find<Account>(companyId, id.Value) : new Account
            {
                CompanyId = companyId
            };
            if (id.HasValue)
            {
                AccountingContext.Version(a, request.Version);
                if (await database.Set<JournalLine>().AnyAsync(x => x.AccountId == id)
                    && (a.Code != code || a.AccountClass != request.AccountClass || a.ParentId != request.ParentId
                        || a.RequiredDimension != request.RequiredDimension
                        || a.IsPostingAllowed != request.IsPostingAllowed))
                {
                    throw DomainException.Conflict("Kullanılan hesabın yapısı değiştirilemez.");
                }
            }

            if (request.ParentId.HasValue)
            {
                var visited = new HashSet<int>();
                var parent = await context.Find<Account>(companyId, request.ParentId.Value);
                if (parent.IsPostingAllowed)
                {
                    throw DomainException.Invalid("Üst hesap kayıt kabul etmemelidir.");
                }

                while (true)
                {
                    if (parent.Id == id || !visited.Add(parent.Id))
                    {
                        throw DomainException.Invalid("Hesap hiyerarşisinde döngü oluşuyor.");
                    }

                    if (!parent.ParentId.HasValue)
                    {
                        break;
                    }

                    parent = await context.Find<Account>(companyId, parent.ParentId.Value);
                }
            }

            if (request.IsPostingAllowed && id.HasValue && await database.Set<Account>().AnyAsync(x => x.ParentId == id))
            {
                throw DomainException.Invalid("Alt hesabı bulunan hesap kayıt kabul edemez.");
            }

            a.Code = AccountingContext.Text(request.Code, "code", 30);
            a.Name = AccountingContext.Text(request.Name, "name");
            a.ParentId = request.ParentId;
            a.AccountClass = request.AccountClass;
            a.IsPostingAllowed = request.IsPostingAllowed;
            a.IsActive = request.IsActive;
            a.RequiredDimension = request.RequiredDimension;
            if (!id.HasValue)
            {
                database.Add(a);
            }

            await database.SaveChangesAsync();
            context.Audit(companyId, "Save", a);
            return a;
        });

    public Task<Counterparty> Party(int companyId, int? id, CounterpartyRequest request) => context.Write(
        companyId,
        request.Type == PartyType.Customer ? "Customer.Write" : "Journal.Post",
        async () =>
        {
            if (!Enum.IsDefined(request.Type) || request.PaymentTermDays is < 0 or > 3650)
            {
                throw DomainException.Invalid("Cari türü veya vade geçersiz.");
            }

            if (request.Email is not null && !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(request.Email))
            {
                throw DomainException.Invalid("E-posta geçersiz.", "email");
            }

            var a = id.HasValue ? await context.Find<Counterparty>(companyId, id.Value) : new Counterparty
            {
                CompanyId = companyId
            };
            if (id.HasValue)
            {
                if (a.Type != PartyType.Customer)
                {
                    await context.Access(companyId, "Journal.Post");
                }

                AccountingContext.Version(a, request.Version);
                if (a.Type != request.Type
                    && (await database.Set<JournalLine>().AnyAsync(x => x.CounterpartyId == id)
                        || await database.Set<Invoice>().AnyAsync(x => x.CounterpartyId == id)))
                {
                    throw DomainException.Conflict("Kullanılan carinin türü değiştirilemez.");
                }
            }

            a.Code = AccountingContext.Text(request.Code, "code", 30);
            a.Name = AccountingContext.Text(request.Name, "name");
            a.Type = request.Type;
            a.TaxNumber = request.TaxNumber;
            a.Email = request.Email;
            a.Phone = request.Phone;
            a.Address = request.Address;
            a.PaymentTermDays = request.PaymentTermDays;
            a.IsActive = request.IsActive;
            if (!id.HasValue)
            {
                database.Add(a);
            }

            await database.SaveChangesAsync();
            context.Audit(companyId, "Save", a);
            return a;
        });

    public Task<TaxRate> Tax(int companyId, TaxRateRequest request) => context.Write(
        companyId,
        "Journal.Post",
        async () =>
        {
            if (request.Rate is < 0 or > 100 || decimal.Round(request.Rate, 4) != request.Rate)
            {
                throw DomainException.Invalid("Vergi oranı 0–100 arasında olmalıdır.");
            }

            var a = new TaxRate
            {
                CompanyId = companyId,
                Name = AccountingContext.Text(request.Name, "name"),
                Rate = request.Rate,
                IsActive = request.IsActive

            };
            database.Add(a);
            await database.SaveChangesAsync();
            context.Audit(companyId, "Create", a);
            return a;
        });

    public async Task<Account> PostingAccount(int companyId, int id)
    {
        var a = await context.Find<Account>(companyId, id);
        if (!a.IsActive || !a.IsPostingAllowed || await database.Set<Account>().AnyAsync(x => x.ParentId == id))
        {
            throw DomainException.Invalid("Hesap aktif bir kayıt alt hesabı olmalıdır.");
        }

        return a;
    }

    public Task<Product> Product(int companyId, int? id, ProductRequest request) => context.Write(
        companyId,
        "Journal.Post",
        async () =>
        {
            if (request.SalesPrice < 0 || request.SalesPrice > 100000000 || decimal.Round(request.SalesPrice, 4) != request.SalesPrice
                || request.CriticalStockQuantity < 0
                || decimal.Round(request.CriticalStockQuantity, 4) != request.CriticalStockQuantity)
            {
                throw DomainException.Invalid("Fiyat veya kritik stok geçersiz.");
            }

            if (!(await context.Find<TaxRate>(companyId, request.DefaultTaxRateId)).IsActive)
            {
                throw DomainException.Invalid("Vergi oranı pasif.");
            }

            if (request.PreferredSupplierId.HasValue)
            {
                var supplier = await context.Find<Counterparty>(companyId, request.PreferredSupplierId.Value);
                if (!supplier.IsActive || supplier.Type == PartyType.Customer)
                {
                    throw DomainException.Invalid("Aktif tedarikçi seçin.");
                }
            }

            if ((await PostingAccount(companyId, request.InventoryAccountId)).AccountClass != AccountClass.Asset
                || (await PostingAccount(companyId, request.CostAccountId)).AccountClass != AccountClass.Expense)
            {
                throw DomainException.Invalid("Stok ve maliyet hesap sınıfları geçersiz.");
            }

            var a = id.HasValue ? await context.Find<Product>(companyId, id.Value) : new Product
            {
                CompanyId = companyId
            };
            if (id.HasValue)
            {
                AccountingContext.Version(a, request.Version);
                if ((a.InventoryAccountId != request.InventoryAccountId || a.CostAccountId != request.CostAccountId || a.Unit != request.Unit)
                    && await database.Set<StockMovement>().AnyAsync(x => x.ProductId == id))
                {
                    throw DomainException.Conflict("Hareketli ürünün birimi/hesapları değiştirilemez.");
                }
            }

            a.Code = AccountingContext.Text(request.Code, "code", 30);
            a.Name = AccountingContext.Text(request.Name, "name");
            a.Unit = AccountingContext.Text(request.Unit, "unit", 20);
            a.SalesPrice = request.SalesPrice;
            a.DefaultTaxRateId = request.DefaultTaxRateId;
            a.CriticalStockQuantity = request.CriticalStockQuantity;
            a.PreferredSupplierId = request.PreferredSupplierId;
            a.InventoryAccountId = request.InventoryAccountId;
            a.CostAccountId = request.CostAccountId;
            a.IsActive = request.IsActive;
            if (!id.HasValue)
            {
                database.Add(a);
            }

            await database.SaveChangesAsync();
            context.Audit(companyId, "Save", a);
            return a;
        });

    public Task<TreasuryAccount> Treasury(int companyId, int? id, TreasuryRequest request) => context.Write(
        companyId,
        "Journal.Post",
        async () =>
        {
            if (!Enum.IsDefined(request.Type))
            {
                throw DomainException.Invalid("Geçersiz kasa/banka türü.");
            }

            var ledger = await PostingAccount(companyId, request.LedgerAccountId);
            if (ledger.RequiredDimension != "Treasury" || !ledger.Code.StartsWith(request.Type == TreasuryType.Cash ? "100." : "102."))
            {
                throw DomainException.Invalid("Kasa 100, banka 102 alt hesabına bağlı olmalıdır.");
            }

            var a = id.HasValue ? await context.Find<TreasuryAccount>(companyId, id.Value) : new TreasuryAccount
            {
                CompanyId = companyId
            };
            if (id.HasValue)
            {
                AccountingContext.Version(a, request.Version);
                if ((a.LedgerAccountId != request.LedgerAccountId || a.Type != request.Type)
                    && await database.Set<JournalLine>().AnyAsync(x => x.TreasuryAccountId == id))
                {
                    throw DomainException.Conflict("Kullanılan kasa/banka hesabının eşlemesi değiştirilemez.");
                }
            }

            a.Name = AccountingContext.Text(request.Name, "name");
            a.Type = request.Type;
            a.LedgerAccountId = request.LedgerAccountId;
            a.BankName = request.BankName;
            a.Iban = request.Iban;
            a.IsActive = request.IsActive;
            if (!id.HasValue)
            {
                database.Add(a);
            }

            await database.SaveChangesAsync();
            context.Audit(companyId, "Save", a);
            return a;
        });

}
