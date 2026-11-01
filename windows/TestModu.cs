using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace TabletEkranGonderici
{
    internal static class TestModu
    {
        public static int Calistir(string[] args)
        {
            ConsoleYardimci.Hazirla();

            if (args.Length == 0)
            {
                Kullanim();
                return 2;
            }
            if (string.Equals(args[0], "--liste", StringComparison.OrdinalIgnoreCase))
            {
                MonitorleriYazdir();
                return 0;
            }
            if (!string.Equals(args[0], "--test", StringComparison.OrdinalIgnoreCase))
            {
                Kullanim();
                return 2;
            }

            var argumanlar = new List<string>();
            int monitorNo = 0;
            int maxGenislik = 1024;
            int maxYukseklik = 640;
            int kalite = 60;

            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "--monitor", StringComparison.OrdinalIgnoreCase))
                {
                    if (++i >= args.Length || !int.TryParse(args[i], out monitorNo))
                    {
                        Console.WriteLine("--monitor için geçerli bir numara gerekli.");
                        return 2;
                    }
                }
                else if (string.Equals(arg, "--maxw", StringComparison.OrdinalIgnoreCase))
                {
                    if (++i >= args.Length || !int.TryParse(args[i], out maxGenislik))
                    {
                        Console.WriteLine("--maxw için geçerli bir sayı gerekli.");
                        return 2;
                    }
                }
                else if (string.Equals(arg, "--maxh", StringComparison.OrdinalIgnoreCase))
                {
                    if (++i >= args.Length || !int.TryParse(args[i], out maxYukseklik))
                    {
                        Console.WriteLine("--maxh için geçerli bir sayı gerekli.");
                        return 2;
                    }
                }
                else if (string.Equals(arg, "--q", StringComparison.OrdinalIgnoreCase))
                {
                    if (++i >= args.Length || !int.TryParse(args[i], out kalite))
                    {
                        Console.WriteLine("--q için geçerli bir sayı gerekli.");
                        return 2;
                    }
                }
                else
                {
                    argumanlar.Add(arg);
                }
            }

            if (argumanlar.Count != 3)
            {
                Kullanim();
                return 2;
            }

            string ip = argumanlar[0].Trim();
            string kod = argumanlar[1].Trim();
            double saniye;
            if (!double.TryParse(argumanlar[2], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out saniye) || saniye <= 0)
            {
                Console.WriteLine("SANIYE pozitif bir sayı olmalı.");
                return 2;
            }

            Screen[] ekranlar = MonitorYardimci.Sirali();
            if (ekranlar.Length == 0)
            {
                Console.WriteLine("HATA: Monitör bulunamadı.");
                return 1;
            }
            Screen secili;
            if (monitorNo <= 0)
            {
                secili = ekranlar.FirstOrDefault(ekran => ekran.Primary) ?? ekranlar[0];
            }
            else if (monitorNo <= ekranlar.Length)
            {
                secili = ekranlar[monitorNo - 1];
            }
            else
            {
                Console.WriteLine("HATA: --monitor " + monitorNo + " yok; " +
                    ekranlar.Length + " monitör var.");
                return 2;
            }

            try
            {
                using var gonderici = new Gonderici(ip, secili.DeviceName,
                    maxGenislik, maxYukseklik, kalite,
                    uyari => Console.WriteLine("UYARI: " + uyari));

                Console.WriteLine("Monitör: " + MonitorMetni(
                    ekranlar.ToList().IndexOf(secili) + 1, secili));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "Ayarlar: en fazla {0}x{1}, JPEG kalite {2}, hedef 15 kare/sn",
                    maxGenislik, maxYukseklik, kalite));
                Console.WriteLine("Eşleşiliyor: " + ip + ":8765 (kod " + kod + ")");
                string token = gonderici.EslesAsync(kod, CancellationToken.None)
                    .GetAwaiter().GetResult();
                Console.WriteLine("Eşleşme OK (token " + token.Length + " karakter)");

                var sonuc = gonderici.GonderAsync(TimeSpan.FromSeconds(saniye), null,
                        CancellationToken.None)
                    .GetAwaiter().GetResult();

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "Süre: {0:0.0} sn, üretilen: {1}, gönderilen: {2}, atlanan: {3}",
                    sonuc.SureSn, sonuc.UretilenKare, sonuc.KareSayisi, sonuc.AtlananKare));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "Ortalama: {0:0.0} kare/sn, kare gecikmesi: {1:0} ms",
                    sonuc.OrtalamaFps, sonuc.OrtalamaGecikmeMs));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "Aşamalar: yakalama {0:0} ms, JPEG {1:0} ms, HTTP {2:0} ms, " +
                    "ağ ~{3:0} ms, tablet çözme {4:0} ms, tablet çizme {5:0} ms",
                    sonuc.OrtalamaYakalamaMs, sonuc.OrtalamaSikistirmaMs,
                    sonuc.OrtalamaGonderimMs, sonuc.OrtalamaAgMs,
                    sonuc.OrtalamaCozMs, sonuc.OrtalamaCizMs));

                // Test modu /bitir cagirmaz; 5 sn sonra saat ekranina donus test edilir.
                return 0;
            }
            catch (Exception hata)
            {
                Console.WriteLine("HATA: " + hata.Message);
                return 1;
            }
        }

        private static void MonitorleriYazdir()
        {
            Screen[] ekranlar = MonitorYardimci.Sirali();
            Console.WriteLine("Monitör sayısı: " + ekranlar.Length);
            for (int i = 0; i < ekranlar.Length; i++)
            {
                Console.WriteLine(MonitorMetni(i + 1, ekranlar[i]));
            }
        }

        private static string MonitorMetni(int sira, Screen ekran)
        {
            return MonitorYardimci.Metin(sira, ekran);
        }

        private static void Kullanim()
        {
            Console.WriteLine("Kullanım:");
            Console.WriteLine("  TabletEkranGonderici.exe --liste");
            Console.WriteLine("  TabletEkranGonderici.exe --test IP KOD SANIYE " +
                "[--monitor N] [--maxw W] [--maxh H] [--q Q]");
            Console.WriteLine("  --monitor N: listedeki 1 tabanlı monitör numarası " +
                "(varsayılan ana monitör)");
        }
    }
}
