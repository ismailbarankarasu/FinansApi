# Şirket izinleri

Rol her şirket üyeliğinde ayrı tutulur. `Authorization/Permissions.cs` izin matrisinin kod karşılığıdır. Sunucu her istekte aktif üyeliği ve rolü veritabanından okur.

| İzin | Admin | Accountant | Sales | Reader |
|---|---|---|---|---|
| Şirket ayarları, üye/rol yönetimi | Evet | Hayır | Hayır | Hayır |
| Muhasebe verilerini okuma | Evet | Evet | Yalnız satış kapsamı | Evet |
| Müşteri kartı ve satış taslağı | Evet | Evet | Evet | Hayır |
| Tedarikçi, hesap planı, ürün, vergi, kasa/banka | Evet | Evet | Hayır | Hayır |
| Yevmiye ve alış faturası | Evet | Evet | Hayır | Hayır |
| Fatura onayı, iptal ve ters kayıt | Evet | Evet | Hayır | Hayır |
| Tahsilat, ödeme, virman | Evet | Evet | Hayır | Hayır |
| Mali rapor ve PDF | Evet | Evet | Hayır | Evet |
| Audit ve kuyruk durumunu okuma | Evet | Evet | Hayır | Hayır |
| Başarısız e-postayı yeniden kuyruğa alma | Evet | Evet | Hayır | Hayır |
| Dönem kapatma ve devir | Evet | Evet | Hayır | Hayır |

Sales rolü satış faturası PDF'sine erişebilir; mali rapor PDF'sine erişemez. Tedarikçi kartları ve alış faturaları satış kapsamından gizlenir. Both türündeki cari görülebilir, ancak müşteri dışındaki türü değiştirme yetkisi muhasebe rolüne aittir.

Başka şirketin kaydı için 404, üyesi olduğu şirkette izin verilmeyen işlem için 403 döner. Frontend'de buton gizlemek yetkilendirme yerine geçmez. Son aktif Admin çıkarılamaz. Audit ve bildirim SMTP parolası gibi sırları döndürmez.
