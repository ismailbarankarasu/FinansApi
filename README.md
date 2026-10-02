# Finans API — Ön Muhasebe Backend

Şirket ve mali dönem bazında gelir–gider, cari, stok, fatura, tahsilat/ödeme ve muhasebe işlemlerini yöneten ASP.NET Core API.

Fatura onayı; yevmiye, stok, audit ve bildirim kuyruğunu aynı veritabanı transaction'ında kaydeder. Mali raporlar onaylanmış yevmiye satırlarından hesaplanır. Eski gelir–gider kayıtları ayrı tutulur.

## Kullanılan teknolojiler

| Teknoloji | Sürüm | Kullanım |
|---|---|---|
| C# / .NET | `net10.0` | Uygulama çalışma ortamı ve dil |
| ASP.NET Core Web API | .NET 10 | Controller, model doğrulama, middleware ve dependency injection |
| Entity Framework Core SQLite | `10.0.11` | İlişkiler, sorgular, transaction ve concurrency kontrolü |
| EF Core Design / migrations | `10.0.11` | Sürüm kontrollü veritabanı şeması |
| SQLite | EF Core sağlayıcısı üzerinden | Dosya tabanlı ilişkisel veritabanı |
| JWT Bearer | `10.0.11` | İmzalı access token ile kimlik doğrulama |
| ASP.NET Core Identity `PasswordHasher` | .NET 10 | Parola hashleme ve doğrulama; ayrı Identity veritabanı kullanılmaz |
| ASP.NET Core OpenAPI | `10.0.11` | Çalışan endpointlerden OpenAPI belgesi üretimi |
| Scalar | `2.17.3` | Tarayıcıdan API inceleme ve istek gönderme |
| QuestPDF | `2024.12.3` | Türkçe karakter ve çok sayfalı tablo destekli PDF |
| `BackgroundService`, `PeriodicTimer`, `TimeProvider` | .NET 10 | Bildirim kuyruğu ve günlük vade kontrolü |
| `System.Net.Mail` | .NET 10 | SMTP üzerinden e-posta gönderimi |
| Docker / Compose | Proje dosyaları | API konteyneri ve kalıcı SQLite volume'u |

CQRS, MediatR ve generic repository katmanı kullanılmaz. Controller HTTP işlemlerini, servisler iş kurallarını, EF Core veri erişimini yürütür.

## Yerelde çalıştırma

.NET 10 SDK gereklidir. Proje klasöründe:

```powershell
dotnet restore FinansApi.csproj
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet user-secrets set "Jwt:Key" "BURAYA_EN_AZ_32_BAYTLIK_RASTGELE_BIR_DEGER_YAZIN" --project FinansApi.csproj
dotnet run --project FinansApi.csproj --no-launch-profile --urls http://localhost:5000
```

Yukarıdaki JWT değeri bir yer tutucudur; kendiniz rastgele bir değer üretin. User-secrets kullanırken `ASPNETCORE_ENVIRONMENT=Development` ayarlayın. Alternatif olarak `Jwt__Key` environment değişkenini kullanabilirsiniz.

- Sağlık: `http://localhost:5000/api/health`
- Scalar: `http://localhost:5000/scalar/v1`
- OpenAPI: `http://localhost:5000/openapi/v1.json`
- Varsayılan yeni veritabanı: `data/finans-accounting.db`
- Şema migration'ları başlangıçta uygulanır.
- Frontend için varsayılan CORS adresleri: `http://localhost:5173` ve `http://127.0.0.1:5173`.

**Mevcut veriniz varsa önce [veritabanı geçişini](docs/database-upgrade.md) uygulayın.** Eski `data/finans.db` dosyası otomatik olarak değiştirilmez. Yeni dosyaya geçilmesi eski dosyadaki kayıtları kendiliğinden taşımaz.

### Ortam ayarları

