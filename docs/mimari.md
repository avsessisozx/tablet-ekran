# Mimari

## Bileşenler

- **Android tablet uygulaması** (`android/`): ekranı tam ekran kaplayan,
  `8765` portunda HTTP sunan küçük bir uygulama. Gelen JPEG kareleri çözer ve
  gösterir; 5 saniye kare gelmezse saat ekranına döner.
- **Windows göndericisi** (`windows/`): seçilen monitörü yakalar, JPEG'e
  sıkıştırır ve HTTP üzerinden tablete gönderir. WinForms arayüzü tablet IP'si,
  4 haneli kod ve monitör seçimini alır.
- **İsteğe bağlı sanal monitör**: Windows'ta fiziksel ikinci ekran yoksa
  kullanıcı tarafından ayrıca kurulabilir; bu projeye dahil değildir.

## Akış

1. Tablet uygulaması açılır, sunucuyu başlatır ve rastgele 4 haneli bir kod
   üretir.
2. Windows uygulaması `POST /esles` ile kodu gönderir; tablet kodu doğrularsa
   oturum tokenı döner.
3. Windows, seçilen monitörü belirli aralıklarla yakalar; her kareyi JPEG'e
   çevirip `POST /kare` ile gönderir.
4. Tablet kareyi çözer ve ekrana çizer. Yeni kare gelmezse 5 saniye sonra saat
   görünümüne geçer.
5. Windows tarafı durunca `POST /bitir` ile oturumu kapatır.

## Windows tarafı

- Monitörler `Screen.AllScreens` üzerinden sıralanır; birincil monitör önce,
  sonra X/Y konumuna göre listelenir.
- Yakalama `Graphics.CopyFromScreen` ile yapılır; isteğe bağlı olarak en fazla
  genişlik/yükseklik ve JPEG kalitesi sınırlandırılır.
- Gönderim tek bir arka plan görevi ve HTTP bağlantısı üzerinden sıralı yapılır.
- Arayüzde konum değiştirme özelliği yoktur. "Windows ekran ayarlarını aç"
  düğmesi yalnızca ilgili Windows sayfasını açar.

## Android tarafı

- `MainActivity` tam ekran bir `SurfaceView`/görüntü alanı ve saat görünümü
  içerir.
- `BootReceiver`, `BOOT_COMPLETED` sonrası uygulamayı başlatır.
- Yönlendirme `fullSensor` ile sistem tarafından yapılır.
- Uygulama yalnızca yerel ağda çalışacak şekilde tasarlanmıştır.

## Güvenlik sınırı

- Taşıma katmanında TLS/şifreleme yoktur.
- Eşleştirme 4 haneli kod ve oturum tokenına dayanır.
- Uygulama internete açılmamalı; güvenilen yerel ağda kullanılmalıdır.
