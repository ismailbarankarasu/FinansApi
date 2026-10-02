# API kullanım akışı

Bütün şirket yollarında `C = /api/companies/{companyId}`. Kimlikler API yanıtlarından alınmalıdır.

## 1. Hazırlık

1. Kayıt/giriş yapıp Bearer token alın.
2. `GET /api/companies` ile şirket ve rolü seçin. Gerekirse yeni şirket oluşturun.
3. `POST C/periods` ile işlem gününü içeren mali dönem oluşturun.
4. Boş plan için `POST C/accounts/seed` çağırın. Dönen hesap kimliklerini kullanın.
5. `POST C/tax-rates` ile eğitim senaryosunun oranlarını tanımlayın.
6. Müşteri, tedarikçi, ürün ve kasa/banka kartlarını oluşturun. Ürüne stok/maliyet hesabı ve vergi oranı bağlayın.

## 2. İlk bakiyeler

- Kasa/banka parasını dengeli bir manuel yevmiye ile açın: kasa/banka borç, sermaye alacak; kasa/banka boyutunu gönderin. Taslağı onaylayın.
- Ürüne alış faturasıyla stok kazandırabilir veya ilk dönem başlangıcında `POST C/inventory/opening` kullanabilirsiniz.
- Kart üzerinde bakiye/miktar değiştirilmez.

## 3. Satış ve tahsilat

1. `POST C/invoices/preview` ile sunucu toplamlarını görün.
2. `POST C/invoices` ile satış taslağını kaydedin.
3. Dönen sürüm ve işlem anahtarıyla `POST C/invoices/{id}/approve` çağırın.
4. Fatura detayı, stok hareketleri ve yevmiye fişini inceleyin.
5. `POST C/payments`: Collection=1, banka/kasa, müşteri ve fatura dağılımları gönderin.
6. Kalan tutar sıfırlandığında durum Paid olur. PDF'yi fatura endpointinden indirin.

## 4. Alış ve ödeme

Alış faturasını tedarikçiyle oluşturup onaylayın. Stok girişi ve tedarikçi borcu oluşur. `POST C/payments` isteğinde Disbursement=2 kullanın. Kasa/banka bakiyesi tutarı karşılamalıdır.

## 5. Düzeltme

Taslak değiştirilebilir veya silinebilir. Onaylı belgede önce gerekiyorsa ödeme terslenir, sonra gerekçeli fatura iptali yapılır. Stok maliyetini güvenle geri alamayan geçmiş iptal reddedilir. Manuel posted fiş, `journal-entries/{id}/reverse` üzerinden açık dönemde terslenir.

409 yanıtında veriyi yenileyin. Sonucu belirsiz kalan isteği aynı gövde ve aynı `idempotencyKey` ile tekrar gönderin; değiştirdiğiniz işlem için yeni anahtar kullanın.

## 6. Rapor ve kapanış

1. Mizan, KDV, gelir tablosu ve bilanço alın.
2. `reports/reconciliation` kontrollerini inceleyin.
3. Kaynak dönemi izleyen boş ve açık hedef dönemi oluşturun.
4. `periods/{id}/closing-preview?targetPeriodId=` ile engelleri giderin.
5. `close-and-carry-forward` isteğinde `targetPeriodId`, kaynak `version` ve `idempotencyKey` gönderin.
6. Devir sonucundaki kapanış/açılış fişlerini ve yeni dönem stoklarını inceleyin.

Açık fatura kopyalanmaz. Yeni dönemdeki tahsilatta eski fatura kimliği kullanılır. Eski dönemin yevmiyesi değişmez.

## 7. Bildirim

Admin saat dilimi, hatırlatma saati ve şirket iletişimini `automation/settings` üzerinden ayarlar. SMTP bilgileri environment/user-secrets üzerinden verilir. Başarısız mesajlar `automation/messages` listesinde görülür; yetkili kullanıcı Failed mesaj için retry çağırabilir.
