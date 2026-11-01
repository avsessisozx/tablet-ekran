# Sürüm Notları

## İlk sürüm — 2026

İlk herkese açık sürüm. İki bileşen birlikte yayımlanır:

- **TabletEkran-0.3.apk** — Android tablet uygulaması.
- **TabletEkranGonderici-1.1.1.zip** — Windows göndericisi (exe + gerekli
  dll/json dosyaları).

### Özellikler

- Tablet ile Windows bilgisayar arasında yerel ağ üzerinden JPEG tabanlı
  görüntü aktarımı (tablet tarafında HTTP, port 8765).
- Dört haneli eşleştirme kodu.
- Windows'ta monitör seçimi; seçilen monitörün karelerinin gönderilmesi.
- Yaklaşık 12 kare/sn hedefi; 5 saniye kare gelmezse tablette saat ekranı.
- Tablet uygulamasının açılışta otomatik başlaması ve `fullSensor` ile ekran
  yönünü ayarlaması.
- Windows arayüzünde "Windows ekran ayarlarını aç" düğmesi. Monitör konumu
  Windows ekran ayarlarından kullanıcı tarafından yapılır.

### Doğrulanan ortam

- Vestel P10, Android 4.1.1 (API 16).
- Windows 10 22H2.
- 4 haneli kod ile eşleşme, `/bilgi`, 10 saniyelik yayın ve saat ekranına
  dönüş.

### Bilinen sınırlar

- Ses aktarımı yoktur.
- Dokunmatik/fare olayları tablete geri gönderilmez.
- Video akıcı değildir; JPEG kareler kullanılır.
- Şifreleme yoktur; yalnızca güvenilen yerel ağda kullanılmalıdır.
- Dört yönlü döndürme ve her konumda fare geçişi tam olarak doğrulanmamıştır.
- Windows 10 dışındaki Windows sürümleri test edilmemiştir.
- APK, her derlemede üretilen geçici bir debug anahtarıyla imzalanır. Daha önce
  farklı bir anahtarla kurulmuş sürümlerde imza uyuşmazlığı olabilir; bu durumda
  eski uygulamayı kaldırıp yeniden kurun.

### Kurulum

- Android: `TabletEkran-0.3.apk` dosyasını kurun.
- Windows: `.NET 8 Desktop Runtime` kurun; zip'i çıkarıp
  `TabletEkranGonderici.exe` dosyasını çalıştırın.

### Lisans

MIT — 2026 avsessisozx. Ayrıntılar için `LICENSE` dosyasına bakın.
