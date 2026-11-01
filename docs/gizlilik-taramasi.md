# Gizlilik Taraması

Tarih: 2026-11-01

Kapsam: Yayın ağacındaki tüm metin dosyaları (`.md`, `.txt`, `.cs`, `.java`,
`.xml`, `.ps1`, `.json`, `.csproj`, `.manifest`, `.gitignore`). `_release`
içindeki derlenmiş APK/zip ikili dosyaları tarama dışıdır; bu dosyalar yukarıdaki
temiz kaynaklardan üretilmiştir.

## Sonuç

- Kullanıcı adı: **0** eşleşme.
- Proje kök yolu: **0** eşleşme.
- Windows kullanıcı yolu: **0** eşleşme.
- E-posta adresi: **0** eşleşme.
- Adb seri numarası: **0** eşleşme; örneklerde `<SERI_NO>` kullanılır.
- Gerçek IP adresi: **0** eşleşme; yalnızca `192.168.1.x` yer tutucuları
  (README.md:48, docs/kurulum.md:29 ve `_durum/DURUM.md` içindeki örnekler).
- Sabit parola / anahtar parolası: **0** eşleşme. `android/build.ps1` geçici
  anahtarı rastgele parola ile üretir; parola dosyaya yazılmaz.
- Sabit token / API anahtarı / secret: **0** eşleşme. Protokoldeki oturum
  tokenı çalışma zamanında üretilir; kaynakta gömülü değer yoktur.

## Yapılan düzeltmeler

- `android/build.ps1`: sabit kullanıcı yolu kaldırıldı; `JAVA_HOME` ve
  `ANDROID_HOME` (veya `ANDROID_SDK_ROOT`) ortam değişkenleri kullanılıyor.
- İmzalama: her derlemede `android/build/` altında rastgele parolalı geçici bir
  debug anahtarı üretiliyor; anahtar ve parola depoya yazılmıyor, işlem sonunda
  siliniyor. Depoda `.keystore`, `.jks`, `.p12` yok.
- `_durum/DURUM.md`: durum dalı için kopya; gerçek kullanıcı yolu, IP, adb seri
  numarası ve uzun token değerleri yer tutuculara çevrildi. `_durum/` ana
  `.gitignore` içinde yok sayılır.
- `test/`, `bin/`, `obj/`, `build/`, `yedek-*`, `.eski`, loglar ve derlenmiş
  exe/apk yayın ağacından çıkarıldı; exe/apk yalnızca `_release/` altında.
- `README.md` ve `docs/` içinde gerçek IP, seri numarası, e-posta veya kişisel
  yol kullanılmadı.

## İnceleme notu

`_release/TabletEkran-0.3.apk` ve `_release/TabletEkranGonderici-1.1.1.zip`
ikili dosyalarıdır; metin taramasına girmez. Kaynak ağacında gizli anahtar,
parola, API anahtarı veya kişisel veri bulunmadı.