| Anahtar | Varsayılan / açıklama |
|---|---|
| `Jwt__Key` | Zorunlu; en az 32 bayt |
| `Jwt__Issuer` | `FinansKurs` |
| `Jwt__Audience` | `FinansKursStudents` |
| `Jwt__ExpiresMinutes` | `480`; 1–10080 dakika |
| `ConnectionStrings__Default` | `Data Source=data/finans-accounting.db` |
| `Seed__Demo` | `false`; yalnız boş veritabanına eski gelir–gider örneklerini ekler |
| `Automation__Enabled` | `true`; worker her 30 saniyede kuyruk ve günlük çalışma zamanını kontrol eder |
| `Smtp__Host`, `Smtp__Port` | SMTP sunucusu; port varsayılanı `1025` |
| `Smtp__From` | Gönderen e-posta adresi |
| `Smtp__Username`, `Smtp__Password` | Gerekirse SMTP kimlik bilgileri |
| `Smtp__EnableSsl` | `false`; sunucunuza uygun ayarlayın |
| `Cors__Origins__0`, `Cors__Origins__1` | İzin verilen frontend adresleri |

`.env.example`, Compose için örnek anahtarları içerir. `dotnet run` `.env` dosyasını otomatik okumaz. JWT ve SMTP sırlarını kaynak koduna eklemeyin.

Demo seed açıkken boş veritabanına `demo@kurs.com` / `Demo123!` eğitim hesabı eklenir. Mevcut kullanıcı bulunan veritabanında seed çalışmaz. Muhasebe kartları ve işlemleri ayrıca API'den oluşturulur.

### Docker

```powershell
Copy-Item .env.example .env
# .env içindeki JWT_KEY değerini doldurun.
docker compose up --build -d
```

Dış port `5000`, konteyner portu `8080`'dir. `finans-db` volume'u `/app/data` altında kalıcı veri tutar. SMTP ayarları Compose üzerinden aktarılır. Eski volume'daki `finans.db` için otomatik dönüşüm yapılmaz; [geçiş yönergesini](docs/database-upgrade.md) izleyin.

## Kod düzeni

```text
Auth/                  JWT ayarları, token üretimi ve kullanıcı claim'i
Authorization/         Şirkete bağlı rol/izin matrisi
Controllers/           HTTP endpointleri
Dtos/                  İstek ve yanıt sözleşmeleri
Data/                  DbContext, eski modeller, veri aktarımı ve seed
Data/Accounting/       Muhasebe modelleri ve EF yapılandırması
Services/Accounting/   Şirket, yevmiye, fatura, stok, ödeme, rapor ve kapanış
Services/Categories/   Eski kategori kuralları
Services/Transactions/ Eski gelir–gider kuralları
Infrastructure/        Ortak hatalar, tarih uyumluluğu, SMTP ve PDF
BackgroundJobs/        Outbox sahiplenme, gönderim ve vade hatırlatma
Migrations/            Şema değişiklikleri
```

İş verileri tipli DTO ve entity sınıflarıyla taşınır. Ayarlar için `JwtSettings` ve `SmtpOptions` kullanılır. Dictionary/indexer kullanımı altyapının gerektirdiği OpenAPI ve hata metaverisi gibi yerlerle sınırlıdır. Kod biçimi `.editorconfig` ile tanımlıdır.

## Kimlik, yetki ve ortak sözleşme

1. `POST /api/auth/register` veya `POST /api/auth/login` ile token alın.
2. Korumalı çağrılara `Authorization: Bearer <token>` ekleyin.
3. Şirket listesi kullanıcının üyeliğini ve şirketteki `role` değerini döndürür.
4. Şirket yetkileri her istekte veritabanındaki güncel üyelikten doğrulanır. JWT içine şirket rolü sabitlenmez.

| Rol | Değer | Yetki özeti |
|---|---:|---|
| Admin | 1 | Şirket, üyeler ve bütün muhasebe işlemleri |
| Accountant | 2 | Muhasebe, kartlar, onay, ödeme, rapor, audit ve kapanış |
| Sales | 3 | Müşteri kartı ve satış taslağı; satış kapsamındaki okuma işlemleri |
| Reader | 4 | Şirket verileri ve raporlar; yazma yetkisi yok |

