using FinansApi.Data;
using FinansApi.Data.Accounting;
using FinansApi.Dtos;
using FinansApi.Infrastructure.Errors;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Services.Accounting;

public record ClosingPreview(bool CanClose, List<string> Blockers, List<ReconciliationCheck> Checks);

public sealed class ClosingService(AppDbContext database, AccountingContext context, JournalService journal, ReportService reports)
{
    public async Task<ClosingPreview> Preview(int companyId, int periodId, int? targetId)
    {
        var p = await context.Find<FiscalPeriod>(companyId, periodId);
        var blockers = new List<string>();
        if (p.Status != PeriodStatus.Open)
        {
            blockers.Add("Kaynak dönem kilitli.");
        }

        if (await database.Set<Invoice>().AnyAsync(x => x.CompanyId == companyId && x.FiscalPeriodId == periodId && x.Status == InvoiceStatus.Draft)
            || await database.Set<JournalEntry>().AnyAsync(x => x.CompanyId == companyId && x.FiscalPeriodId == periodId && x.Status == JournalStatus.Draft))
        {
            blockers.Add("Taslak belgeleri tamamlayın veya silin.");
        }

        if (!await database.Set<PostingProfile>().AnyAsync(x => x.CompanyId == companyId))
        {
            blockers.Add("Hesap eşlemeleri eksik.");
        }

        if (targetId.HasValue)
        {
            var target = await context.Find<FiscalPeriod>(companyId, targetId.Value);
            if (p.EndDate == DateOnly.MaxValue || target.StartDate != p.EndDate.AddDays(1) || target.Status != PeriodStatus.Open)
            {
                blockers.Add("Hedef dönem açık ve ardışık olmalıdır.");
            }

            if (await database.Set<JournalEntry>().AnyAsync(x => x.FiscalPeriodId == target.Id)
                || await database.Set<StockMovement>().AnyAsync(x => x.FiscalPeriodId == target.Id)
                || await database.Set<Invoice>().AnyAsync(x => x.FiscalPeriodId == target.Id))
            {
                blockers.Add("Hedef dönemde henüz belge/hareket bulunmamalıdır.");
            }
        }

        var profile = await database.Set<PostingProfile>().FindAsync(companyId);
        if (profile is not null)
        {
            var mappedAccountIds = new[]
            {
                profile.ReceivableAccountId,
                profile.PayableAccountId,
                profile.RevenueAccountId,
                profile.InputVatAccountId,
                profile.OutputVatAccountId,
                profile.ResultAccountId,
                profile.RetainedEarningsAccountId

            };
            var validAccounts = await database.Set<Account>().CountAsync(account => account.CompanyId == companyId && mappedAccountIds.Contains(account.Id) && account.IsActive && account.IsPostingAllowed);
            if (validAccounts != mappedAccountIds.Distinct().Count())
            {
                blockers.Add("Hesap eşlemelerinde pasif veya geçersiz hesap var.");
            }
        }

        var checks = await reports.Reconcile(companyId, periodId);
        blockers.AddRange(checks.Where(x => !x.Passed).Select(x => $"Mutabakat farkı: {x.Name}"));
        return new(blockers.Count == 0, blockers, checks);
    }

