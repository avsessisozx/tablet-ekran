# Protokol

Tablet, `8765` TCP portunda HTTP/1.1 sunar. Gövde olarak JSON kullanılır.
Taşıma şifrelenmez; yalnızca yerel ağ içindir.

## Uç noktalar

### `GET /bilgi`

Kimlik doğrulaması istemez. Örnek yanıt:

```json
{"ad":"TabletEkran","surum":"0.3","kod_var":true}
```

`kod_var`, tablette eşleştirme kodunun üretilmiş olduğunu belirtir.

### `POST /esles`

Gövde, tablette gösterilen 4 haneli kodu içerir. Kod doğruysa oturum için
rastgele bir token döner. Kod yanlışsa istek reddedilir.

### `POST /kare`

`Authorization: Bearer <TOKEN>` başlığı ve JPEG gövdesi ile bir kare gönderir.
Token geçersizse istek reddedilir. Tablet kareyi çözer ve gösterir.

### `POST /bitir`

Oturumu kapatır; token geçersiz hale gelir.

## Zamanlama ve hata davranışı

- Tablet 5 saniye boyunca kare almazsa saat ekranına döner.
- Windows tarafı bağlantı koptuğunda yeniden eşleşme gerektirir.
- Aynı anda tek oturum desteklenir.
- Hata yanıtları kısa JSON ve uygun HTTP durum kodu içerir.

## Sınırlar

- Ses, dokunmatik veya fare olayı taşınmaz.
- Şifreleme, sertifika veya kullanıcı hesabı yoktur.
- Protokol Chromecast, Miracast veya Google Cast değildir; bu protokollerle
  uyumlu değildir.