- Muhasebe iş tarihleri `YYYY-MM-DD` / `DateOnly` biçimindedir; teknik zamanlar UTC'dir.
- Tek para birimi `TRY`'dir. Para hesapları `decimal`, yeni muhasebe tutarlarının SQLite saklaması integer kuruştur. Miktar, birim fiyat ve oranlar dört ondalık basamak destekler.
- Yuvarlama `MidpointRounding.AwayFromZero` ile yapılır. Fatura toplamları yuvarlanmış satırlardan hesaplanır.
- Enum alanları JSON'da sayısaldır: `EntryType` Income=1, Expense=2; `InvoiceType` Sales=1, Purchase=2; `PaymentDirection` Collection=1, Disbursement=2; `PartyType` Customer=1, Supplier=2, Both=3; `TreasuryType` Cash=1, Bank=2.
- Fatura durumları: Draft=1, Approved=2, PartiallyPaid=3, Paid=4, Cancelled=5. Yevmiye Draft=1, Posted=2; dönem Open=1, Locked=2.
- Hesap sınıfları: Asset=1, Liability=2, Equity=3, Income=4, Expense=5.
- Güncelleme/onay işlemlerinde güncel `version` gönderin. Onay, tersleme, ödeme, virman, stok açılışı ve devirde 8–100 karakterlik `idempotencyKey` kullanın. Aynı işlem tekrarlanırken anahtar ve gövde değişmemelidir.
- Hatalar `ProblemDetails` / `ValidationProblemDetails` içerir; `message`, `traceId` ve doğrulama hatalarında `errors` bulunur.
- `400`: geçersiz veri; `401`: giriş gerekli; `403`: yetki yetersiz; `404`: bulunamayan/erişilemeyen şirket kaydı; `409`: durum, bakiye, dönem veya concurrency çatışması.
- Sayfalı listeler `{ items, totalCount, page, pageSize }` döndürür. Yeni muhasebe listelerinde `pageSize` en fazla 100; eski işlem listesinde 50'dir.

## API endpointleri

Aşağıdaki şirket tablolarında **`C` = `/api/companies/{companyId}`**. `{id}` ilgili kaydın kimliğidir. Request alanlarının tamamı [OpenAPI dosyasında](docs/api-v1.json) ve çalışan Scalar arayüzünde görülebilir.

### Kimlik ve sistem

| Metot | Endpoint | Açıklama |
|---|---|---|
| POST | `/api/auth/register` | Kayıt, varsayılan şirket/dönem ve kategori oluşturma |
| POST | `/api/auth/login` | JWT ve kullanıcı bilgisi |
| GET | `/api/auth/me` | Giriş yapan kullanıcı |
| GET | `/api/health` | Sağlık kontrolü; token gerekmez |
| GET | `/openapi/v1.json` | OpenAPI belgesi |
| GET | `/scalar/v1` | İnteraktif API arayüzü |

### Şirket, üyelik ve dönem

| Metot | Endpoint | Açıklama |
|---|---|---|
| GET / POST | `/api/companies` | Erişilebilir şirketler / yeni şirket |
| GET / PUT | `C` | Şirket detayı / ayarlarını değiştirme |
| GET / POST | `C/members` | Üyeler / kayıtlı kullanıcıya üyelik verme |
| PUT / DELETE | `C/members/{userId}` | Rol/aktiflik değiştirme / üyeliği pasifleştirme |
| GET / POST | `C/periods` | Dönemler / çakışmayan dönem oluşturma |
| GET | `C/periods/{id}` | Dönem ve kilit bilgisi |
| GET | `C/periods/{id}/closing-preview?targetPeriodId=` | Kapanış engelleri ve mutabakatlar |
| POST | `C/periods/{id}/close-and-carry-forward` | Kapanış, açılış, stok devri ve kaynak dönem kilidi |
| GET | `C/periods/{id}/carry-forward-result` | Oluşan fişler ve devir sonucu |
| GET | `C/audit-logs` | `from`, `to`, `entityType`, `page`, `pageSize` filtreli audit |