    public Task<CarryForwardRun> Close(int companyId, int periodId, ClosingRequest request) => context.Write(
        companyId,
        "Period.Close",
        async () =>
        {
            var previous = await database.Set<CarryForwardRun>().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SourcePeriodId == periodId);
            if (previous is not null)
            {
                if (previous.TargetPeriodId != request.TargetPeriodId)
                {
                    throw DomainException.Conflict("Dönem başka hedefe devredilmiş.");
                }

                return previous;
            }

            var preview = await Preview(companyId, periodId, request.TargetPeriodId);
            if (!preview.CanClose)
            {
                throw DomainException.Conflict(string.Join(" ", preview.Blockers));
            }

            var source = await context.Find<FiscalPeriod>(companyId, periodId);
            var target = await context.Find<FiscalPeriod>(companyId, request.TargetPeriodId);
            AccountingContext.Version(source, request.Version);
            target.Version++;
            var profile = await database.Set<PostingProfile>().FindAsync(companyId) ?? throw DomainException.Invalid("Hesap eşlemesi eksik.");
            var rows = await reports.Rows(companyId, periodId);
            var closeLines = new List<JournalLine>();
            foreach (var g in rows.Where(x => ReportAccountMapping.IsOperatingAccount(x.AccountClass)).GroupBy(x => new { x.AccountId, x.CounterpartyId, x.TreasuryAccountId }))
            {
                var net = g.Sum(x => x.Debit - x.Credit);
                if (net == 0)
                {
                    continue;
                }

                closeLines.Add(new()
                {
                    AccountId = g.Key.AccountId,
                    CounterpartyId = g.Key.CounterpartyId,
                    TreasuryAccountId = g.Key.TreasuryAccountId,
                    Debit = Math.Max(-net, 0),
                    Credit = Math.Max(net, 0)
                });
            }

            var difference = closeLines.Sum(x => x.Debit - x.Credit);
            if (difference != 0)
            {
                closeLines.Add(new()
                {
                    AccountId = profile.ResultAccountId,
                    Debit = Math.Max(-difference, 0),
                    Credit = Math.Max(difference, 0)
                });
            }

            JournalEntry? closing = null;
            if (closeLines.Count > 0)
            {
                closing = await journal.CreatePosted(companyId, periodId, source.EndDate, "Closing", periodId.ToString(), closeLines);
            }

            if (difference != 0)
            {
                await journal.CreatePosted(
                    companyId,
                    periodId,
                    source.EndDate,
                    "ResultTransfer",
                    periodId.ToString(),
                    [
                        new() {
                            AccountId = profile.ResultAccountId,
                            Debit = Math.Max(difference, 0),
                            Credit = Math.Max(-difference, 0)
                        },
                        new() {
                            AccountId = profile.RetainedEarningsAccountId,
                            Debit = Math.Max(-difference, 0),
                            Credit = Math.Max(difference, 0)
                        }
                ]);
            }

            rows = await reports.Rows(companyId, periodId);
            var openingLines = new List<JournalLine>();
            foreach (var g in rows.Where(x => ReportAccountMapping.IsBalanceSheetAccount(x.AccountClass)).GroupBy(x => new { x.AccountId, x.CounterpartyId, x.TreasuryAccountId }))
            {
                var net = g.Sum(x => x.Debit - x.Credit);
                if (net == 0)
                {
                    continue;
                }

                openingLines.Add(new()
                {
                    AccountId = g.Key.AccountId,
                    CounterpartyId = g.Key.CounterpartyId,
                    TreasuryAccountId = g.Key.TreasuryAccountId,
                    Debit = Math.Max(net, 0),
                    Credit = Math.Max(-net, 0)
                });
            }

            JournalEntry? opening = null;
            if (openingLines.Count > 0)
            {
                opening = await journal.CreatePosted(companyId, target.Id, target.StartDate, "Opening", periodId.ToString(), openingLines);
            }

            var stock = await database.Set<StockMovement>().Where(x => x.CompanyId == companyId && x.FiscalPeriodId == periodId).ToListAsync();
            foreach (var g in stock.GroupBy(x => x.ProductId))
            {
                var quantity = g.Sum(x => x.Quantity);
                var value = g.Sum(x => x.TotalCost);
                if (quantity < 0 || (quantity == 0 && value != 0))
                {
                    throw DomainException.Conflict("Stok miktar/değer uyumsuzluğu.");
                }

                if (quantity == 0)
                {
                    continue;
                }

                database.Add(new StockMovement
                {
                    CompanyId = companyId,
                    FiscalPeriodId = target.Id,
                    ProductId = g.Key,
                    Date = target.StartDate,
                    Quantity = quantity,
                    TotalCost = value,
                    UnitCost = decimal.Round(value / quantity, 4, MidpointRounding.AwayFromZero),
                    SourceType = "Opening",
                    SourceId = periodId.ToString()
                });
            }

            await database.SaveChangesAsync();
            var targetChecks = await reports.Reconcile(companyId, target.Id);
            if (targetChecks.Any(check => !check.Passed))
            {
                throw DomainException.Conflict("Hedef dönem açılış mutabakatı başarısız; devir geri alındı.");
            }

            source.Status = PeriodStatus.Locked;
            source.LockedAtUtc = DateTimeOffset.UtcNow;
            source.LockedByUserId = context.UserId;
            var run = new CarryForwardRun
            {
                CompanyId = companyId,
                SourcePeriodId = periodId,
                TargetPeriodId = target.Id,
                ClosingJournalEntryId = closing?.Id,
                OpeningJournalEntryId = opening?.Id,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ActorUserId = context.UserId

            };
            database.Add(run);
            await database.SaveChangesAsync();
            context.Audit(companyId, "CloseAndCarryForward", run);
            return run;
        },
        request.IdempotencyKey,
        new
        {
            Action = "Period.Close",
            periodId,
            request
        });

}
