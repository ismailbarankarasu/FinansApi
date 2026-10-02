# Veritabanı geçişi ve geri dönüş

## Dosyalar

- Eski gelir–gider veritabanı: `data/finans.db` (`EnsureCreated` ile oluşturulmuş olabilir).
- Yeni migration veritabanı: `data/finans-accounting.db`.
- Windows'ta `Data` ve `data` aynı klasöre karşılık gelebilir. Linux konteynerinde yol `/app/data`'dır.

Eski dosyada `__EFMigrationsHistory` yoksa API başlangıçta tablo oluşturma migration'larını bu dosyaya uygulamaz; açıklayıcı hatayla durur.

## Yerel geçiş

Önce uygulamayı derleyin. Aktarım sırasında eski uygulamanın yazmasını durdurun; SQLite backup tutarlı bir anlık kopya üretse de kopyadan sonra eski uygulamaya yazılan veriler yeni dosyaya aktarılmaz.

```powershell
dotnet build FinansApi.csproj
dotnet bin/Debug/net10.0/FinansApi.dll --import-legacy data/finans.db data/finans-accounting.db
```

Bu komut:

1. Kaynağı SQLite `BackupDatabase` API'siyle `finans-accounting.db.legacy-backup` dosyasına yedekler.
2. `PRAGMA integrity_check` ile yedeği doğrular.
3. Yeni dosyaya migration'ları uygular.
4. Kullanıcı, kategori ve işlemleri kimlikleri korunarak aktarır.
5. Her kullanıcıya varsayılan şirket ve Admin üyeliği verir; işlem yıllarına uygun mali dönemler oluşturur.
6. Kategorilerin `CompanyId`, işlemlerin `CompanyId` ve `FiscalPeriodId` alanlarını doldurur.
7. Kaynak/hedef kayıt sayıları, işlem alanlarının eşitliği ve foreign key bütünlüğünü kontrol eder.

Eski şifre hashleri, işlem tutarları ve işlem günleri korunur. Kayıtlar fatura veya yevmiye fişine dönüştürülmez. Hedef dosya veya yedek mevcutsa komut üzerine yazmaz; yeni bir hedef adı seçin. Aynı şirkette aynı ad/türden eski kategoriler varsa benzersizlik kontrolü aktarımı reddeder; kaynak korunur ve veriler ayrıca incelenmelidir.

Varsayılan bağlantı yeni dosyayı gösterir. Farklı hedef kullanırsanız `ConnectionStrings__Default` değerini güncelleyin.

## Yalnız yedek alma

```powershell
dotnet bin/Debug/net10.0/FinansApi.dll --backup-db data/finans-accounting.db data/backup-20261002.db
```

Çalışan SQLite dosyasını sıradan dosya kopyalama ile yedeklemeyin; WAL dosyaları nedeniyle eksik kopya oluşabilir. Bu komut mevcut hedefin üzerine yazmaz.

## Docker volume geçişi

Önce yeni imajı derleyin, eski API'yi durdurun. Aynı Compose projesini kullanın; böylece aynı volume bağlanır.

```powershell
docker compose build api
docker compose stop api
docker compose run --rm --no-deps api --import-legacy /app/data/finans.db /app/data/finans-accounting.db
docker compose up -d api
```

JWT ayarı `.env` içinde tanımlı olmalıdır; Compose bu ayarı komut başlamadan doğrular. `docker compose down -v` volume'u siler; veri koruma akışında kullanılmaz.

## Sonraki migration'lar

`Migrations/` sürüm kontrollüdür. Yeni şema değişikliği için EF Core 10.0.11 ile uyumlu `dotnet-ef` kullanın:

```powershell
dotnet ef migrations add DegisiklikAdi --project FinansApi.csproj
```

`DesignTimeDbContextFactory` tasarım zamanı modelini sağlar. API normal başlangıçta migration'ları uygular. Özellikle SQLite tablo yeniden oluşturma migration'ları kesinti halinde kısmen uygulanabilir; mevcut migration veritabanını da güncellemeden önce yedekleyin.

## Geri dönüş

Eski dosya değişmeden kalır. Aktarım doğrulanmadan eski uygulamaya yeniden yazma açmayın. Geri dönüş gerekiyorsa yeni API'yi durdurun ve **eski uygulama sürümünü**, korunan eski veritabanına bağlayın. Yeni API'yi migration geçmişi olmayan dosyaya yönlendirmek geri dönüş yöntemi değildir.

Yeni sistemde sonradan oluşan faturalar/ödemeler eski şemaya aktarılamaz. Canlı geçiş sonrası geri dönüşte bu kayıtlar ayrıca ele alınmalıdır. Migration veritabanı için yedeği ayrı dosyaya geri yükleyip uygun uygulama sürümüyle açın.

## Bu çalışma alanındaki kontrol

1 Ekim 2026'da yerel eski dosyada 1 kullanıcı, 10 kategori ve 14 işlem bulundu; geçici hedefe aktarımda sayılar ve işlem alanları eşleşti. Planın sözünü ettiği 90 örnek bu dosyada bulunmuyor. Docker daemon çalışmadığından Docker volume geçişi bu ortamda uygulanmadı.

2 Ekim 2026'da aynı aktarım varsayılan yerel hedef olan `Data/finans-accounting.db` dosyasına da uygulandı. Kaynak `Data/finans.db` ve tutarlı `Data/finans-accounting.db.legacy-backup` yedeği korundu; 1 kullanıcı, 10 kategori ve 14 işlem doğrulandı. Bu çalışma alanında komutu aynı hedefe yeniden çalıştırmak gerekmez.