Son aktif Admin üyeliği kaldırılamaz. Kilitli dönemi yeniden açan endpoint yoktur. Şirket pasifleştirildiyse Admin `PUT C` ile yeniden aktifleştirebilir.

### Hesap planı ve yevmiye

| Metot | Endpoint | Açıklama |
|---|---|---|
| GET / POST | `C/accounts` | Hesap planı / hesap oluşturma |
| PUT | `C/accounts/{id}` | Kullanım ve üst hesap kontrolleriyle güncelleme |
| POST | `C/accounts/seed` | Boş hesap planına eğitim hesaplarını ve posting profilini ekleme |
| GET / PUT | `C/posting-profile` | Cari, satış, vergi ve sonuç hesabı eşlemeleri |
| GET / POST | `C/journal-entries` | Fiş listesi / dengeli taslak oluşturma |
| GET / PUT / DELETE | `C/journal-entries/{id}` | Detay / yalnız taslakta güncelleme veya silme |
| POST | `C/journal-entries/{id}/post` | Taslağı onaylama |
| POST | `C/journal-entries/{id}/reverse` | Manuel fişi açık dönemde tersleme |

Fiş listesi `periodId`, `status`, `from`, `to`, `page`, `pageSize` alır. En az iki satır gerekir; her satırda yalnız borç veya alacak pozitif olmalıdır. Cari ve kasa/banka kontrol hesaplarında ilgili alt kayıt kimliği zorunludur. Belge kaynaklı fişler ilgili faturadan/ödemeden terslenir.

### Cari

| Metot | Endpoint | Açıklama |
|---|---|---|
| GET / POST | `C/counterparties` | Cari listesi / müşteri veya tedarikçi oluşturma |
| GET / PUT / DELETE | `C/counterparties/{id}` | Detay / güncelleme / pasifleştirme |
| GET | `C/counterparties/{id}/statement?periodId=&from=&to=` | Açılış, hareketler ve koşan bakiye; ayrıca güncel açık faturalar ve son hatırlatma |
| GET | `C/reports/overdue?type=` | Vadesi geçmiş açık satış/alış faturaları |

Cari listesinde `type`, `q`, `page`, `pageSize` kullanılabilir. Ekstrede borç pozitif, alacak negatiftir. `CurrentOutstandingInvoices` güncel durumdur; tarih filtreli muhasebe satırlarından ayrı sunulur.

### Ürün, vergi ve stok

| Metot | Endpoint | Açıklama |
|---|---|---|
| GET / POST | `C/products` | Ürün listesi / ürün kartı |
| GET / PUT | `C/products/{id}` | Ürün detayı / güncelleme |
| GET | `C/products/{id}/stock-movements?periodId=&from=&to=` | Miktar, maliyet ve kaynak belge hareketleri |
| GET | `C/inventory/balances?periodId=` | Ürün bazında stok miktarı ve değeri |
| POST | `C/inventory/opening` | İlk stok miktarı/değeri ve dengeli açılış fişi |
| GET / POST | `C/tax-rates` | Şirkete ait oranlar / oran oluşturma |

Vergi oranı kodda sabitlenmez; API'den tanımlanır ve faturaya snapshot olarak alınır. Maliyet hareketli ağırlıklı ortalamadır. Negatif stok ve son stok hareketinden eski tarihli maliyet hareketi reddedilir. Stok açılışı yalnız dönem başlangıcında, daha önce hareketi olmayan ürüne yapılır.

### Kasa / banka

| Metot | Endpoint | Açıklama |
|---|---|---|
| GET / POST | `C/treasury-accounts` | Hesaplar / yeni kasa veya banka |
| GET / PUT | `C/treasury-accounts/{id}` | Detay / güncelleme veya `isActive=false` ile pasifleştirme |
| GET | `C/treasury-accounts/{id}/statement?periodId=&from=&to=` | Açılış, hareketler ve bakiye |
| GET | `C/treasury-accounts/{id}/reconciliation?periodId=&date=` | Alt hesap ve muhasebe hesabı mutabakatı |

