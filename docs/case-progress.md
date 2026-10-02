# Backend case durumu — 2 Ekim 2026

Kapsam, `Finans-Muhasebe-Gelistirme-Plani.md` belgesinin backend bölümleridir. Frontend değişikliği yapılmadı. Kullanıcının son isteği doğrultusunda yeni test dosyası eklenmedi; önceki turda eklenen test klasörü korunuyor.

| Case adımı | Backend karşılığı |
|---|---|
| Veri koruma ve migration | Başlangıç, muhasebe, kategori benzersizliği ve devir ilişkisi migration'ları; SQLite backup/import komutları |
| Mevcut API iyileştirmeleri | Kategori ve işlem servisleri, enum/tutar/tarih kuralları, uyumlu tarih girişi, ortak hatalar |
| Şirket ve dönem | Üyelikle izolasyon, varsayılan eski veri eşlemesi, dönem çakışması/kilidi |
| Rol ve audit | Güncel üyelik kontrolü, son Admin koruması, maskelenmiş önce/sonra, değiştirilemeyen audit |
| Hesap planı ve yevmiye | Eğitim planı, kontrol boyutları, denk taslak/onay/ters fiş |
| Cari | Kartlar, ekstre, güncel açık faturalar ve son hatırlatma |
| Stok | Ürün/vergi kartı, ağırlıklı ortalama, negatif stok koruması, ilk açılış ve dönem devri |
| Kasa/banka | Muhasebe hesabı eşlemesi, ekstre, günlük negatif bakiye kontrolü |
| Fatura | Hesaplama, snapshot, atomik onay, iptal, PDF |
| Ödeme/virman | Fatura dağılımları, fazla ödeme koruması, tersleme, tekrar güvenliği |
| Otomasyon | Kritik stok, günlük vade, atomik outbox sahiplenme, retry/Failed/Skipped |
| Rapor | Mizan, KDV, gelir tablosu, bilanço, cari/fatura-stok-kasa mutabakatı ve PDF |
| Kapanış/devir | Ön kontrol, kaynak kapanış, hedef açılış/stok, hedef mutabakatı ve kaynak kilidi |
| Dokümantasyon | README, mimari, iş kuralları, izinler, kullanım, veri geçişi, OpenAPI |

## Yapılan kontroller

- 1 Ekim: eski yerel dosyanın geçici hedefe aktarımında 1 kullanıcı, 10 kategori ve 14 işlem korundu. İşlem alanları ve ilişki bütünlüğü doğrulandı.
- 2 Ekim: eski kayıtlar varsayılan `Data/finans-accounting.db` dosyasına da aktarıldı; kaynak ve SQLite yedeği korundu.
- 2 Ekim: gerçek HTTP API ve ayrı geçici SQLite dosyasıyla alış → ödeme → satış → kapanış/devir → eski satış faturasını yeni dönemde tahsilat akışı çalıştırıldı.
- Aynı devir isteğinin aynı sonucu döndürdüğü görüldü.
- Tahsilat sonrasında hem eski hem yeni dönemin cari/stok/kasa/KDV mutabakatı geçti; satışın kalan tutarı sıfırlandı.
- Audit, fatura PDF ve mizan PDF endpointleri yanıt verdi. Çalışan API'den OpenAPI belgesi alındı.
- Önceki turda 80 satırlı fatura beş sayfaya render edildi; Türkçe karakter ve sayfalama görsel olarak incelendi.
- Son derleme 0 hata / 0 uyarıyla tamamlandı; EF modelinin son migration ile eşleştiği doğrulandı.
- Güncel OpenAPI 91 HTTP işlemi içeriyor; belgelenmemiş endpoint yolu bulunmadı.
- Bu devam çalışmasında test projesi çalıştırılmadı; derleme ve doğrudan API kontrolü kullanıldı.

## Ortama bağlı sınırlar

- Docker daemon çalışmadığı için imaj/volume akışı burada çalıştırılmadı.
- Gerçek SMTP kimlik bilgisi verilmedi; dış alıcılara e-posta gönderilmedi. Canlı posta teslimi doğrulanmış sayılmaz.
- Planın belirttiği 90 örnek veri yerel eski dosyada bulunmuyor.
- Açılış hedefinin boş olması, geçmiş stok maliyetini yeniden hesaplamama, avans/çoklu para birimi ve dönem yeniden açma desteğinin olmaması ilk sürüm kurallarıdır; ayrıntılar `business-rules.md` içindedir.
