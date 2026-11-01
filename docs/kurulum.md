# Kurulum

## Gereksinimler

- Windows 10 veya üzeri (yalnızca Windows 10 22H2 üzerinde test edildi).
- `.NET 8 Desktop Runtime` (Windows göndericisi için).
- Android 4.1+ tablet (yalnızca Vestel P10 / Android 4.1.1 üzerinde test edildi).
- Tablet ve bilgisayar aynı yerel ağda olmalı.

## Tablet

1. `_release/TabletEkran-0.3.apk` dosyasını tablete kopyalayın.
2. Dosyayı açıp kurun; gerekirse "bilinmeyen kaynaklar" iznini verin.
3. Uygulamayı açın. Ekranda 4 haneli eşleştirme kodu görünür.
4. Uygulama açılışta otomatik başlayacak şekilde yapılandırılmıştır.

`adb` ile:

```text
adb -s <SERI_NO> install TabletEkran-0.3.apk
adb -s <SERI_NO> shell am start -n com.tabletekran/.MainActivity
```

Daha önce farklı bir debug anahtarıyla kurulmuş sürüm varsa, imza uyuşmazlığı
nedeniyle önce eski uygulamayı kaldırmanız gerekebilir.

## Windows

1. `.NET 8 Desktop Runtime` kurun.
2. `_release/TabletEkranGonderici-1.1.1.zip` dosyasını bir klasöre çıkarın.
3. `TabletEkranGonderici.exe` dosyasını çalıştırın.
4. Tablet IP'sini girin (örnek: `192.168.1.x`), tabletteki 4 haneli kodu ve
   monitörü seçin.
5. **Başlat**'a basın. İlk çalıştırmada güvenlik duvarı sorarsa yalnızca yerel
   ağ için izin verin.

## Sanal monitör (isteğe bağlı)

Fiziksel ikinci monitör yoksa, Windows'ta sanal monitör için isteğe bağlı
olarak Virtual Display Driver kullanılabilir:

<https://github.com/VirtualDrivers/Virtual-Display-Driver>

Sürücü MIT lisanslıdır ve bu depoya dahil değildir. Monitörün konumu Windows
ekran ayarlarından kullanıcı tarafından yapılır; uygulama ekran düzenini
değiştirmez.

## Sorun giderme

- Bağlantı kurulamıyorsa tablet ve bilgisayarın aynı ağda olduğunu kontrol edin.
- Windows güvenlik duvarında `TabletEkranGonderici.exe` için yerel ağ izni
  verin.
- Kod yanlışsa tabletteki güncel kodu kullanın.
- Kare gelmezse tablet 5 saniye sonra saat ekranına döner; yeniden başlatın.