Kasa `100.*`, banka `102.*` alt hesabına bağlıdır. Bakiye alanı elle değiştirilmez; açılış parası dengeli yevmiye fişiyle girilir. Negatif kasa ve banka bakiyesi reddedilir.

### Fatura

| Metot | Endpoint | Açıklama |
|---|---|---|
| GET / POST | `C/invoices` | Fatura listesi / alış veya satış taslağı |
| GET / PUT / DELETE | `C/invoices/{id}` | Detay / yalnız taslakta güncelleme veya silme |
| POST | `C/invoices/preview` | Kaydetmeden sunucuda tutar hesaplama |
| POST | `C/invoices/{id}/approve` | Atomik onay, stok ve yevmiye kaydı |
| POST | `C/invoices/{id}/cancel` | Gerekçeli iptal, ters fiş ve ters stok hareketi |
| GET | `C/invoices/{id}/pdf` | Kaydedilmiş satır ve toplamlarla PDF |

Liste filtreleri: `periodId`, `type`, `status`, `counterpartyId`, `from`, `to`, `page`, `pageSize`. Detay yanıtı `invoice`, `paidAmount`, `remainingAmount`, `payments` ve `lastReminderAtUtc` içerir.

Ödemeli faturada önce ödeme terslenmelidir. Ürünün sonraki stok hareketi varsa veya fatura başka döneme devredildiyse stok maliyetini geçmişe dönük değiştirecek iptal reddedilir. Aynı ürünün satırları tek satırda birleştirilmelidir.

### Tahsilat, ödeme ve virman

| Metot | Endpoint | Açıklama |
|---|---|---|
| GET / POST | `C/payments` | Ödemeler / faturaya dağıtılan tahsilat veya ödeme |
| GET | `C/payments/{id}` | Ödeme ve dağılım detayı |
| POST | `C/payments/{id}/reverse` | Gerekçeli ters kayıt ve fatura durumlarını yeniden hesaplama |
| GET / POST | `C/transfers` | Virman listesi / iki kasa-banka arasında transfer |

Ödeme listesinde `periodId`, `page`, `pageSize`; virman listesinde `periodId` kullanılabilir. Dağılımların toplamı ödeme tutarına eşit olmalıdır. Fazla tahsilat ve avans desteklenmez. Yeni dönemde eski dönemin açık faturası tahsil edilebilir; eski yevmiye değiştirilmez.

### Rapor ve PDF

| Metot | Endpoint | Açıklama |
|---|---|---|
| GET | `C/reports/trial-balance?periodId=&from=&to=` | Hesap bazında borç, alacak ve bakiyeler |
| GET | `C/reports/vat-summary?periodId=&from=&to=` | Fatura KDV snapshot'ları ve yevmiye mutabakatı |
| GET | `C/reports/income-statement?periodId=&from=&to=` | Gelir, gider ve dönem sonucu; kapanış fişleri hariç |
| GET | `C/reports/balance-sheet?periodId=&to=` | Varlık, yükümlülük, özkaynak ve denklik |
| GET | `C/reports/reconciliation?periodId=` | Fiş, cari/fatura, stok, kasa/banka ve KDV kontrolleri |
| GET | `C/reports/{kind}/pdf?periodId=&from=&to=` | Aynı rapor verisinden PDF |

`kind`: `trial-balance`, `vat-summary`, `income-statement`, `balance-sheet`, `reconciliation`. Bilanço `to` tarihine kadarki bakiyeyi, mutabakat dönemin tamamını kullanır; bu iki PDF'de uygulanmayan filtreler kapsam başlığında gösterilmez.

### Otomasyon

| Metot | Endpoint | Açıklama |
|---|---|---|
| GET | `C/automation/messages?status=&page=&pageSize=` | Kuyruk, gönderim ve hata durumları |
| POST | `C/automation/messages/{id}/retry` | `Failed` mesajı yeniden kuyruğa alma |
| GET / PUT | `C/automation/settings` | Şirket saat dilimi, hatırlatma saati ve iletişim adresi |

