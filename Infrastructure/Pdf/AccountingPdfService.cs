using System.Globalization;
using System.Text.Json;
using FinansApi.Data.Accounting;
using FinansApi.Services.Accounting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FinansApi.Infrastructure.Pdf;

public sealed class AccountingPdfService
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("tr-TR");
    private static string Amount(decimal value) => value.ToString("N2", Culture);

    public static void Configure() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Invoice(Company company, Counterparty party, Invoice invoice) => InvoiceDocument(company, party, invoice).GeneratePdf();

    public IDocument InvoiceDocument(Company company, Counterparty party, Invoice invoice)
    {
        var rows = invoice.Lines.Select(x => new[] {
                x.DescriptionSnapshot,
                x.Quantity.ToString("0.####", Culture),
                x.UnitPrice.ToString("0.####", Culture),
                Amount(x.DiscountAmount),
                Amount(x.NetAmount),
                $"%{x.TaxRateSnapshot:0.####}",
                Amount(x.TaxAmount),
                Amount(x.TotalAmount)
        }).ToList();
        return Table(
            company.Name,
            invoice.Type == InvoiceType.Sales ? "Satış faturası" : "Alış faturası",
            $"Belge: {invoice.Number}\nCari: {party.Name}\nTarih: {invoice.Date:yyyy-MM-dd}   Vade: {invoice.DueDate:yyyy-MM-dd}\nDurum: {InvoiceStatusLabel(invoice.Status)}   Para birimi: TRY",
            ["Ürün", "Miktar", "Birim fiyat", "İndirim", "Net", "KDV oranı", "KDV", "Toplam"],
            rows,
            $"Net: {Amount(invoice.NetTotal)} TRY    KDV: {Amount(invoice.TaxTotal)} TRY    Genel toplam: {Amount(invoice.GrandTotal)} TRY",
            true);
    }

    public byte[] Report(Company company, FiscalPeriod period, string kind, DateOnly? from, DateOnly? to, object data)
    {
        string[] headers;
        List<string[]> rows;
        if (data is List<BalanceRow> trial)
        {
            headers = ["Hesap", "Ad", "Borç", "Alacak", "Borç bakiyesi", "Alacak bakiyesi"];
            rows = trial.Select(x => new[] {
                    x.Code,
                    x.Name,
                    Amount(x.Debit),
                    Amount(x.Credit),
                    Amount(x.DebitBalance),
                    Amount(x.CreditBalance)
            }).ToList();
            rows.Add([
                    "TOPLAM",
                    "",
                    Amount(trial.Sum(x => x.Debit)),
                    Amount(trial.Sum(x => x.Credit)),
                    Amount(trial.Sum(x => x.DebitBalance)),
                    Amount(trial.Sum(x => x.CreditBalance))
            ]);
        }
        else if (data is List<ReconciliationCheck> checks)
        {
            headers = ["Kontrol", "Beklenen", "Gerçekleşen", "Sonuç"];
            rows = checks.Select(x => new[] { x.Name, Amount(x.Expected), Amount(x.Actual), x.Passed ? "Uyumlu" : "Fark var" }).ToList();
        }
        else
        {
            headers = ["Kalem", "Değer"];
            rows = JsonSerializer.SerializeToElement(data).EnumerateObject().Select(x => new[] {
                    ReportLabel(x.Name),
                    x.Value.ValueKind == JsonValueKind.Number ? Amount(x.Value.GetDecimal()) : x.Value.ToString()
            }).ToList();
        }

        var title = kind switch
        {
            "trial-balance" => "Mizan",
            "income-statement" => "Gelir tablosu",
            "balance-sheet" => "Bilanço",
            "vat-summary" => "KDV özeti",
            _ => "Mutabakat raporu"
        };
        return Table(
            company.Name,
            title,
            $"Dönem: {period.Name}\nKapsam: {(from ?? period.StartDate):yyyy-MM-dd} - {(to ?? period.EndDate):yyyy-MM-dd}   Para birimi: TRY",
            headers,
            rows,
            null,
            headers.Length > 4).GeneratePdf();
    }

    private static string InvoiceStatusLabel(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Draft => "Taslak",
        InvoiceStatus.Approved => "Onaylı",
        InvoiceStatus.PartiallyPaid => "Kısmi ödendi",
        InvoiceStatus.Paid => "Ödendi",
        InvoiceStatus.Cancelled => "İptal",
        _ => status.ToString()
    };

    private static string ReportLabel(string property) => property switch
    {
        "Income" => "Gelir",
        "Expense" => "Gider",
        "Result" => "Dönem sonucu",
        "Assets" => "Varlıklar",
        "Liabilities" => "Yükümlülükler",
        "Equity" => "Özkaynak",
        "CurrentUnclosedResult" => "Kapanmamış dönem sonucu",
        "Difference" => "Fark",
        "InputVat" => "İndirilecek KDV",
        "OutputVat" => "Hesaplanan KDV",
        "NetVat" => "Net KDV",
        "InputDifference" => "İndirilecek KDV mutabakat farkı",
        "OutputDifference" => "Hesaplanan KDV mutabakat farkı",
        "CurrencyCode" => "Para birimi",
        _ => property
    };

    private static IDocument Table(
        string company,
        string title,
        string metadata,
        string[] headers,
        List<string[]> rows,
        string? totals,
        bool landscape)
    {
        return Document.Create(document => document.Page(page =>
                {
                    page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
                    page.Margin(32);
                    page.DefaultTextStyle(x => x.FontFamily("Lato").FontSize(9));
                    page.Header().Column(c =>
                        {
                            c.Item().Text(company).FontSize(17).Bold();
                            c.Item().Text(title).FontSize(14).FontColor(Colors.Blue.Darken2);
                            c.Item().PaddingTop(6).Text(metadata);
                            c.Item().PaddingVertical(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                        });
                    page.Content().Column(c =>
                        {
                            c.Item().Table(t =>
                                {
                                    t.ColumnsDefinition(columns =>
                                        {
                                            for (var n = 0; n < headers.Length; n++)
                                                columns.RelativeColumn(n == 0 ? 2 : 1);
                                        });
                                    t.Header(h =>
                                        {
                                            foreach (var header in headers)
                                            {
                                                h.Cell().Background(Colors.Blue.Lighten4).Padding(5).Text(header).Bold();
                                            }
                                        });
                                    foreach (var row in rows)
                                    {
                                        foreach (var cell in row)
                                        {
                                            t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(cell);
                                        }
                                    }
                                });
                            if (rows.Count == 0)
                            {
                                c.Item().Padding(8).Text("Bu kapsamda kayıt bulunmuyor.");
                            }

                            if (totals is not null)
                            {
                                c.Item().PaddingTop(12).AlignRight().Text(totals).Bold();
                            }
                        });
                    page.Footer().PaddingTop(12).Column(c =>
                        {
                            c.Item().Text("Eğitim çıktısıdır; yasal e-Fatura/e-Defter belgesi değildir.").FontSize(8).FontColor(Colors.Grey.Darken1);
                            c.Item().Row(r =>
                                {
                                    r.RelativeItem().Text($"Oluşturma (UTC): {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}").FontSize(8);
                                    r.AutoItem().Text(x =>
                                        {
                                            x.CurrentPageNumber();
                                            x.Span(" / ");
                                            x.TotalPages();
                                        });
                                });
                        });
                }));
    }

}
