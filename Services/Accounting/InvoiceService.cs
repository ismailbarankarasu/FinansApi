using FinansApi.Data;
using FinansApi.Data.Accounting;
using FinansApi.Dtos;
using FinansApi.Infrastructure.Errors;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Services.Accounting;

public sealed class InvoiceService(AppDbContext database, AccountingContext context, JournalService journal)
{
    public async Task<Invoice> Calculate(int companyId, InvoiceRequest request)
    {
        if (!Enum.IsDefined(request.Type) || request.Lines is null || request.Lines.Count is < 1 or > 200)
        {
            throw DomainException.Invalid("Fatura türü veya satırları geçersiz.");
        }

        await context.Open(companyId, request.FiscalPeriodId, request.Date);
        if (request.DueDate < request.Date)
        {
            throw DomainException.Invalid("Vade fatura tarihinden önce olamaz.", "dueDate");
        }

        var party = await context.Find<Counterparty>(companyId, request.CounterpartyId);
        if (!party.IsActive || (request.Type == InvoiceType.Sales && party.Type == PartyType.Supplier)
            || (request.Type == InvoiceType.Purchase && party.Type == PartyType.Customer))
        {
            throw DomainException.Invalid("Cari türü fatura için uygun değil.");
        }

        if (request.Lines.GroupBy(x => x.ProductId).Any(x => x.Count() > 1))
        {
            throw DomainException.Invalid("Ürünleri tek satırda birleştirin.");
        }

        var invoice = new Invoice
        {
            CompanyId = companyId,
            FiscalPeriodId = request.FiscalPeriodId,
            CounterpartyId = request.CounterpartyId,
            Type = request.Type,
            Date = request.Date,
            DueDate = request.DueDate

        };
        foreach (var line in request.Lines)
        {
            if (line.Quantity <= 0 || line.Quantity > 1000000 || decimal.Round(line.Quantity, 4) != line.Quantity || line.UnitPrice < 0
                || line.UnitPrice > 100000000
                || decimal.Round(line.UnitPrice, 4) != line.UnitPrice
                || line.DiscountAmount < 0
                || AccountingContext.Money(line.DiscountAmount) != line.DiscountAmount)
            {
                throw DomainException.Invalid("Miktar, fiyat veya indirim geçersiz.", "lines");
            }

            var product = await context.Find<Product>(companyId, line.ProductId);
            if (!product.IsActive)
            {
                throw DomainException.Invalid("Ürün pasif.");
            }

            var tax = await context.Find<TaxRate>(companyId, product.DefaultTaxRateId);
            if (!tax.IsActive)
            {
                throw DomainException.Invalid("Vergi oranı pasif.");
            }

            var net = AccountingContext.Money(line.Quantity * line.UnitPrice - line.DiscountAmount);
            if (net < 0 || net > 100000000)
            {
                throw DomainException.Invalid("Satır neti geçersiz.");
            }

            var taxAmount = AccountingContext.Money(net * tax.Rate / 100m);
            invoice.Lines.Add(new InvoiceLine
            {
                ProductId = product.Id,
                DescriptionSnapshot = product.Name,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                DiscountAmount = line.DiscountAmount,
                TaxRateSnapshot = tax.Rate,
                NetAmount = net,
                TaxAmount = taxAmount,
                TotalAmount = net + taxAmount
            });
        }

        invoice.NetTotal = invoice.Lines.Sum(x => x.NetAmount);
        invoice.TaxTotal = invoice.Lines.Sum(x => x.TaxAmount);
        invoice.GrandTotal = invoice.NetTotal + invoice.TaxTotal;
        AccountingContext.Positive(invoice.GrandTotal);
        return invoice;
    }

