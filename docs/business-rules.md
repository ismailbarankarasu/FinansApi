# İş kuralları

## Şirket ve dönem

- Her muhasebe kaydı şirkete; mali hareketler ayrıca döneme bağlıdır. İlişkili kimlikler aynı şirketten olmalıdır.
- Aktif şirket sunucuda kullanıcıya ait global bir alan değildir. Her muhasebe route'u şirket kimliğini taşır. Eski API yalnız uyumluluk için varsayılan şirket kullanır.
- Aynı şirkette mali dönemler çakışmaz. İşlem günü seçilen dönem içinde olmalıdır.
- Kilitli dönemde hareket oluşturma/değiştirme/silme/onaylama yapılamaz. Açık hedef döneme izin verilen ters kayıtlar kaynak kaydı korur.

## Yevmiye, para ve stok

- Taslak dahil fiş en az iki satır içerir. Her satırda yalnız borç veya alacak pozitiftir; toplamlar eşittir.
- Aktif, kayıt kabul eden alt hesap kullanılır. Cari ve kasa/banka boyutları zorunlu hesaplarda boş bırakılamaz.
- Onaylı fiş doğrudan düzenlenmez veya silinmez. Ticari belge fişi manuel endpointten terslenemez.
- Yeni muhasebe para değerleri integer kuruş; miktar, oran ve birim değerleri dört ondalık ölçekle saklanır. Eski işlem tablosunun tutar saklama biçimi veri koruması için değiştirilmemiştir.
- Fatura satırı neti ve KDV ayrı ayrı kuruşa yuvarlanır; belge toplamı bu satırlardan oluşur.
- Stok maliyeti hareketli ağırlıklı ortalamadır. İndirilebilir alış KDV'si maliyete eklenmez. Son satışta kalan stok maliyeti tamamen tüketilir.
- Geçmişe dönük maliyet yeniden hesaplaması ve negatif stok desteklenmez. Ürün kartında serbestçe yazılabilir bakiye alanı yoktur.

## Fatura ve ödeme

- Taslak mali bakiye veya stok etkilemez. Ürün adı, fiyatı, oranı ve tutarları fatura satırında korunur.
- Satış fişi: alıcı borç / gelir ve KDV alacak. Maliyet fişi: maliyet borç / stok alacak.
- Alış fişi: stok ve indirilecek KDV borç / satıcı alacak. Hesaplar posting profilinden alınır.
- Ödeme tek cariye ve uygun türde faturalara dağılır; dağılım toplamı tutara eşittir. Fazla ödeme ve avans reddedilir.
- Tahsilat kasa/banka borç, alıcı alacak; ödeme satıcı borç, kasa/banka alacak üretir.
- Kasa/banka için tarihe göre koşan bakiye negatif olamaz. Virman gelir/gider oluşturmaz.
- Ödemeli fatura iptalinden önce ödeme terslenir. Sonraki stok hareketleri varsa maliyeti güvenle geri alınamayan iptal engellenir.

## Rapor ve kapanış

- Gelir tablosu kapanış fişlerinden etkilenmeden faaliyet sonucunu gösterir.
- Bilanço, henüz kapatılmamış sonucu özkaynağa ekler; sonuç aktarılmışsa ikinci kez saymaz.
- Cari mutabakatı fatura, ödeme, manuel cari fişi ve terslemelerin rapor tarihindeki etkisini karşılaştırır. Sonraki dönemdeki tahsilat eski dönemin raporunu değiştirmez.
- Kapanış taslak belgeleri, fiş denkliğini, hesap eşlemelerini, cari/stok/kasa/KDV mutabakatlarını denetler.
- Açık fatura tek başına kapanış engeli değildir. Fatura kopyalanmaz; bakiye cari boyutuyla açılışa taşınır.
- Hedef dönem ardışık ve açık olmalı, henüz belge/hareket içermemelidir. Bu ilk sürümün bilinçli sınırıdır.
- Gelir/gider kapatılır; yalnız bilanço hesapları cari/kasa boyutlarıyla ve stok miktar/değeri açılışa taşınır. Hedef mutabakatı başarısızsa işlem geri alınır.
- Kaynak dönem kilitlenir. Yeniden açma endpoint'i yoktur. Hareketsiz dönemde gereksiz sıfır fiş yerine devir kaydındaki fiş kimliği `null` olabilir.

## Otomasyon

- Kritik stok bildirimi eşik altındaki bir olay için bir kez üretilir; stok eşik seviyesine gelince yeni uyarıya izin verilir.
- Önerilen sipariş miktarı `max(2 * kritik eşik - mevcut miktar, 1)` formülüdür. E-posta stok girişi veya alış faturası yaratmaz.
- Günlük hatırlatma şirket/fatura/iş günü ile benzersizdir. Uygulama gün içinde geç açılırsa uygun saatten sonraki ilk tur o günün kontrolünü yapar; geçmiş her gün için ayrı bir e-posta yığını oluşturmaz.
- SMTP veya alıcı eksikliği görünür kuyruk hatasıdır. Gönderimden önce kapanmış/iptal olmuş fatura mesajı atlanır.
