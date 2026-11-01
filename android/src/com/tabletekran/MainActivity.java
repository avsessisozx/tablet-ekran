package com.tabletekran;

import android.app.Activity;
import android.content.Context;
import android.content.res.Configuration;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Color;
import android.graphics.Typeface;
import android.net.ConnectivityManager;
import android.net.NetworkInfo;
import android.os.Bundle;
import android.os.Handler;
import android.util.DisplayMetrics;
import android.util.Log;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.WindowManager;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;

import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.ServerSocket;
import java.net.Socket;
import java.net.URLDecoder;
import java.security.SecureRandom;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.HashMap;
import java.util.Locale;
import java.util.Map;
import java.util.Random;
import java.util.TimeZone;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;

/**
 * TabletEkran - Asama 1 + Asama 2 (ekran yansitma alicisi).
 *
 * Tam ekran dijital saat/tarih, kosede 4 haneli eslestirme kodu, 8765 portunda
 * yerel HTTP sunucusu. Asama 2: /esles, /kare, /bitir uclari; gelen JPEG kareleri
 * inBitmap ile yeniden kullanilan bitmap'lere cozup tam ekran gosterir.
 * Internet erisimi yok, AndroidX yok, minSdk 16.
 */
public class MainActivity extends Activity {

    private static final String ETIKET = "TabletEkran";
    private static final int PORT = 8765;

    /** /kare govdesi icin ust sinir: 2 MB. */
    private static final int AZAMI_GOVDE = 2 * 1024 * 1024;
    /** /esles govdesi kucuk bir JSON'dur. */
    private static final int AZAMI_ESLES_GOVDE = 64 * 1024;
    private static final long KARE_ZAMAN_ASIMI_MS = 5000L;
    private static final int AZAMI_YANLIS_DENEME = 5;
    private static final long KILIT_SURESI_MS = 60000L;
    private static final String JSON_TURU = "application/json; charset=utf-8";

    /** /bilgi yaniti; eslestirme kodu burada asla yer almaz. */
    private static final String BILGI_JSON =
            "{\"ad\":\"TabletEkran\",\"surum\":\"0.3\",\"kod_var\":true}";

    private TextView saatGorunumu;
    private TextView tarihGorunumu;
    private TextView kodGorunumu;
    private LinearLayout orta;
    private LinearLayout kodKutusu;
    private ImageView kareGorunumu;
    private TextView agUyarisi;
    private String eslesmeKodu;

    /** Token, kilit ve kare modu durumunu koruyan kilit. */
    private final Object durumKilidi = new Object();
    private String gecerliToken;
    private volatile boolean kareModu;
    private volatile long sonKareZamani;
    private volatile long sonGosterilenSeq = -1;
    private final Object kareIslemeKilidi = new Object();
    private volatile boolean kareAkisiAcik;
    private int yanlisDeneme;
    private long kilitBitis;

    /** Bitmap arabelleklerini ve ekranda hangisinin oldugunu koruyan kilit. */
    private final Object kareKilidi = new Object();
    private final Bitmap[] arabellekler = new Bitmap[2];
    private int aktifArabellek = -1;

    /**
     * Bu tabletin saat dilimi ayari Europe/Athens (+2) oldugu icin yerel saat
     * Turkiye saatinden 1 saat geride kaliyor. Cihaz ayarina dokunmadan gosterim
     * her zaman Turkiye'nin sabit UTC+03:00 diliminde yapilir.
     */
    private static final TimeZone TURKIYE_DILIMI = TimeZone.getTimeZone("GMT+03:00");

    private final Handler zamanlayici = new Handler();
    private final SimpleDateFormat saatBicimi = bicimOlustur("HH:mm:ss", Locale.US);
    private final SimpleDateFormat tarihBicimi =
            bicimOlustur("dd.MM.yyyy EEEE", new Locale("tr", "TR"));
    private final SecureRandom guvenliRastgele = new SecureRandom();

    private Sunucu sunucu;

    private static SimpleDateFormat bicimOlustur(String desen, Locale yerel) {
        SimpleDateFormat bicim = new SimpleDateFormat(desen, yerel);
        bicim.setTimeZone(TURKIYE_DILIMI);
        return bicim;
    }

    private final Runnable saatDongusu = new Runnable() {
        @Override
        public void run() {
            saatiGuncelle();
            long simdi = System.currentTimeMillis();
            // Bir sonraki tam saniyeye hizala; saniye atlamasi olmasin.
            zamanlayici.postDelayed(this, 1000L - (simdi % 1000L));
        }
    };