Kritik stok uyarısı stok yeniden eşiğe ulaşana kadar tekrar üretilmez. Vade kontrolü varsayılan olarak şirketin `Europe/Istanbul` saat diliminde 09:00 sonrasında günde bir kez çalışır. Gönderimden önce faturanın kalan borcu tekrar kontrol edilir.

Mesaj durumları: `Pending`, `Processing`, `Sent`, `Failed`, `Skipped`. Worker mesajı sürüm kontrolüyle sahiplenir; gönderimi transaction dışında yapar. Hatalar artan bekleme süreleriyle en fazla beş kez denenir. SMTP gönderiminden hemen sonra süreç çökerse aynı e-posta tekrar gönderilebilir; exactly-once teslim garantisi yoktur.

### Eski gelir–gider API'si

| Metot | Endpoint | Açıklama |
|---|---|---|
| GET / POST | `/api/categories` | Kategori listesi / kategori oluşturma |
| PUT / DELETE | `/api/categories/{id}` | Güncelleme / kullanılmayan kategoriyi silme |
| GET / POST | `/api/transactions` | İşlem listesi / işlem oluşturma |
| PUT / DELETE | `/api/transactions/{id}` | Güncelleme / silme |
| GET | `/api/reports/summary?from=&to=` | Gelir, gider ve bakiye |
| GET | `/api/reports/by-category?from=&to=&type=` | Kategori toplamları |
| GET | `/api/reports/monthly?year=` | Aylık gelir/gider |
| GET | `/api/reports/recent?take=` | Son işlemler |

Bu endpointlerde `?companyId=` isteğe bağlıdır; verilmezse kullanıcının varsayılan şirketi kullanılır. Yazma gövdelerinde `companyId`, işlemlerde ayrıca `fiscalPeriodId` verilebilir. Dönem verilmezse tarihe göre bulunur. Dönem oluşturulmadan dönem dışı kayıt yapılamaz.

İşlem listesi `type`, `categoryId`, `from`, `to`, `q`, `page`, `pageSize` alır. Eski istemciyle uyumluluk için işlem girişinde `YYYY-MM-DD` yanında ISO timestamp kabul edilir; gönderilen takvim günü korunur. Eski yanıtın `DateTime` biçimi korunmuştur. Bu kayıtlar yevmiye, stok ve mali raporlara otomatik dönüştürülmez.

## Örnek fatura ve onay isteği

```json
{
  "fiscalPeriodId": 1,
  "counterpartyId": 4,
  "type": 1,
  "date": "2026-10-02",
  "dueDate": "2026-11-02",
  "lines": [
    { "productId": 7, "quantity": 2, "unitPrice": 100, "discountAmount": 0 }
  ]
}
```

Önce `POST C/invoices`; dönen gerçek `id` ve `version` ile `POST C/invoices/{id}/approve`:

```json
{
  "version": 1,
  "idempotencyKey": "fatura-onay-20261002-0001"
}
```

İptal/tersleme gövdesine ayrıca `reason`, gerektiğinde hedef açık dönemin `fiscalPeriodId` ve `date` değerleri eklenir. Örnek kimlikler veritabanınızdaki gerçek kayıtlarla değiştirilmelidir.

## Ek belgeler ve doğrulama

- [Mimari ve servis sorumlulukları](docs/architecture.md)
- [İş kuralları ve sınırlar](docs/business-rules.md)
- [Rol ve izin matrisi](docs/permissions.md)
- [Veritabanı yedekleme ve geçiş](docs/database-upgrade.md)
- [API üzerinden kullanım akışı](docs/user-guide.md)
- [Case ilerlemesi ve yapılan kontroller](docs/case-progress.md)

Derleme: `dotnet build FinansApi.csproj`. Yeni test dosyaları bu devam çalışmasının kapsamına alınmadı. Önceki turdaki test klasörü korunmuştur; ana API projesinin derleme ve yayın içeriğine dahil değildir.

Bu bir eğitim uygulamasıdır. Hesap planı ve PDF çıktıları yasal e-Fatura/e-Defter entegrasyonu oluşturmaz. QuestPDF bu eğitim projesinde Community lisans ayarıyla çalışır.