    public async Task<Invoice> Load(int companyId, int id) => await database.Set<Invoice>().Include(x => x.Lines).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id) ?? throw DomainException.Missing();

    public Task<Invoice> Save(int companyId, int? id, InvoiceRequest request) => context.Write(
        companyId,
        request.Type == InvoiceType.Sales ? "Invoice.DraftSales" : "Journal.Post",
        async () =>
        {
            var calculated = await Calculate(companyId, request);
            if (!id.HasValue)
            {
                calculated.Number = AccountingContext.Number(request.Type == InvoiceType.Sales ? "SAT" : "ALI");
                database.Add(calculated);
            }
            else
            {
                var old = await Load(companyId, id.Value);
                if (old.Type == InvoiceType.Purchase)
                {
                    await context.Access(companyId, "Journal.Post");
                }

                await context.Open(companyId, old.FiscalPeriodId, old.Date);
                if (old.Status != InvoiceStatus.Draft)
                {
                    throw DomainException.Conflict("Yalnız taslak fatura değiştirilebilir.");
                }

                AccountingContext.Version(old, request.Version);
                database.RemoveRange(old.Lines);
                old.FiscalPeriodId = request.FiscalPeriodId;
                old.CounterpartyId = request.CounterpartyId;
                old.Type = request.Type;
                old.Date = request.Date;
                old.DueDate = request.DueDate;
                old.Lines = calculated.Lines;
                old.NetTotal = calculated.NetTotal;
                old.TaxTotal = calculated.TaxTotal;
                old.GrandTotal = calculated.GrandTotal;
                calculated = old;
            }

            await database.SaveChangesAsync();
            context.Audit(companyId, "SaveDraft", calculated);
            return calculated;
        });

    public Task<bool> Delete(int companyId, int id) => context.Write(
        companyId,
        "Invoice.DraftSales",
        async () =>
        {
            var e = await Load(companyId, id);
            if (e.Type == InvoiceType.Purchase)
            {
                await context.Access(companyId, "Journal.Post");
            }

            await context.Open(companyId, e.FiscalPeriodId, e.Date);
            if (e.Status != InvoiceStatus.Draft)
            {
                throw DomainException.Conflict("Yalnız taslak fatura silinebilir.");
            }

            context.Audit(companyId, "Delete", e);
            database.Remove(e);
            return true;
        });

    public async Task<(decimal Quantity, decimal Value)> Stock(int companyId, int periodId, int productId)
    {
        var movements = await database.Set<StockMovement>().Where(x => x.CompanyId == companyId && x.FiscalPeriodId == periodId && x.ProductId == productId).ToListAsync();
        return (movements.Sum(x => x.Quantity), movements.Sum(x => x.TotalCost));
    }

    public Task<Invoice> Approve(int companyId, int id, OperationRequest request) => context.Write(
        companyId,
        "Invoice.Approve",
        async () =>
        {
            var invoice = await Load(companyId, id);
            await context.Open(companyId, invoice.FiscalPeriodId, invoice.Date);
            if (invoice.Status is InvoiceStatus.Approved or InvoiceStatus.PartiallyPaid or InvoiceStatus.Paid)
            {
                return invoice;
            }

            if (invoice.Status != InvoiceStatus.Draft)
            {
                throw DomainException.Conflict("Fatura onaylanamaz.");
            }

            AccountingContext.Version(invoice, request.Version);
            var party = await context.Find<Counterparty>(companyId, invoice.CounterpartyId);
            if (!party.IsActive)
            {
                throw DomainException.Invalid("Cari pasif.");
            }

            var profile = await database.Set<PostingProfile>().FindAsync(companyId) ?? throw DomainException.Invalid("Muhasebe hesap eşlemelerini oluşturun.");
            var lines = new List<JournalLine>();
            var costs = new List<JournalLine>();
            foreach (var line in invoice.Lines)
            {
                var product = await context.Find<Product>(companyId, line.ProductId);
                if (!product.IsActive)
                {
                    throw DomainException.Invalid("Ürün pasif.");
                }

                if (await database.Set<StockMovement>().AnyAsync(x => x.CompanyId == companyId && x.ProductId == product.Id && x.Date > invoice.Date))
                {
                    throw DomainException.Conflict("Son stok hareketinden eski tarih kullanılamaz.");
                }

                var balance = await Stock(companyId, invoice.FiscalPeriodId, product.Id);
                decimal value;
                if (invoice.Type == InvoiceType.Sales)
                {
                    if (balance.Quantity < line.Quantity)
                    {
                        throw DomainException.Conflict($"{product.Name}: stok yetersiz.");
                    }

                    value = line.Quantity == balance.Quantity ? balance.Value : AccountingContext.Money(balance.Value / balance.Quantity * line.Quantity);
                    line.CostSnapshot = value;
                    if (value > 0)
                    {
                        costs.Add(new()
                        {
                            AccountId = product.CostAccountId,
                            Debit = value
                        });
                        costs.Add(new()
                        {
                            AccountId = product.InventoryAccountId,
                            Credit = value
                        });
                    }
                }
                else
                {
                    value = line.NetAmount;
                    if (value > 0)
                    {
                        lines.Add(new()
                        {
                            AccountId = product.InventoryAccountId,
                            Debit = value
                        });
                    }
                }

                var sign = invoice.Type == InvoiceType.Sales ? -1 : 1;
                database.Add(new StockMovement
                {
                    CompanyId = companyId,
                    FiscalPeriodId = invoice.FiscalPeriodId,
                    ProductId = product.Id,
                    Date = invoice.Date,
                    Quantity = sign * line.Quantity,
                    UnitCost = decimal.Round(value / line.Quantity, 4, MidpointRounding.AwayFromZero),
                    TotalCost = sign * value,
                    SourceType = "Invoice",
                    SourceId = id.ToString()
                });
                product.Version++;
                var quantity = balance.Quantity + sign * line.Quantity;
                if (quantity >= product.CriticalStockQuantity)
                {
                    product.LowStockAlertActive = false;
                }
                else if (invoice.Type == InvoiceType.Sales && !product.LowStockAlertActive)
                {
                    product.LowStockAlertActive = true;
                    var supplier = product.PreferredSupplierId.HasValue ? await context.Find<Counterparty>(companyId, product.PreferredSupplierId.Value) : null;
                    var company = await database.Set<Company>().SingleAsync(company => company.Id == companyId);
                    database.Add(new OutboxMessage
                    {
                        CompanyId = companyId,
                        EventType = "CriticalStock",
                        DeduplicationKey = $"stock:{companyId}:{product.Id}:{id}",
                        NextAttemptAtUtc = DateTimeOffset.UtcNow,
                        Payload = System.Text.Json.JsonSerializer.Serialize(new { Email = supplier?.Email, Subject = "Kritik stok uyarısı", Body = $"{product.Code} {product.Name}: miktar {quantity}, eşik {product.CriticalStockQuantity}, önerilen sipariş {Math.Max(product.CriticalStockQuantity * 2 - quantity, 1)}. Şirket: {company.Name}; iletişim: {company.ContactEmail ?? "belirtilmedi"}." })
                    });
                }
            }

            if (invoice.Type == InvoiceType.Sales)
            {
                lines.Add(new()
                {
                    AccountId = profile.ReceivableAccountId,
                    CounterpartyId = invoice.CounterpartyId,
                    Debit = invoice.GrandTotal
                });
                if (invoice.NetTotal > 0)
                {
                    lines.Add(new()
                    {
                        AccountId = profile.RevenueAccountId,
                        Credit = invoice.NetTotal
                    });
                }

                if (invoice.TaxTotal > 0)
                {
                    lines.Add(new()
                    {
                        AccountId = profile.OutputVatAccountId,
                        Credit = invoice.TaxTotal
                    });
                }
            }
            else
            {
                lines.Add(new()
                {
                    AccountId = profile.PayableAccountId,
                    CounterpartyId = invoice.CounterpartyId,
                    Credit = invoice.GrandTotal
                });
                if (invoice.TaxTotal > 0)
                {
                    lines.Add(new()
                    {
                        AccountId = profile.InputVatAccountId,
                        Debit = invoice.TaxTotal
                    });
                }
            }

            var e = await journal.CreatePosted(companyId, invoice.FiscalPeriodId, invoice.Date, "Invoice", id.ToString(), lines);
            if (costs.Count > 0)
            {
                await journal.CreatePosted(companyId, invoice.FiscalPeriodId, invoice.Date, "InvoiceCost", id.ToString(), costs);
            }

            invoice.JournalEntryId = e.Id;
            invoice.Status = InvoiceStatus.Approved;
            context.Audit(companyId, "Approve", invoice);
            return invoice;
        },
        request.IdempotencyKey,
        new
        {
            Action = "Invoice.Approve",
            id,
            request
        });

    public async Task<decimal> Paid(int invoiceId) => (await (
            from product in database.Set<Payment>()
            from a in product.Allocations
            where !product.IsReversed && a.InvoiceId == invoiceId
            select a.Amount).ToListAsync()).Sum();

    public Task<Invoice> Cancel(int companyId, int id, OperationRequest request) => context.Write(
        companyId,
        "Invoice.Approve",
        async () =>
        {
            var invoice = await Load(companyId, id);
            AccountingContext.Version(invoice, request.Version);
            if (invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Cancelled)
            {
                throw DomainException.Conflict("Fatura iptale uygun değil.");
            }

            if (await Paid(id) > 0)
            {
                throw DomainException.Conflict("Önce bağlı ödemeleri tersleyin.");
            }

            var periodId = request.FiscalPeriodId ?? invoice.FiscalPeriodId;
            var date = request.Date ?? invoice.Date;
            await context.Open(companyId, periodId, date);
            var movements = await database.Set<StockMovement>().Where(x => x.CompanyId == companyId && x.SourceType == "Invoice" && x.SourceId == id.ToString()).ToListAsync();
            foreach (var m in movements)
            {
                if (periodId != m.FiscalPeriodId || date < m.Date
                    || await database.Set<StockMovement>().AnyAsync(x => x.ProductId == m.ProductId && x.Id > m.Id))
                {
                    throw DomainException.Conflict("Sonraki stok hareketleri bulunan veya devredilmiş fatura güvenle terslenemez.");
                }

                database.Add(new StockMovement
                {
                    CompanyId = companyId,
                    FiscalPeriodId = periodId,
                    ProductId = m.ProductId,
                    Date = date,
                    Quantity = -m.Quantity,
                    UnitCost = m.UnitCost,
                    TotalCost = -m.TotalCost,
                    SourceType = "InvoiceCancel",
                    SourceId = id.ToString(),
                    ReversalOfId = m.Id
                });
                var product = await context.Find<Product>(companyId, m.ProductId);
                product.Version++;
                var stock = await Stock(companyId, periodId, product.Id);
                if (stock.Quantity - m.Quantity >= product.CriticalStockQuantity)
                {
                    product.LowStockAlertActive = false;
                }
            }

            var entries = await database.Set<JournalEntry>().Include(x => x.Lines).Where(x => x.CompanyId == companyId && (x.SourceType == "Invoice" || x.SourceType == "InvoiceCost") && x.SourceId == id.ToString()).ToListAsync();
            foreach (var e in entries)
            {
                await journal.ReverseInternal(e, periodId, date, request.Reason);
            }

            invoice.Status = InvoiceStatus.Cancelled;
            context.Audit(companyId, "Cancel", invoice, request.Reason);
            return invoice;
        },
        request.IdempotencyKey,
        new
        {
            Action = "Invoice.Cancel",
            id,
            request
        });

}