    /** Kare akisi 5 saniye durursa saat ekranina doner. */
    private final Runnable kareKontrolu = new Runnable() {
        @Override
        public void run() {
            long simdi = System.currentTimeMillis();
            if (kareModu && sonKareZamani > 0
                    && simdi - sonKareZamani > KARE_ZAMAN_ASIMI_MS) {
                saatEkraninaDon();
            }
            zamanlayici.postDelayed(this, 250);
        }
    };

    @Override
    protected void onCreate(Bundle durum) {
        super.onCreate(durum);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_FULLSCREEN
                | WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON
                | WindowManager.LayoutParams.FLAG_SHOW_WHEN_LOCKED
                | WindowManager.LayoutParams.FLAG_DISMISS_KEYGUARD
                | WindowManager.LayoutParams.FLAG_TURN_SCREEN_ON);

        if (durum != null && durum.getString("kod") != null) {
            eslesmeKodu = durum.getString("kod");
        } else {
            eslesmeKodu = String.format(Locale.US, "%04d", 1000 + new Random().nextInt(9000));
        }

        arayuzuKur();
        saatiGuncelle();

        sunucu = new Sunucu();
        sunucu.baslat();
    }

    @Override
    protected void onResume() {
        super.onResume();
        tamEkran();
        zamanlayici.removeCallbacks(saatDongusu);
        zamanlayici.post(saatDongusu);
        zamanlayici.removeCallbacks(kareKontrolu);
        zamanlayici.post(kareKontrolu);
    }

    @Override
    public void onWindowFocusChanged(boolean odakta) {
        super.onWindowFocusChanged(odakta);
        if (odakta) {
            tamEkran();
        }
    }

    /**
     * Donme sirasinda activity yeniden olusturulmaz (manifest configChanges);
     * yalnizca yazilar yeni ekran genisligine gore yeniden boyutlandirilir.
     * Boylece eslesme kodu, token, HTTP sunucusu ve son kare korunur.
     */
    @Override
    public void onConfigurationChanged(Configuration yeniYapilandirma) {
        super.onConfigurationChanged(yeniYapilandirma);
        tamEkran();
        yazilariYenidenBoyutlandir();
        agDurumunuGuncelle();
        if (kareGorunumu != null) {
            kareGorunumu.requestLayout();
        }
    }

    @Override
    protected void onSaveInstanceState(Bundle durum) {
        super.onSaveInstanceState(durum);
        durum.putString("kod", eslesmeKodu);
    }

    @Override
    protected void onDestroy() {
        zamanlayici.removeCallbacks(saatDongusu);
        zamanlayici.removeCallbacks(kareKontrolu);
        if (sunucu != null) {
            sunucu.durdur();
        }
        // Bitmap'ler burada geri donusturulmez: sunucu is parcacigi hala bir
        // kareyi isliyor olabilir; GC'ye birakilir.
        super.onDestroy();
    }

    private void tamEkran() {
        View kok = getWindow().getDecorView();
        kok.setSystemUiVisibility(View.SYSTEM_UI_FLAG_FULLSCREEN
                | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION);
    }

    private int dp(int deger) {
        return (int) (deger * getResources().getDisplayMetrics().density + 0.5f);
    }

    private void arayuzuKur() {
        FrameLayout kok = new FrameLayout(this);
        kok.setBackgroundColor(Color.BLACK);
        kok.setKeepScreenOn(true);

        // Ortada saat ve tarih.
        orta = new LinearLayout(this);
        orta.setOrientation(LinearLayout.VERTICAL);
        orta.setGravity(Gravity.CENTER);

        saatGorunumu = new TextView(this);
        saatGorunumu.setTextColor(Color.WHITE);
        saatGorunumu.setTextSize(TypedValue.COMPLEX_UNIT_SP, 120);
        saatGorunumu.setTypeface(Typeface.MONOSPACE, Typeface.BOLD);
        saatGorunumu.setIncludeFontPadding(false);
        saatGorunumu.setGravity(Gravity.CENTER);
        orta.addView(saatGorunumu);

        tarihGorunumu = new TextView(this);
        tarihGorunumu.setTextColor(Color.rgb(176, 190, 197));
        tarihGorunumu.setTextSize(TypedValue.COMPLEX_UNIT_SP, 34);
        tarihGorunumu.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams tarihParametreleri = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.WRAP_CONTENT,
                LinearLayout.LayoutParams.WRAP_CONTENT);
        tarihParametreleri.topMargin = dp(18);
        orta.addView(tarihGorunumu, tarihParametreleri);

        FrameLayout.LayoutParams ortaParametreleri = new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT);
        ortaParametreleri.gravity = Gravity.CENTER;
        kok.addView(orta, ortaParametreleri);

        // Sag ust kose: eslestirme kodu.
        kodKutusu = new LinearLayout(this);
        kodKutusu.setOrientation(LinearLayout.VERTICAL);
        kodKutusu.setGravity(Gravity.RIGHT);

        TextView kodBasligi = new TextView(this);
        kodBasligi.setText("EŞLEŞTİRME KODU");
        kodBasligi.setTextColor(Color.rgb(144, 164, 174));
        kodBasligi.setTextSize(TypedValue.COMPLEX_UNIT_SP, 16);
        kodBasligi.setGravity(Gravity.RIGHT);
        kodKutusu.addView(kodBasligi);

        kodGorunumu = new TextView(this);
        kodGorunumu.setText(eslesmeKodu);
        kodGorunumu.setTextColor(Color.rgb(255, 193, 7));
        kodGorunumu.setTextSize(TypedValue.COMPLEX_UNIT_SP, 56);
        kodGorunumu.setTypeface(Typeface.MONOSPACE, Typeface.BOLD);
        kodGorunumu.setGravity(Gravity.RIGHT);
        kodKutusu.addView(kodGorunumu);

        FrameLayout.LayoutParams kodParametreleri = new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.WRAP_CONTENT,
                FrameLayout.LayoutParams.WRAP_CONTENT);
        kodParametreleri.gravity = Gravity.TOP | Gravity.RIGHT;
        kodParametreleri.topMargin = dp(20);
        kodParametreleri.rightMargin = dp(28);
        kok.addView(kodKutusu, kodParametreleri);

        // Gelen kareyi gosteren katman (en ustte).
        kareGorunumu = new ImageView(this);
        kareGorunumu.setScaleType(ImageView.ScaleType.FIT_CENTER);
        kareGorunumu.setBackgroundColor(Color.BLACK);
        kareGorunumu.setVisibility(View.GONE);
        FrameLayout.LayoutParams kareParametreleri = new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT);
        kok.addView(kareGorunumu, kareParametreleri);

        // Ag yoksa kisa uyari; kare modunda gizlenir.
        agUyarisi = new TextView(this);
        agUyarisi.setText("Ağ bekleniyor...");
        agUyarisi.setTextColor(Color.rgb(255, 193, 7));
        agUyarisi.setTextSize(TypedValue.COMPLEX_UNIT_SP, 18);
        agUyarisi.setGravity(Gravity.CENTER);
        agUyarisi.setVisibility(View.GONE);
        FrameLayout.LayoutParams agParametreleri = new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.WRAP_CONTENT,
                FrameLayout.LayoutParams.WRAP_CONTENT);
        agParametreleri.gravity = Gravity.BOTTOM | Gravity.CENTER_HORIZONTAL;
        agParametreleri.bottomMargin = dp(28);
        kok.addView(agUyarisi, agParametreleri);

        setContentView(kok);

        // Ilk yerlesimde de ekran genisligine gore olceklenir.
        yazilariYenidenBoyutlandir();
    }

    /**
     * Saat/tarih/kod yazilarini ekran genisligine gore olcekler. 120sp ust sinir
     * korunur; dar/dikey ekranlarda tasmayi onlemek icin kucultulur.
     */
    private void yazilariYenidenBoyutlandir() {
        DisplayMetrics metrikler = getResources().getDisplayMetrics();
        int genislik = metrikler.widthPixels > 0 ? metrikler.widthPixels : 1280;
        float yogunluk = metrikler.density > 0 ? metrikler.density : 1f;

        // "HH:mm:ss" 8 karakter monospace; karakter genisligi ~0.62 em.
        float saatPx = genislik * 0.80f / (8f * 0.62f);
        float ustSinir = 120f * yogunluk;
        float altSinir = 48f * yogunluk;
        if (saatPx > ustSinir) {
            saatPx = ustSinir;
        }
        if (saatPx < altSinir) {
            saatPx = altSinir;
        }
        float saatSp = saatPx / yogunluk;

        saatGorunumu.setTextSize(TypedValue.COMPLEX_UNIT_SP, saatSp);
        tarihGorunumu.setTextSize(TypedValue.COMPLEX_UNIT_SP,
                Math.max(20f, saatSp * 0.28f));
        kodGorunumu.setTextSize(TypedValue.COMPLEX_UNIT_SP,
                Math.max(28f, Math.min(56f, saatSp * 0.47f)));
    }

    private void saatiGuncelle() {
        Date simdi = new Date();
        saatGorunumu.setText(saatBicimi.format(simdi));
        tarihGorunumu.setText(tarihBicimi.format(simdi));
        agDurumunuGuncelle();
    }

    /** Wi-Fi/ag baglantisi yoksa alt ortada kisa uyari gosterir. */
    private void agDurumunuGuncelle() {
        if (agUyarisi == null) {
            return;
        }
        boolean bagli = false;
        try {
            ConnectivityManager yonetici = (ConnectivityManager)
                    getSystemService(Context.CONNECTIVITY_SERVICE);
            if (yonetici != null) {
                NetworkInfo bilgi = yonetici.getActiveNetworkInfo();
                bagli = bilgi != null && bilgi.isConnected();
            }
        } catch (Exception hata) {
            // Ag servisi yoksa/okunamazsa uyari gosterilir; cokme olmaz.
            bagli = false;
        }
        if (kareModu) {
            agUyarisi.setVisibility(View.GONE);
        } else {
            agUyarisi.setVisibility(bagli ? View.GONE : View.VISIBLE);
        }
    }

    /** Kareyi tam ekran gosterir; UI islemi bitene kadar bekler. Cizim suresini ms doner. */
    private long kareGoster(final Bitmap kare) {
        long basla = System.nanoTime();
        sonKareZamani = System.currentTimeMillis();
        kareModu = true;
        final CountDownLatch bitti = new CountDownLatch(1);
        runOnUiThread(new Runnable() {
            @Override
            public void run() {
                try {
                    kareGorunumu.setImageBitmap(kare);
                    kareGorunumu.setVisibility(View.VISIBLE);
                    orta.setVisibility(View.GONE);
                    kodKutusu.setVisibility(View.GONE);
                } finally {
                    bitti.countDown();
                }
            }
        });
        try {
            bitti.await(2, TimeUnit.SECONDS);
        } catch (InterruptedException hata) {
            Thread.currentThread().interrupt();
        }
        return (System.nanoTime() - basla) / 1000000L;
    }

    /** Saat/tarih ekranina doner. Yalnizca UI thread'inden cagrilmalidir. */
    private void saatEkraninaDon() {
        kareModu = false;
        sonKareZamani = 0;
        kareGorunumu.setImageDrawable(null);
        kareGorunumu.setVisibility(View.GONE);
        orta.setVisibility(View.VISIBLE);
        kodKutusu.setVisibility(View.VISIBLE);
        synchronized (kareKilidi) {
            aktifArabellek = -1;
        }
    }

    /**
     * JPEG baytlarini onceki kare bitmap'ini (inBitmap) yeniden kullanarak cozer.
     * Basarisizlikta null doner.
     */
    private Bitmap kareCoz(byte[] veri) {
        BitmapFactory.Options olcum = new BitmapFactory.Options();
        olcum.inJustDecodeBounds = true;
        BitmapFactory.decodeByteArray(veri, 0, veri.length, olcum);
        if (olcum.outWidth <= 0 || olcum.outHeight <= 0) {
            return null;
        }

        synchronized (kareKilidi) {
            int hedef = (aktifArabellek == 0) ? 1 : 0;
            Bitmap aday = arabellekler[hedef];

            BitmapFactory.Options secenek = new BitmapFactory.Options();
            secenek.inPreferredConfig = Bitmap.Config.RGB_565;
            secenek.inMutable = true;
            secenek.inPreferQualityOverSpeed = false;
            if (aday != null && !aday.isRecycled()
                    && aday.getWidth() == olcum.outWidth
                    && aday.getHeight() == olcum.outHeight) {
                secenek.inBitmap = aday;
            }

            Bitmap cozulen;
            try {
                cozulen = BitmapFactory.decodeByteArray(veri, 0, veri.length, secenek);
            } catch (IllegalArgumentException hata) {
                // inBitmap uyumsuzsa temiz bir cozumlemeye dus.
                secenek.inBitmap = null;
                cozulen = BitmapFactory.decodeByteArray(veri, 0, veri.length, secenek);
            } catch (OutOfMemoryError hata) {
                Log.e(ETIKET, "Kare cozme bellegi yetersiz: " + hata);
                if (aday != null) {
                    aday.recycle();
                    arabellekler[hedef] = null;
                }
                return null;
            }

            if (cozulen == null) {
                if (aday != null) {
                    aday.recycle();
                    arabellekler[hedef] = null;
                }
                return null;
            }
            if (aday != null && aday != cozulen) {
                aday.recycle();
            }
            arabellekler[hedef] = cozulen;
            aktifArabellek = hedef;
            return cozulen;
        }
    }

    private String tokenUret() {
        byte[] bayt = new byte[16]; // 32 karakter hex
        guvenliRastgele.nextBytes(bayt);
        StringBuilder sonuc = new StringBuilder(32);
        for (int i = 0; i < bayt.length; i++) {
            int deger = bayt[i] & 0xFF;
            sonuc.append(Character.forDigit(deger >> 4, 16));
            sonuc.append(Character.forDigit(deger & 0x0F, 16));
        }
        return sonuc.toString();
    }

    private boolean tokenGecerli(String token) {
        synchronized (durumKilidi) {
            return token != null && gecerliToken != null && gecerliToken.equals(token);
        }
    }

    /**
     * 8765 portunda dinleyen minimal HTTP sunucusu.
     * GET /bilgi; POST /esles, /kare, /bitir.
     */
    private final class Sunucu implements Runnable {

        private volatile boolean calisiyor = true;
        private volatile ServerSocket sunucuSoketi;
        private Thread isParcacigi;

        void baslat() {
            isParcacigi = new Thread(this, "TabletEkran-HTTP");
            isParcacigi.setDaemon(true);
            isParcacigi.start();
        }

        @Override
        public void run() {
            try {
                sunucuSoketi = new ServerSocket(PORT);
                Log.i(ETIKET, "HTTP sunucu " + PORT + " portunda dinliyor");
                while (calisiyor) {
                    final Socket istemci = sunucuSoketi.accept();
                    Thread isci = new Thread(new Runnable() {
                        @Override
                        public void run() {
                            try {
                                istegiYanitla(istemci);
                            } catch (IOException hata) {
                                if (calisiyor) {
                                    Log.w(ETIKET, "Baglanti hatasi: " + hata);
                                }
                            } catch (RuntimeException hata) {
                                Log.e(ETIKET, "Baglanti isleme hatasi: " + hata);
                            } finally {
                                try {
                                    istemci.close();
                                } catch (IOException yok) {
                                    // yoksay
                                }
                            }
                        }
                    }, "TabletEkran-HTTP-Conn");
                    isci.setDaemon(true);
                    isci.start();
                }
            } catch (IOException hata) {
                if (calisiyor) {
                    Log.e(ETIKET, "HTTP sunucu baslatilamadi: " + hata);
                }
            }
        }

        private void istegiYanitla(Socket istemci) throws IOException {
            istemci.setSoTimeout(15000);
            istemci.setTcpNoDelay(true);
            InputStream giris = istemci.getInputStream();
            OutputStream cikis = istemci.getOutputStream();

            while (calisiyor) {
                String istekSatiri = okuSatir(giris);
                if (istekSatiri == null) {
                    return;
                }

                String[] parcalar = istekSatiri.split(" ");
                String yontem = parcalar.length > 0 ? parcalar[0] : "";
                String hedef = parcalar.length > 1 ? parcalar[1] : "/";
                String yol = hedef;
                String sorgu = null;
                int soruIsareti = yol.indexOf('?');
                if (soruIsareti >= 0) {
                    sorgu = yol.substring(soruIsareti + 1);
                    yol = yol.substring(0, soruIsareti);
                }

                Map<String, String> basliklar = new HashMap<String, String>();
                int satirSayisi = 0;
                String baslik;
                while ((baslik = okuSatir(giris)) != null
                        && baslik.length() > 0 && satirSayisi++ < 100) {
                    int ikiNokta = baslik.indexOf(':');
                    if (ikiNokta > 0) {
                        basliklar.put(baslik.substring(0, ikiNokta).trim().toLowerCase(Locale.US),
                                baslik.substring(ikiNokta + 1).trim());
                    }
                }

                int uzunluk = -1;
                String uzunlukBasligi = basliklar.get("content-length");
                if (uzunlukBasligi != null) {
                    try {
                        uzunluk = Integer.parseInt(uzunlukBasligi);
                    } catch (NumberFormatException yok) {
                        uzunluk = -1;
                    }
                }

                boolean istemciKapat = "close".equalsIgnoreCase(basliklar.get("connection"));

                if ("GET".equals(yontem)) {
                    if ("/bilgi".equals(yol)) {
                        yanit(cikis, 200, "OK", JSON_TURU, BILGI_JSON.getBytes("UTF-8"), null);
                    } else if ("/".equals(yol) || "/index.html".equals(yol)) {
                        yanit(cikis, 200, "OK", "text/plain; charset=utf-8",
                                "TabletEkran".getBytes("UTF-8"), null);
                    } else if ("/esles".equals(yol) || "/kare".equals(yol) || "/bitir".equals(yol)) {
                        yanit(cikis, 405, "Method Not Allowed", JSON_TURU,
                                "{\"hata\":\"post_gerekli\"}".getBytes("UTF-8"),
                                "Allow: POST\r\n");
                    } else {
                        yanit(cikis, 404, "Not Found", JSON_TURU,
                                "{\"hata\":\"bulunamadi\"}".getBytes("UTF-8"), null);
                    }
                    return;
                }

                if (!"POST".equals(yontem)) {
                    yanit(cikis, 405, "Method Not Allowed", JSON_TURU,
                            "{\"hata\":\"desteklenmeyen_yontem\"}".getBytes("UTF-8"),
                            "Allow: GET, POST\r\n");
                    return;
                }

                if ("/kare".equals(yol)) {
                    boolean devam = kare(giris, cikis, sorgu, uzunluk, !istemciKapat);
                    if (!devam) {
                        return;
                    }
                    // Ayni baglantida bir sonraki kare istegini oku.
                    continue;
                }

                if ("/esles".equals(yol)) {
                    esles(giris, cikis, uzunluk);
                } else if ("/bitir".equals(yol)) {
                    bitir(cikis, sorgu);
                } else {
                    yanit(cikis, 404, "Not Found", JSON_TURU,
                            "{\"hata\":\"bulunamadi\"}".getBytes("UTF-8"), null);
                }
                return;
            }
        }

        private void esles(InputStream giris, OutputStream cikis, int uzunluk)
                throws IOException {
            if (uzunluk > AZAMI_ESLES_GOVDE) {
                yanit(cikis, 413, "Payload Too Large", JSON_TURU,
                        "{\"hata\":\"govde_cok_buyuk\"}".getBytes("UTF-8"), null);
                return;
            }
            byte[] govde = govdeOku(giris, uzunluk, AZAMI_ESLES_GOVDE);
            if (govde == null) {
                yanit(cikis, 413, "Payload Too Large", JSON_TURU,
                        "{\"hata\":\"govde_cok_buyuk\"}".getBytes("UTF-8"), null);
                return;
            }

            String kod;
            try {
                JSONObject istek = new JSONObject(new String(govde, "UTF-8"));
                kod = istek.optString("kod", "");
            } catch (Exception hata) {
                yanit(cikis, 400, "Bad Request", JSON_TURU,
                        "{\"ok\":false,\"hata\":\"gecersiz_json\"}".getBytes("UTF-8"), null);
                return;
            }

            String token = null;
            boolean kilitli = false;
            int kalanDeneme = 0;
            long simdi = System.currentTimeMillis();

            synchronized (durumKilidi) {
                if (kilitBitis > simdi) {
                    kilitli = true;
                } else {
                    if (kilitBitis != 0) {
                        kilitBitis = 0;
                        yanlisDeneme = 0;
                    }
                    if (kod.equals(eslesmeKodu)) {
                        yanlisDeneme = 0;
                        gecerliToken = tokenUret();
                        token = gecerliToken;
                        kareAkisiAcik = true;
                        sonGosterilenSeq = -1;
                        kareModu = false;
                        sonKareZamani = 0;
                    } else {
                        yanlisDeneme++;
                        if (yanlisDeneme >= AZAMI_YANLIS_DENEME) {
                            yanlisDeneme = 0;
                            kilitBitis = simdi + KILIT_SURESI_MS;
                            kilitli = true;
                        } else {
                            kalanDeneme = AZAMI_YANLIS_DENEME - yanlisDeneme;
                        }
                    }
                }
            }

            if (token != null) {
                runOnUiThread(new Runnable() {
                    @Override
                    public void run() {
                        saatEkraninaDon();
                    }
                });
                String cevap = "{\"ok\":true,\"token\":\"" + token + "\"}";
                yanit(cikis, 200, "OK", JSON_TURU, cevap.getBytes("UTF-8"), null);
            } else if (kilitli) {
                long kalanSn = Math.max(1L, (kilitBitis - System.currentTimeMillis() + 999L) / 1000L);
                String cevap = "{\"ok\":false,\"kilit\":true,\"kalan_sn\":" + kalanSn + "}";
                yanit(cikis, 403, "Forbidden", JSON_TURU, cevap.getBytes("UTF-8"),
                        "Retry-After: 60\r\n");
            } else {
                String cevap = "{\"ok\":false,\"kalan_deneme\":" + kalanDeneme + "}";
                yanit(cikis, 403, "Forbidden", JSON_TURU, cevap.getBytes("UTF-8"), null);
            }
        }

        private boolean kare(InputStream giris, OutputStream cikis, String sorgu,
                int uzunluk, boolean keepAlive) throws IOException {
            if (uzunluk > AZAMI_GOVDE) {
                yanit(cikis, 413, "Payload Too Large", JSON_TURU,
                        "{\"hata\":\"govde_cok_buyuk\"}".getBytes("UTF-8"), null);
                return false;
            }
            byte[] govde = govdeOku(giris, uzunluk, AZAMI_GOVDE);
            if (govde == null) {
                yanit(cikis, 413, "Payload Too Large", JSON_TURU,
                        "{\"hata\":\"govde_cok_buyuk\"}".getBytes("UTF-8"), null);
                return false;
            }

            String token = sorguDegeri(sorgu, "token");
            if (!tokenGecerli(token)) {
                yanit(cikis, 401, "Unauthorized", JSON_TURU,
                        "{\"ok\":false,\"hata\":\"gecersiz_token\"}".getBytes("UTF-8"), null);
                return false;
            }
            if (govde.length == 0) {
                yanit(cikis, 400, "Bad Request", JSON_TURU,
                        "{\"ok\":false,\"hata\":\"bos_govde\"}".getBytes("UTF-8"), null);
                return false;
            }

            long seq = -1;
            String seqMetni = sorguDegeri(sorgu, "seq");
            if (seqMetni != null) {
                try {
                    seq = Long.parseLong(seqMetni);
                } catch (NumberFormatException yok) {
                    seq = -1;
                }
            }

            synchronized (kareIslemeKilidi) {
                if (!kareAkisiAcik) {
                    String ekBasliklar = "X-Atlandi: 1\r\nX-Seq: " + seq
                            + "\r\nX-Decode-Ms: 0\r\nX-Draw-Ms: 0\r\nX-Server-Ms: 0\r\n";
                    yanit(cikis, 200, "OK", JSON_TURU,
                            "{\"ok\":true,\"atlandi\":true}".getBytes("UTF-8"),
                            ekBasliklar, keepAlive);
                    return keepAlive;
                }
                if (!tokenGecerli(token)) {
                    yanit(cikis, 401, "Unauthorized", JSON_TURU,
                            "{\"ok\":false,\"hata\":\"gecersiz_token\"}".getBytes("UTF-8"), null);
                    return false;
                }

                // Daha yeni bir kare zaten gosterildiyse eskisini cozme/cizme.
                if (seq > 0 && seq <= sonGosterilenSeq) {
                    String ekBasliklar = "X-Atlandi: 1\r\nX-Seq: " + seq
                            + "\r\nX-Decode-Ms: 0\r\nX-Draw-Ms: 0\r\nX-Server-Ms: 0\r\n";
                    yanit(cikis, 200, "OK", JSON_TURU,
                            "{\"ok\":true,\"atlandi\":true}".getBytes("UTF-8"),
                            ekBasliklar, keepAlive);
                    return keepAlive;
                }

                long sunucuBasla = System.nanoTime();
                long cozBasla = System.nanoTime();
                Bitmap kare = kareCoz(govde);
                long cozMs = (System.nanoTime() - cozBasla) / 1000000L;
                if (kare == null) {
                    yanit(cikis, 400, "Bad Request", JSON_TURU,
                            "{\"ok\":false,\"hata\":\"gecersiz_jpeg\"}".getBytes("UTF-8"), null);
                    return false;
                }

                long cizMs = kareGoster(kare);
                if (seq > 0) {
                    sonGosterilenSeq = seq;
                }
                long sunucuMs = (System.nanoTime() - sunucuBasla) / 1000000L;
                String ekBasliklar = "X-Seq: " + seq
                        + "\r\nX-Decode-Ms: " + cozMs
                        + "\r\nX-Draw-Ms: " + cizMs
                        + "\r\nX-Server-Ms: " + sunucuMs + "\r\n";
                yanit(cikis, 200, "OK", JSON_TURU,
                        "{\"ok\":true}".getBytes("UTF-8"), ekBasliklar, keepAlive);
                return keepAlive;
            }
        }

        private void bitir(OutputStream cikis, String sorgu) throws IOException {
            String token = sorguDegeri(sorgu, "token");
            if (!tokenGecerli(token)) {
                yanit(cikis, 401, "Unauthorized", JSON_TURU,
                        "{\"ok\":false,\"hata\":\"gecersiz_token\"}".getBytes("UTF-8"), null);
                return;
            }
            synchronized (kareIslemeKilidi) {
                kareAkisiAcik = false;
                kareModu = false;
                sonKareZamani = 0;
            }
            runOnUiThread(new Runnable() {
                @Override
                public void run() {
                    saatEkraninaDon();
                }
            });
            yanit(cikis, 200, "OK", JSON_TURU,
                    "{\"ok\":true}".getBytes("UTF-8"), null);
        }

        private byte[] govdeOku(InputStream giris, int uzunluk, int sinir)
                throws IOException {
            ByteArrayOutputStream cikti = new ByteArrayOutputStream(
                    uzunluk > 0 ? Math.min(uzunluk, 65536) : 8192);
            byte[] tampon = new byte[8192];

            if (uzunluk >= 0) {
                int kalan = uzunluk;
                while (kalan > 0) {
                    int okunan = giris.read(tampon, 0, Math.min(tampon.length, kalan));
                    if (okunan < 0) {
                        break;
                    }
                    cikti.write(tampon, 0, okunan);
                    kalan -= okunan;
                }
                return cikti.toByteArray();
            }

            int toplam = 0;
            int okunan;
            while ((okunan = giris.read(tampon)) >= 0) {
                toplam += okunan;
                if (toplam > sinir) {
                    return null;
                }
                cikti.write(tampon, 0, okunan);
            }
            return cikti.toByteArray();
        }

        private String okuSatir(InputStream giris) throws IOException {
            ByteArrayOutputStream satir = new ByteArrayOutputStream(128);
            int bayt;
            boolean veriVar = false;
            while ((bayt = giris.read()) >= 0) {
                veriVar = true;
                if (bayt == '\n') {
                    break;
                }
                if (bayt != '\r') {
                    satir.write(bayt);
                }
                if (satir.size() > 8192) {
                    break;
                }
            }
            if (!veriVar && satir.size() == 0) {
                return null;
            }
            return satir.toString("UTF-8");
        }

        private String sorguDegeri(String sorgu, String anahtar) {
            if (sorgu == null || sorgu.length() == 0) {
                return null;
            }
            String[] parcalar = sorgu.split("&");
            for (int i = 0; i < parcalar.length; i++) {
                int esit = parcalar[i].indexOf('=');
                String ad = esit >= 0 ? parcalar[i].substring(0, esit) : parcalar[i];
                if (anahtar.equals(ad)) {
                    String deger = esit >= 0 ? parcalar[i].substring(esit + 1) : "";
                    try {
                        return URLDecoder.decode(deger, "UTF-8");
                    } catch (Exception hata) {
                        return deger;
                    }
                }
            }
            return null;
        }

        private void yanit(OutputStream cikis, int kod, String durum, String icerikTuru,
                byte[] govde, String ekBasliklar) throws IOException {
            yanit(cikis, kod, durum, icerikTuru, govde, ekBasliklar, false);
        }

        private void yanit(OutputStream cikis, int kod, String durum, String icerikTuru,
                byte[] govde, String ekBasliklar, boolean keepAlive) throws IOException {
            StringBuilder basliklar = new StringBuilder();
            basliklar.append("HTTP/1.1 ").append(kod).append(' ').append(durum).append("\r\n");
            basliklar.append("Content-Type: ").append(icerikTuru).append("\r\n");
            basliklar.append("Content-Length: ").append(govde.length).append("\r\n");
            basliklar.append("Cache-Control: no-store\r\n");
            if (keepAlive) {
                basliklar.append("Connection: keep-alive\r\n");
                basliklar.append("Keep-Alive: timeout=15\r\n");
            } else {
                basliklar.append("Connection: close\r\n");
            }
            if (ekBasliklar != null) {
                basliklar.append(ekBasliklar);
            }
            basliklar.append("\r\n");
            byte[] baslikBaytlari = basliklar.toString().getBytes("UTF-8");
            byte[] tamYanit = new byte[baslikBaytlari.length + govde.length];
            System.arraycopy(baslikBaytlari, 0, tamYanit, 0, baslikBaytlari.length);
            System.arraycopy(govde, 0, tamYanit, baslikBaytlari.length, govde.length);
            cikis.write(tamYanit);
            cikis.flush();
        }

        void durdur() {
            calisiyor = false;
            ServerSocket soket = sunucuSoketi;
            if (soket != null) {
                try {
                    soket.close();
                } catch (IOException yok) {
                    // yoksay
                }
            }
            if (isParcacigi != null) {
                isParcacigi.interrupt();
            }
        }
    }
}
