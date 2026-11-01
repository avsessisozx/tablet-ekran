using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TabletEkranGonderici
{
    /// <summary>
    /// Tablet ile konusup secilen monitorun karelerini gonderen istemci.
    /// Yalnizca yerlesik System.Drawing / System.Net.Http kullanir.
    /// </summary>
    internal sealed class Gonderici : IDisposable
    {
        private const int VarsayilanGenislik = 1024;
        private const int VarsayilanYukseklik = 640;
        private const int VarsayilanKalite = 60;
        private const int VarsayilanHedefFps = 15;

        private static readonly ImageCodecInfo JpegKodek = ImageCodecInfo
            .GetImageEncoders()
            .First(kodek => kodek.FormatID == ImageFormat.Jpeg.Guid);

        private readonly HttpClient _http;
        private readonly string _kok;
        private readonly string _monitorAdi;
        private readonly int _azamiGenislik;
        private readonly int _azamiYukseklik;
        private readonly long _jpegKalitesi;
        private readonly int _hedefFps;
        private readonly Action<string> _uyari;
        private bool _uyariVerildi;

        public string Token { get; private set; }

        public Gonderici(string ip, string monitorAdi, int azamiGenislik,
            int azamiYukseklik, int jpegKalitesi, Action<string> uyari)
        {
            _kok = "http://" + ip.Trim() + ":8765";
            _monitorAdi = monitorAdi;
            _azamiGenislik = azamiGenislik > 0 ? azamiGenislik : VarsayilanGenislik;
            _azamiYukseklik = azamiYukseklik > 0 ? azamiYukseklik : VarsayilanYukseklik;
            _jpegKalitesi = jpegKalitesi > 0 ? jpegKalitesi : VarsayilanKalite;
            _hedefFps = VarsayilanHedefFps;
            _uyari = uyari;

            var isleyici = new HttpClientHandler
            {
                UseProxy = false,
                MaxConnectionsPerServer = 4
            };
            _http = new HttpClient(isleyici) { Timeout = TimeSpan.FromSeconds(10) };
        }

        public async Task<string> EslesAsync(string kod, CancellationToken iptal)
        {
            string govde = "{\"kod\":\"" + Kacis(kod) + "\"}";
            Exception sonHata = null;

            for (int deneme = 1; deneme <= 3; deneme++)
            {
                iptal.ThrowIfCancellationRequested();
                try
                {
                    using var icerik = new StringContent(govde, Encoding.UTF8, "application/json");
                    using var yanit = await _http.PostAsync(_kok + "/esles", icerik, iptal)
                        .ConfigureAwait(false);
                    string metin = await yanit.Content.ReadAsStringAsync(iptal).ConfigureAwait(false);

                    if (yanit.StatusCode == HttpStatusCode.OK)
                    {
                        using var belge = JsonDocument.Parse(metin);
                        if (belge.RootElement.TryGetProperty("token", out JsonElement tokenOgrasi))
                        {
                            string token = tokenOgrasi.GetString();
                            if (!string.IsNullOrEmpty(token))
                            {
                                Token = token;
                                return token;
                            }
                        }
                        throw new GonderimHatasi("Eşleşme yanıtında geçerli token yok.");
                    }
                    if (yanit.StatusCode == HttpStatusCode.Forbidden)
                    {
                        throw new GonderimHatasi("Kod yanlış veya eşleşme kilitli. " + metin);
                    }
                    if ((int)yanit.StatusCode >= 500)
                    {
                        throw new HttpRequestException(
                            $"Sunucu hatası: HTTP {(int)yanit.StatusCode} {metin}");
                    }
                    throw new GonderimHatasi(
                        $"Eşleşme başarısız: HTTP {(int)yanit.StatusCode} {metin}");
                }
                catch (GonderimHatasi)
                {
                    throw;
                }
                catch (OperationCanceledException) when (iptal.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception hata) when (hata is HttpRequestException || hata is TaskCanceledException)
                {
                    sonHata = hata;
                    if (deneme < 3)
                    {
                        await Task.Delay(300, iptal).ConfigureAwait(false);
                    }
                }
            }

            throw new GonderimHatasi("Eşleşme bağlantı hatası: " +
                (sonHata == null ? "bilinmeyen" : sonHata.Message));
        }

        public async Task<KareGonderimSonucu> KareGonderAsync(KareVerisi kare, CancellationToken iptal)
        {
            Exception sonHata = null;

            for (int deneme = 1; deneme <= 3; deneme++)
            {
                iptal.ThrowIfCancellationRequested();
                var kronometre = Stopwatch.StartNew();
                try
                {
                    using var icerik = new ByteArrayContent(kare.Jpeg);
                    icerik.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                    string adres = _kok + "/kare?token=" + Uri.EscapeDataString(Token)
                        + "&seq=" + kare.Seq;
                    using var yanit = await _http.PostAsync(adres, icerik, iptal)
                        .ConfigureAwait(false);
                    string metin = await yanit.Content.ReadAsStringAsync(iptal).ConfigureAwait(false);

                    if (yanit.StatusCode == HttpStatusCode.OK)
                    {
                        long toplam = kronometre.ElapsedMilliseconds;
                        long coz = BaslikSayisi(yanit, "X-Decode-Ms");
                        long ciz = BaslikSayisi(yanit, "X-Draw-Ms");
                        long sunucu = BaslikSayisi(yanit, "X-Server-Ms");
                        bool atlandi = BaslikMetni(yanit, "X-Atlandi") == "1";
                        return new KareGonderimSonucu
                        {
                            ToplamMs = toplam,
                            CozMs = coz,
                            CizMs = ciz,
                            SunucuMs = sunucu,
                            AgMs = Math.Max(0, toplam - sunucu),
                            Atlandi = atlandi
                        };
                    }
                    if (yanit.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        throw new GonderimHatasi("Token geçersiz (401); akış durduruldu.");
                    }
                    if (yanit.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                    {
                        throw new GonderimHatasi("Kare 2 MB sınırını aştı (413).");
                    }
                    if ((int)yanit.StatusCode >= 500)
                    {
                        throw new HttpRequestException(
                            $"Sunucu hatası: HTTP {(int)yanit.StatusCode} {metin}");
                    }
                    throw new GonderimHatasi(
                        $"Kare gönderilemedi: HTTP {(int)yanit.StatusCode} {metin}");
                }
                catch (GonderimHatasi)
                {
                    throw;
                }
                catch (OperationCanceledException) when (iptal.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception hata) when (hata is HttpRequestException || hata is TaskCanceledException)
                {
                    sonHata = hata;
                    if (deneme < 3)
                    {
                        await Task.Delay(200, iptal).ConfigureAwait(false);
                    }
                }
            }

            throw new GonderimHatasi("Kare bağlantı hatası: " +
                (sonHata == null ? "bilinmeyen" : sonHata.Message));
        }

        /// <summary>
        /// Uretici (yakalama + JPEG) ve tuketici (HTTP) ayri is parcaciklarinda
        /// calisir; her zaman en yeni kare gonderilir, aradakiler atilir.
        /// </summary>
        public async Task<GonderimSonucu> GonderAsync(TimeSpan sure, Action<double> fpsBildir,
            CancellationToken iptal)
        {
            var kuyruk = new KareKuyrugu();
            var sayac = new GonderimSayaclari();
            var toplamKronometre = Stopwatch.StartNew();

            Task uretici = Task.Run(() => Uret(sure, kuyruk, sayac, iptal));
            Task tuketici = Task.Run(() => TuketAsync(kuyruk, sayac, fpsBildir, iptal));
            try
            {
                await Task.WhenAll(uretici, tuketici).ConfigureAwait(false);
            }
            finally
            {
                kuyruk.Bitti();
            }

            double sureSn = toplamKronometre.Elapsed.TotalSeconds;
            long islenen = sayac.Gonderilen + sayac.Atlanan;
            var sonuc = new GonderimSonucu
            {
                KareSayisi = sayac.Gonderilen,
                UretilenKare = sayac.Uretilen,
                AtlananKare = sayac.Atlanan,
                SureSn = sureSn,
                OrtalamaFps = sureSn > 0 ? sayac.Gonderilen / sureSn : 0,
                OrtalamaGecikmeMs = islenen > 0 ? (double)sayac.UctanUcaMs / islenen : 0,
                OrtalamaYakalamaMs = sayac.Uretilen > 0
                    ? (double)sayac.YakalamaMs / sayac.Uretilen : 0,
                OrtalamaSikistirmaMs = sayac.Uretilen > 0
                    ? (double)sayac.SikistirmaMs / sayac.Uretilen : 0,
                OrtalamaGonderimMs = islenen > 0
                    ? (double)sayac.GonderimMs / islenen : 0,
                OrtalamaCozMs = islenen > 0 ? (double)sayac.CozMs / islenen : 0,
                OrtalamaCizMs = islenen > 0 ? (double)sayac.CizMs / islenen : 0,
                OrtalamaSunucuMs = islenen > 0
                    ? (double)sayac.SunucuMs / islenen : 0,
                OrtalamaAgMs = islenen > 0 ? (double)sayac.AgMs / islenen : 0
            };
            if (fpsBildir != null)
            {
                fpsBildir(sonuc.OrtalamaFps);
            }
            return sonuc;
        }

        private void Uret(TimeSpan sure, KareKuyrugu kuyruk,
            GonderimSayaclari sayac, CancellationToken iptal)
        {
            var kronometre = Stopwatch.StartNew();
            long seq = 0;
            try
            {
                while (kronometre.Elapsed < sure)
                {
                    iptal.ThrowIfCancellationRequested();
                    long kareBaslangicMs = kronometre.ElapsedMilliseconds;
                    long uretimTicks = Stopwatch.GetTimestamp();
                    KareVerisi kare = KareYakala();
                    kare.Seq = ++seq;
                    kare.UretimTicks = uretimTicks;
                    kuyruk.Koy(kare);

                    Interlocked.Increment(ref sayac.Uretilen);
                    Interlocked.Add(ref sayac.YakalamaMs, kare.YakalamaMs);
                    Interlocked.Add(ref sayac.SikistirmaMs, kare.SikistirmaMs);

                    long hedef = kareBaslangicMs + (1000 / _hedefFps);
                    long gecen = kronometre.ElapsedMilliseconds;
                    if (gecen < hedef)
                    {
                        int bekle = (int)(hedef - gecen);
                        if (bekle > 0)
                        {
                            iptal.WaitHandle.WaitOne(bekle);
                        }
                    }
                }
            }
            finally
            {
                kuyruk.Bitti();
            }
        }

        private async Task TuketAsync(KareKuyrugu kuyruk, GonderimSayaclari sayac,
            Action<double> fpsBildir, CancellationToken iptal)
        {
            var fpsKronometre = Stopwatch.StartNew();
            long sonBildirimMs = 0;
            long sonBildirimKare = 0;

            while (true)
            {
                KareVerisi kare = kuyruk.Al(iptal);
                if (kare == null)
                {
                    break;
                }
                KareGonderimSonucu gonderim = await KareGonderAsync(kare, iptal)
                    .ConfigureAwait(false);
                long bitisTicks = Stopwatch.GetTimestamp();
                long uctanUcaMs = (long)((bitisTicks - kare.UretimTicks) * 1000.0
                    / Stopwatch.Frequency);

                Interlocked.Add(ref sayac.GonderimMs, gonderim.ToplamMs);
                Interlocked.Add(ref sayac.CozMs, gonderim.CozMs);
                Interlocked.Add(ref sayac.CizMs, gonderim.CizMs);
                Interlocked.Add(ref sayac.SunucuMs, gonderim.SunucuMs);
                Interlocked.Add(ref sayac.AgMs, gonderim.AgMs);
                Interlocked.Add(ref sayac.UctanUcaMs, uctanUcaMs);
                if (gonderim.Atlandi)
                {
                    Interlocked.Increment(ref sayac.Atlanan);
                }
                else
                {
                    Interlocked.Increment(ref sayac.Gonderilen);
                }

                long simdi = fpsKronometre.ElapsedMilliseconds;
                if (simdi - sonBildirimMs >= 1000)
                {
                    long gonderilen = Interlocked.Read(ref sayac.Gonderilen);
                    double fps = (gonderilen - sonBildirimKare) * 1000.0
                        / Math.Max(1, simdi - sonBildirimMs);
                    if (fpsBildir != null)
                    {
                        fpsBildir(fps);
                    }
                    sonBildirimMs = simdi;
                    sonBildirimKare = gonderilen;
                }
            }
        }

        private sealed class KareKuyrugu
        {
            private readonly object _kilit = new object();
            private KareVerisi _son;
            private bool _bitti;

            public void Koy(KareVerisi kare)
            {
                lock (_kilit)
                {
                    _son = kare;
                    Monitor.Pulse(_kilit);
                }
            }

            public void Bitti()
            {
                lock (_kilit)
                {
                    _bitti = true;
                    Monitor.PulseAll(_kilit);
                }
            }

            public KareVerisi Al(CancellationToken iptal)
            {
                lock (_kilit)
                {
                    while (_son == null && !_bitti)
                    {
                        if (iptal.IsCancellationRequested)
                        {
                            iptal.ThrowIfCancellationRequested();
                        }
                        Monitor.Wait(_kilit, 100);
                    }
                    if (_son != null)
                    {
                        KareVerisi kare = _son;
                        _son = null;
                        return kare;
                    }
                    return null;
                }
            }
        }

        private sealed class GonderimSayaclari
        {
            public long Uretilen;
            public long Gonderilen;
            public long Atlanan;
            public long YakalamaMs;
            public long SikistirmaMs;
            public long GonderimMs;
            public long CozMs;
            public long CizMs;
            public long SunucuMs;
            public long AgMs;
            public long UctanUcaMs;
        }

        public async Task BitirAsync(CancellationToken iptal)
        {
            if (string.IsNullOrEmpty(Token))
            {
                return;
            }
            try
            {
                using var icerik = new ByteArrayContent(Array.Empty<byte>());
                using var yanit = await _http.PostAsync(
                        _kok + "/bitir?token=" + Uri.EscapeDataString(Token), icerik, iptal)
                    .ConfigureAwait(false);
                await yanit.Content.ReadAsStringAsync(iptal).ConfigureAwait(false);
            }
            catch
            {
                // En iyi caba: durdururken hata olsa da akis durur.
            }
        }

        public KareVerisi KareYakala()
        {
            Screen ekran = EkranBul();
            if (ekran == null)
            {
                throw new GonderimHatasi("Ekran bulunamadı.");
            }
            Rectangle sinir = ekran.Bounds;
            if (sinir.Width <= 0 || sinir.Height <= 0)
            {
                throw new GonderimHatasi("Ekran boyutu okunamadı.");
            }

            var kronometre = Stopwatch.StartNew();
            using var tam = new Bitmap(sinir.Width, sinir.Height, PixelFormat.Format24bppRgb);
            using (var grafik = Graphics.FromImage(tam))
            {
                grafik.CopyFromScreen(sinir.Location, Point.Empty, sinir.Size,
                    CopyPixelOperation.SourceCopy);
                ImleciCiz(grafik, sinir);
            }
            long yakalamaMs = kronometre.ElapsedMilliseconds;

            kronometre.Restart();
            double olcek = Math.Min((double)_azamiGenislik / tam.Width,
                (double)_azamiYukseklik / tam.Height);
            if (olcek > 1.0)
            {
                olcek = 1.0;
            }
            int genislik = Math.Max(1, (int)Math.Round(tam.Width * olcek));
            int yukseklik = Math.Max(1, (int)Math.Round(tam.Height * olcek));

            using var kucuk = new Bitmap(genislik, yukseklik, PixelFormat.Format24bppRgb);
            using (var grafik = Graphics.FromImage(kucuk))
            {
                grafik.InterpolationMode = InterpolationMode.HighQualityBilinear;
                grafik.PixelOffsetMode = PixelOffsetMode.HighQuality;
                grafik.DrawImage(tam, 0, 0, genislik, yukseklik);
            }

            using var akis = new MemoryStream();
            using var parametreler = new EncoderParameters(1);
            parametreler.Param[0] = new EncoderParameter(
                System.Drawing.Imaging.Encoder.Quality, _jpegKalitesi);
            kucuk.Save(akis, JpegKodek, parametreler);
            long sikistirmaMs = kronometre.ElapsedMilliseconds;

            return new KareVerisi
            {
                Jpeg = akis.ToArray(),
                YakalamaMs = yakalamaMs,
                SikistirmaMs = sikistirmaMs,
                Genislik = genislik,
                Yukseklik = yukseklik,
                EkranAdi = ekran.DeviceName
            };
        }

        private Screen EkranBul()
        {
            if (!string.IsNullOrEmpty(_monitorAdi))
            {
                Screen secili = Screen.AllScreens.FirstOrDefault(ekran =>
                    string.Equals(ekran.DeviceName, _monitorAdi,
                        StringComparison.OrdinalIgnoreCase));
                if (secili != null)
                {
                    return secili;
                }
                if (!_uyariVerildi)
                {
                    _uyariVerildi = true;
                    if (_uyari != null)
                    {
                        _uyari("Seçili monitör bulunamadı; ana monitöre dönüldü.");
                    }
                }
            }
            return Screen.PrimaryScreen;
        }

        private static void ImleciCiz(Graphics grafik, Rectangle sinir)
        {
            CURSORINFO bilgi = new CURSORINFO();
            bilgi.cbSize = Marshal.SizeOf(typeof(CURSORINFO));
            if (!GetCursorInfo(ref bilgi) || (bilgi.flags & CURSOR_SHOWING) == 0)
            {
                return;
            }
            int x = bilgi.ptScreenPos.X - sinir.Left;
            int y = bilgi.ptScreenPos.Y - sinir.Top;
            if (x < 0 || y < 0 || x >= sinir.Width || y >= sinir.Height)
            {
                return;
            }
            IntPtr hdc = grafik.GetHdc();
            try
            {
                DrawIconEx(hdc, x, y, bilgi.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
            }
            finally
            {
                grafik.ReleaseHdc(hdc);
            }
        }

        private static long BaslikSayisi(HttpResponseMessage yanit, string ad)
        {
            string metin = BaslikMetni(yanit, ad);
            long deger;
            return long.TryParse(metin, out deger) ? deger : 0;
        }

        private static string BaslikMetni(HttpResponseMessage yanit, string ad)
        {
            System.Collections.Generic.IEnumerable<string> degerler;
            if (yanit.Headers.TryGetValues(ad, out degerler))
            {
                return degerler.FirstOrDefault();
            }
            return null;
        }

        public void Dispose()
        {
            _http.Dispose();
        }

        private static string Kacis(string metin)
        {
            return metin.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public POINT ptScreenPos;
        }

        private const int CURSOR_SHOWING = 0x00000001;
        private const int DI_NORMAL = 0x0003;

        [DllImport("user32.dll")]
        private static extern bool GetCursorInfo(ref CURSORINFO pci);

        [DllImport("user32.dll")]
        private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon,
            int cx, int cy, int istep, IntPtr hbrFlickerFreeDraw, int diFlags);
    }

    internal sealed class KareVerisi
    {
        public byte[] Jpeg { get; set; }
        public long Seq { get; set; }
        public long UretimTicks { get; set; }
        public long YakalamaMs { get; set; }
        public long SikistirmaMs { get; set; }
        public int Genislik { get; set; }
        public int Yukseklik { get; set; }
        public string EkranAdi { get; set; }
    }

    internal sealed class KareGonderimSonucu
    {
        public long ToplamMs { get; set; }
        public long CozMs { get; set; }
        public long CizMs { get; set; }
        public long SunucuMs { get; set; }
        public long AgMs { get; set; }
        public bool Atlandi { get; set; }
    }

    internal sealed class GonderimHatasi : Exception
    {
        public GonderimHatasi(string mesaj) : base(mesaj)
        {
        }
    }

    internal sealed class GonderimSonucu
    {
        public long KareSayisi { get; set; }
        public long UretilenKare { get; set; }
        public long AtlananKare { get; set; }
        public double SureSn { get; set; }
        public double OrtalamaFps { get; set; }
        public double OrtalamaGecikmeMs { get; set; }
        public double OrtalamaYakalamaMs { get; set; }
        public double OrtalamaSikistirmaMs { get; set; }
        public double OrtalamaGonderimMs { get; set; }
        public double OrtalamaCozMs { get; set; }
        public double OrtalamaCizMs { get; set; }
        public double OrtalamaSunucuMs { get; set; }
        public double OrtalamaAgMs { get; set; }
    }
}
