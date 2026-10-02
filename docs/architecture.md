# Mimari

## HTTP ve servis sınırı

Controller'lar route, parametre, yetki kontrolü ve yanıt üretir. İş akışları `Services/Accounting` içinde, EF Core sorguları `AppDbContext` üzerinden yürür. Aynı API projesi korunur.

| Bileşen | Sorumluluk |
|---|---|
| `AccountingContext` | Claim'den kullanıcı; güncel üyelik/izin; şirket ve açık dönem kontrolü; transaction; idempotency; audit |
| `CompanyService` | Şirket, çakışmayan dönem, üyelik, son Admin koruması |
| `MasterDataService` | Hesap planı, cari, vergi oranı, ürün, kasa/banka kartları |
| `JournalService` | Dengeli taslak, onay, manuel ters fiş ve ticari belge fişleri |
| `InvoiceService` | Satır hesapları/snapshot, taslak, onay, stok maliyeti, iptal |
| `InventoryOpeningService` | İlk stok açılışı ve muhasebe hesap eşlemeleri |
| `PaymentService` | Fatura dağılımı, ödeme tersleme ve virman |
| `ReportService` | Mizan, KDV, gelir tablosu, bilanço, cari/kasa ekstresi |
| `ReportAccountMapping` | Hesap sınıflarının mali tablo grupları |
| `ReconciliationService` | Belge ve yevmiye, stok ve kasa/banka mutabakatları |
| `ClosingService` | Ön kontrol, kapanış/sonuç fişi, açılış, stok devri ve kilit |
| `LegacyScope` | Eski endpointlerde varsayılan/açıkça seçilen şirket ve dönem çözümü |
| `AutomationService` | Günlük hatırlatma üretimi, outbox sahiplenme ve yeniden deneme |
| `AccountingPdfService` | Fatura ve rapor verilerinden ortak tablo/PDF düzeni |

## İşlem bütünlüğü

Yazma servisleri `AccountingContext.Write` ile aynı SQLite transaction'ında çalışır. İç servislerin `SaveChangesAsync` çağrıları transaction'ı commit etmez. Dış akış tamamlanırsa belge, fiş, stok, dağılım, audit ve işlem makbuzu birlikte commit edilir; hata halinde birlikte geri alınır.

SQLite yazıcı kilidi, EF concurrency token'ları ve benzersiz indeksler beraber kullanılır. Bu düzen tek SQLite dosyasını paylaşan istekler içindir; çok düğümlü bir veritabanı tasarımı iddiası yoktur.

`OperationReceipt`, şirket ve işlem anahtarını benzersiz tutar. Aynı anahtar/gövde önceki yanıtı döndürür; aynı anahtar farklı gövdede 409 üretir. Kaynak fiş türü/kimliği ve ters fiş referansı da benzersizdir.

## Muhasebe akışı

```text
Taslak fatura
  -> şirket + rol + dönem + sürüm doğrulaması
  -> ürün ve stok maliyeti
  -> fatura fişi + gerekiyorsa satış maliyet fişi
  -> stok hareketi + audit + kritik stok outbox kaydı
  -> tek commit

Tahsilat / ödeme
  -> cari + kasa/banka + fatura kalan tutarı
  -> dağılımlar + dengeli fiş + fatura durumu + audit
  -> tek commit
```

Raporlar posted yevmiye satırlarını okur. Stok miktarı stok hareketlerinden, fatura kalan tutarı terslenmemiş ödeme dağılımlarından hesaplanır. Mutabakat bu kaynakların eşitliğini kontrol eder.

## Audit ve dış etkiler

`AppDbContext.Audit` yüklenen kayıtların önceki değerlerini saklar; audit'e yazılırken iletişim ve kimlik bilgileri maskelenir. Audit değiştirme/silme engellenir. Yetkisiz işlem denemeleri uygulama loguna gider.

SMTP iş transaction'ının dışında çalışır. Outbox, sürüm ve süreli sahiplenme ile aynı mesajın eşzamanlı işlenmesini önler. SMTP sonrası süreç çökmesinde yeniden teslim olasılığı vardır.
