using FinansApi.Data;
using FinansApi.Data.Accounting;
using FinansApi.Dtos;
using FinansApi.Infrastructure.Errors;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Services.Accounting;

public sealed class JournalService(AppDbContext database, AccountingContext context, MasterDataService masters)
{
    public async Task Validate(int companyId, List<JournalLine> lines)
    {
        if (lines.Count < 2 || lines.Count > 500)
        {
            throw DomainException.Invalid("Fiş 2–500 satır içermelidir.", "lines");
        }

        foreach (var line in lines)
        {
            if (line.Debit < 0 || line.Credit < 0 || (line.Debit > 0) == (line.Credit > 0))
            {
                throw DomainException.Invalid("Her satırda yalnız borç veya yalnız alacak pozitif olmalıdır.", "lines");
            }

            AccountingContext.Positive(line.Debit + line.Credit);
            var a = await masters.PostingAccount(companyId, line.AccountId);
            if (a.RequiredDimension == "Counterparty" && line.CounterpartyId is null)
            {
                throw DomainException.Invalid("Cari boyutu zorunludur.");
            }

            if (a.RequiredDimension == "Treasury" && line.TreasuryAccountId is null)
            {
                throw DomainException.Invalid("Kasa/banka boyutu zorunludur.");
            }

            if (line.CounterpartyId.HasValue)
            {
                var party = await context.Find<Counterparty>(companyId, line.CounterpartyId.Value);
                if (!party.IsActive || a.RequiredDimension != "Counterparty")
                {
                    throw DomainException.Invalid("Cari boyutu bu hesap için uygun değil.");
                }

                if ((a.Code.StartsWith("120.") && party.Type == PartyType.Supplier)
                    || (a.Code.StartsWith("320.") && party.Type == PartyType.Customer))
                {
                    throw DomainException.Invalid("Cari türü hesaba uygun değil.");
                }
            }

            if (line.TreasuryAccountId.HasValue)
            {
                var treasury = await context.Find<TreasuryAccount>(companyId, line.TreasuryAccountId.Value);
                if (!treasury.IsActive || treasury.LedgerAccountId != line.AccountId)
                {
                    throw DomainException.Invalid("Kasa/banka hesap eşlemesi geçersiz.");
                }
            }
        }

        if (lines.Sum(x => x.Debit) != lines.Sum(x => x.Credit))
        {
            throw DomainException.Invalid("Toplam borç ve alacak eşit olmalıdır.", "lines");
        }
    }

    public async Task CheckTreasury(int companyId, int periodId, DateOnly date, List<JournalLine> proposed)
    {
        foreach (var treasuryGroup in proposed.Where(x => x.TreasuryAccountId.HasValue).GroupBy(x => x.TreasuryAccountId!.Value))
        {
            var rows = await (
                from journalEntry in database.Set<JournalEntry>()
                from line in journalEntry.Lines
                where journalEntry.CompanyId == companyId && journalEntry.FiscalPeriodId == periodId
                && journalEntry.Status == JournalStatus.Posted
                && line.TreasuryAccountId == treasuryGroup.Key
                select new
                {
                    journalEntry.Date,
                    Amount = line.Debit - line.Credit
                }

            ).ToListAsync();
            var daily = rows.Select(x => (x.Date, x.Amount)).Append((Date: date, Amount: treasuryGroup.Sum(x => x.Debit - x.Credit))).GroupBy(x => x.Date).OrderBy(x => x.Key);
            decimal balance = 0;
            foreach (var day in daily)
            {
                balance += day.Sum(x => x.Amount);
                if (balance < 0)
                {
                    throw DomainException.Conflict("Kasa/banka bakiyesi yetersiz.");
                }
            }
        }
    }

    public Task<JournalEntry> Save(int companyId, int? id, JournalRequest request) => context.Write(
        companyId,
        "Journal.Post",
        async () =>
        {
            await context.Open(companyId, request.FiscalPeriodId, request.Date);
            var entry = id.HasValue ? await Load(companyId, id.Value) : new JournalEntry
            {
                CompanyId = companyId,
                Number = AccountingContext.Number("YEV"),
                Status = JournalStatus.Draft

            };
            if (id.HasValue)
            {
                await context.Open(companyId, entry.FiscalPeriodId, entry.Date);
                if (entry.Status != JournalStatus.Draft)
                {
                    throw DomainException.Conflict("Onaylı fiş değiştirilemez.");
                }

                AccountingContext.Version(entry, request.Version);
                database.RemoveRange(entry.Lines);
            }

            entry.FiscalPeriodId = request.FiscalPeriodId;
            entry.Date = request.Date;
            entry.Description = request.Description;
            entry.Lines = (request.Lines ?? []).Select(x => new JournalLine
            {
                AccountId = x.AccountId,
                Debit = x.Debit,
                Credit = x.Credit,
                CounterpartyId = x.CounterpartyId,
                TreasuryAccountId = x.TreasuryAccountId,
                Description = x.Description
            }).ToList();
            await Validate(companyId, entry.Lines);
            if (!id.HasValue)
            {
                database.Add(entry);
            }

            await database.SaveChangesAsync();
            context.Audit(companyId, "SaveDraft", entry);
            return entry;
        });

