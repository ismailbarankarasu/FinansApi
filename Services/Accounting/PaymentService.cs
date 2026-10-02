using FinansApi.Data;
using FinansApi.Data.Accounting;
using FinansApi.Dtos;
using FinansApi.Infrastructure.Errors;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Services.Accounting;

public sealed class PaymentService(AppDbContext database, AccountingContext context, JournalService journal, InvoiceService invoices)
{
    public Task<Payment> Create(int companyId, PaymentRequest request) => context.Write(
        companyId,
        "Payment.Create",
        async () =>
        {
            await context.Open(companyId, request.FiscalPeriodId, request.Date);
            AccountingContext.Positive(request.Amount);
            if (!Enum.IsDefined(request.Direction) || request.Allocations is null || request.Allocations.Count == 0
                || request.Allocations.Count > 200
                || request.Allocations.Sum(x => x.Amount) != request.Amount
                || request.Allocations.Select(x => x.InvoiceId).Distinct().Count() != request.Allocations.Count)
            {
                throw DomainException.Invalid("Ödeme dağılımı toplamı tutara eşit olmalıdır; fatura tekrarlanamaz.");
            }

            var party = await context.Find<Counterparty>(companyId, request.CounterpartyId);
            if (!party.IsActive)
            {
                throw DomainException.Invalid("Cari pasif.");
            }

            var treasury = await context.Find<TreasuryAccount>(companyId, request.TreasuryAccountId);
            if (!treasury.IsActive)
            {
                throw DomainException.Invalid("Kasa/banka pasif.");
            }

            var profile = await database.Set<PostingProfile>().FindAsync(companyId) ?? throw DomainException.Invalid("Hesap eşlemeleri eksik.");
            var payment = new Payment
            {
                CompanyId = companyId,
                FiscalPeriodId = request.FiscalPeriodId,
                CounterpartyId = request.CounterpartyId,
                TreasuryAccountId = request.TreasuryAccountId,
                Date = request.Date,
                Direction = request.Direction,
                Amount = request.Amount

            };
            foreach (var a in request.Allocations)
            {
                AccountingContext.Positive(a.Amount);
                var invoice = await invoices.Load(companyId, a.InvoiceId);
                if (invoice.CounterpartyId != request.CounterpartyId || invoice.Date > request.Date
                    || invoice.Type != (request.Direction == PaymentDirection.Collection ? InvoiceType.Sales : InvoiceType.Purchase)
                    || invoice.Status is not (InvoiceStatus.Approved or InvoiceStatus.PartiallyPaid))
                {
                    throw DomainException.Invalid("Fatura ödeme için uygun değil.");
                }

                var paid = await invoices.Paid(invoice.Id);
                if (paid + a.Amount > invoice.GrandTotal)
                {
                    throw DomainException.Conflict("Ödeme kalan fatura tutarını aşıyor.");
                }

                invoice.Version++;
                invoice.Status = paid + a.Amount == invoice.GrandTotal ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
                payment.Allocations.Add(new()
                {
                    InvoiceId = invoice.Id,
                    Amount = a.Amount
                });
            }

            var collection = request.Direction == PaymentDirection.Collection;
            var journalEntry = await journal.CreatePosted(
                companyId,
                request.FiscalPeriodId,
                request.Date,
                "Payment",
                Guid.NewGuid().ToString("N"),
                [
                    new() {
                        AccountId = treasury.LedgerAccountId,
                        TreasuryAccountId = treasury.Id,
                        Debit = collection ? request.Amount : 0,
                        Credit = collection ? 0 : request.Amount
                    },
                    new() {
                        AccountId = collection ? profile.ReceivableAccountId : profile.PayableAccountId,
                        CounterpartyId = party.Id,
                        Debit = collection ? 0 : request.Amount,
                        Credit = collection ? request.Amount : 0
                    }
            ]);
            payment.JournalEntryId = journalEntry.Id;
            treasury.Version++;
            database.Add(payment);
            await database.SaveChangesAsync();
            context.Audit(companyId, "Create", payment);
            return payment;
        },
        request.IdempotencyKey,
        new
        {
            Action = "Payment.Create",
            request
        });

    public Task<Payment> Reverse(int companyId, int id, OperationRequest request) => context.Write(
        companyId,
        "Payment.Create",
        async () =>
        {
            var payment = await database.Set<Payment>().Include(x => x.Allocations).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id) ?? throw DomainException.Missing();
            if (payment.IsReversed)
            {
                throw DomainException.Conflict("Ödeme zaten terslendi.");
            }

            AccountingContext.Version(payment, request.Version);
            var periodId = request.FiscalPeriodId ?? payment.FiscalPeriodId;
            var date = request.Date ?? payment.Date;
            if (date < payment.Date)
            {
                throw DomainException.Invalid("Ters kayıt tarihi ödeme tarihinden önce olamaz.");
            }

            await journal.ReverseInternal(await journal.Load(companyId, payment.JournalEntryId), periodId, date, request.Reason);
            foreach (var a in payment.Allocations)
            {
                var invoice = await invoices.Load(companyId, a.InvoiceId);
                var paid = await invoices.Paid(invoice.Id) - a.Amount;
                invoice.Version++;
                invoice.Status = paid == 0 ? InvoiceStatus.Approved : InvoiceStatus.PartiallyPaid;
            }

            payment.IsReversed = true;
            context.Audit(companyId, "Reverse", payment, request.Reason);
            return payment;
        },
        request.IdempotencyKey,
        new
        {
            Action = "Payment.Reverse",
            id,
            request
        });

    public Task<Transfer> Transfer(int companyId, TransferRequest request) => context.Write(
        companyId,
        "Payment.Create",
        async () =>
        {
            AccountingContext.Positive(request.Amount);
            await context.Open(companyId, request.FiscalPeriodId, request.Date);
            if (request.SourceAccountId == request.TargetAccountId)
            {
                throw DomainException.Invalid("Kaynak ve hedef farklı olmalıdır.");
            }

            var source = await context.Find<TreasuryAccount>(companyId, request.SourceAccountId);
            var target = await context.Find<TreasuryAccount>(companyId, request.TargetAccountId);
            var journalEntry = await journal.CreatePosted(
                companyId,
                request.FiscalPeriodId,
                request.Date,
                "Transfer",
                Guid.NewGuid().ToString("N"),
                [
                    new() { AccountId = source.LedgerAccountId, TreasuryAccountId = source.Id, Credit = request.Amount },
                    new() { AccountId = target.LedgerAccountId, TreasuryAccountId = target.Id, Debit = request.Amount }
            ]);
            source.Version++;
            target.Version++;
            var transfer = new Transfer
            {
                CompanyId = companyId,
                FiscalPeriodId = request.FiscalPeriodId,
                Date = request.Date,
                SourceAccountId = source.Id,
                TargetAccountId = target.Id,
                Amount = request.Amount,
                JournalEntryId = journalEntry.Id

            };
            database.Add(transfer);
            await database.SaveChangesAsync();
            context.Audit(companyId, "Create", transfer);
            return transfer;
        },
        request.IdempotencyKey,
        new
        {
            Action = "Transfer.Create",
            request
        });

}
