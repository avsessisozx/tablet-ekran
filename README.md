# TabletEkran

Eski bir Android tableti, aynı yerel ağdaki Windows bilgisayar için kablosuz
ikinci ekran / ekran alıcısı olarak kullanmanızı sağlayan küçük bir araçtır.
Tablet uygulaması görüntüyü alır ve tam ekran gösterir; Windows tarafı seçilen
monitörün karelerini JPEG olarak gönderir.

Bu proje Chromecast, Miracast veya Google Cast ile uyumlu değildir; kendi basit
HTTP/JPEG protokolünü kullanır.

## Nasıl çalışır?

- Tablet uygulaması `8765` numaralı TCP portunda basit bir HTTP sunucusu açar.
- Windows uygulaması tablete bağlanır ve tablette gösterilen 4 haneli eşleştirme
  kodunu girer.
- Eşleşme sonrası Windows, seçilen monitörü yakalayıp JPEG kareler halinde
  gönderir. Tipik hız yaklaşık 12 kare/sn'dir; ağa ve tablete göre değişir.
- Tablet 5 saniye boyunca yeni kare alamazsa saat ekranına döner.
- Tablet uygulaması açılışta otomatik başlayacak şekilde yapılandırılmıştır ve
  ekran yönünü `fullSensor` ile otomatik ayarlar.
- Ses aktarılmaz; dokunmatik veya fare olayları tablete geri gönderilmez.

## Kurulum

### Tablet (APK)

1. `_release/TabletEkran-0.3.apk` dosyasını tablete kopyalayın ve kurun; veya
   `adb install TabletEkran-0.3.apk` kullanın.
2. Uygulamayı açın. Ekranda 4 haneli eşleştirme kodu görünür.
3. Aynı yerel ağda olan Windows bilgisayarın IP'sini öğrenin.

`adb` ile kurulum örneği:

```text
adb -s <SERI_NO> install TabletEkran-0.3.apk
```

Daha önce farklı bir debug anahtarıyla kurulmuş bir sürüm varsa, imza
uyuşmazlığı nedeniyle önce eski uygulamayı kaldırmanız gerekebilir.

### Windows

1. `.NET 8 Desktop Runtime` kurulu olmalıdır.
2. `_release/TabletEkranGonderici-1.1.1.zip` arşivini bir klasöre çıkarın.
3. `TabletEkranGonderici.exe` dosyasını çalıştırın.
4. Tablet IP'sini, tabletteki 4 haneli kodu ve ikinci ekran olarak kullanmak
   istediğiniz monitörü seçin; **Başlat**'a basın.
5. İlk çalıştırmada Windows güvenlik duvarı sorarsa, yalnızca yerel ağ için
   izin verin.

Windows tarafında örnek bir yerel IP `192.168.1.x` biçimindedir; kendi ağınıza
göre girin.

## Sanal ikinci monitör (isteğe bağlı)

Bilgisayarda fiziksel ikinci monitör yoksa, Windows'ta sanal bir monitör
oluşturmak için isteğe bağlı olarak Virtual Display Driver kullanılabilir:

<https://github.com/VirtualDrivers/Virtual-Display-Driver>

Bu sürücü MIT lisanslıdır ve bu depoya **dahil edilmemiştir**; kurulum ve
lisans koşulları kendi projesine aittir. Monitörün konumu ve yerleşimi Windows
ekran ayarlarından kullanıcı tarafından yapılır. Uygulamadaki **Windows ekran
ayarlarını aç** düğmesi yalnızca ayarlar sayfasını açar; konumu değiştirmez.

## Doğrulananlar

- Vestel P10 (`VP10`), Android 4.1.1, API 16 tablete kurulum ve çalışma.
- Windows 10 (22H2) üzerinde yayın; yaklaşık 12 kare/sn.
- 4 haneli kod ile eşleşme ve `/bilgi` yanıtı.
- 5 saniye kare gelmezse saat ekranına dönüş.
- Açılışta otomatik başlama (test edilen tablette).
- Seçilen sanal monitörün karelerinin tablete ulaşması.

## Doğrulanmayanlar

- Dört yönün tamamında döndürme; yalnızca test edilen tablette gözlenen
  yönler doğrulandı.
- Her monitör konumunda fare kenarından tablete geçiş; konum Windows ekran
  ayarlarından kullanıcı tarafından ayarlanır.
- Windows 10 dışındaki Windows sürümleri.
- Farklı Android sürümleri ve farklı tablet modelleri.

## Bilinen sınırlar

- Dokunmatik ve fare olayları tablete geri gönderilmez; tablet yalnızca
  görüntü alır.
- Ses aktarımı yoktur.
- Video akıcı değildir; JPEG kareler ve yaklaşık 12 kare/sn hedeflenir.
- Şifreleme yoktur; yalnızca güvenilen yerel ağda kullanılmalıdır.
- Eşleştirme 4 haneli kod iledir; kod tahmin edilebilir, bu nedenle uygulama
  internete açılmamalıdır.

## Derleme

### Windows

```text
dotnet build windows/TabletEkranGonderici.csproj -c Release
```

Çıktı `windows/bin/Release/net8.0-windows/` altındadır. `.NET 8 SDK` gerekir.

### Android

Gereksinimler: JDK 17+ ve Android SDK. Ortam değişkenlerini ayarlayın:

```text
JAVA_HOME=<JDK dizini>
ANDROID_HOME=<Android SDK dizini>
```

Sonra:

```text
powershell -ExecutionPolicy Bypass -File android/build.ps1
```

Betik, imzalama için her çalışmada `android/build/` altında geçici bir debug
anahtarı üretir; anahtar ve parolası depoya eklenmez, işlem sonunda silinir.
APK çıktısı `android/build/TabletEkran-0.3.apk` olur.

## Lisans

MIT. Ayrıntılar için [LICENSE](LICENSE) dosyasına bakın.
Telif: 2026 avsessisozx.

## English summary

TabletEkran turns an old Android tablet into a simple wireless second screen
for a Windows PC on the same local network. The tablet runs a small HTTP server
on port 8765 and displays JPEG frames sent by the Windows sender after a
four-digit pairing code. It is not compatible with Chromecast, Miracast or
Google Cast; it uses its own minimal protocol. Audio and touch/mouse input are
not supported. The project was tested only with a Vestel P10 tablet on Android
4.1.1 and Windows 10; four-way rotation and mouse edge crossing were not fully
verified. The optional Virtual Display Driver is a separate MIT-licensed
project and is not included. Licensed under MIT, 2026 avsessisozx.