    public async Task<JournalEntry> Load(int companyId, int id) => await database.Set<JournalEntry>().Include(x => x.Lines).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id) ?? throw DomainException.Missing();

    public Task<JournalEntry> Post(int companyId, int id, OperationRequest request) => context.Write(
        companyId,
        "Journal.Post",
        async () =>
        {
            var journalEntry = await Load(companyId, id);
            await context.Open(companyId, journalEntry.FiscalPeriodId, journalEntry.Date);
            if (journalEntry.Status == JournalStatus.Posted)
            {
                return journalEntry;
            }

            AccountingContext.Version(journalEntry, request.Version);
            await Validate(companyId, journalEntry.Lines);
            await CheckTreasury(companyId, journalEntry.FiscalPeriodId, journalEntry.Date, journalEntry.Lines);
            journalEntry.Status = JournalStatus.Posted;
            context.Audit(companyId, "Post", journalEntry);
            return journalEntry;
        },
        request.IdempotencyKey,
        new
        {
            Action = "Journal.Post",
            id,
            request
        });

    public Task<JournalEntry> Reverse(int companyId, int id, OperationRequest request) => context.Write(
        companyId,
        "Journal.Post",
        async () =>
        {
            var journalEntry = await Load(companyId, id);
            if (journalEntry.SourceType != "Manual")
            {
                throw DomainException.Conflict("Belge kaynaklı fişi ilgili belgeden tersleyin.");
            }

            AccountingContext.Version(journalEntry, request.Version);
            return await ReverseInternal(journalEntry, request.FiscalPeriodId ?? journalEntry.FiscalPeriodId, request.Date ?? journalEntry.Date, request.Reason);
        },
        request.IdempotencyKey,
        new
        {
            Action = "Journal.Reverse",
            id,
            request
        });

    public async Task<JournalEntry> ReverseInternal(JournalEntry original, int periodId, DateOnly date, string? reason)
    {
        if (original.Status != JournalStatus.Posted || original.ReversalOfId.HasValue)
        {
            throw DomainException.Conflict("Fiş ters kayda uygun değil.");
        }

        if (date < original.Date)
        {
            throw DomainException.Invalid("Ters kayıt tarihi kaynak fişten önce olamaz.");
        }
        AccountingContext.Text(reason, "reason", 500);
        if (await database.Set<JournalEntry>().AnyAsync(x => x.ReversalOfId == original.Id))
        {
            throw DomainException.Conflict("Fiş zaten terslendi.");
        }

        var lines = original.Lines.Select(x => new JournalLine
        {
            AccountId = x.AccountId,
            Debit = x.Credit,
            Credit = x.Debit,
            CounterpartyId = x.CounterpartyId,
            TreasuryAccountId = x.TreasuryAccountId,
            Description = reason
        }).ToList();
        var reversal = await CreatePosted(original.CompanyId, periodId, date, "Reversal", original.Id.ToString(), lines, reason);
        reversal.ReversalOfId = original.Id;
        return reversal;
    }

    public async Task<JournalEntry> CreatePosted(
        int companyId,
        int periodId,
        DateOnly date,
        string source,
        string sourceId,
        List<JournalLine> lines,
        string? description = null)
    {
        await context.Open(companyId, periodId, date);
        await Validate(companyId, lines);
        await CheckTreasury(companyId, periodId, date, lines);
        var entry = new JournalEntry
        {
            CompanyId = companyId,
            FiscalPeriodId = periodId,
            Date = date,
            SourceType = source,
            SourceId = sourceId,
            Description = description,
            Number = AccountingContext.Number("YEV"),
            Status = JournalStatus.Posted,
            Lines = lines

        };
        database.Add(entry);
        await database.SaveChangesAsync();
        context.Audit(companyId, "Post", entry);
        return entry;
    }

    public Task<bool> Delete(int companyId, int id) => context.Write(
        companyId,
        "Journal.Post",
        async () =>
        {
            var journalEntry = await Load(companyId, id);
            await context.Open(companyId, journalEntry.FiscalPeriodId, journalEntry.Date);
            if (journalEntry.Status != JournalStatus.Draft)
            {
                throw DomainException.Conflict("Onaylı fiş silinemez.");
            }

            context.Audit(companyId, "Delete", journalEntry);
            database.Remove(journalEntry);
            return true;
        });

}
