using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TabletEkranGonderici
{
    internal sealed class MainForm : Form
    {
        private readonly TextBox _ipKutusu = new TextBox();
        private readonly TextBox _kodKutusu = new TextBox();
        private readonly ComboBox _monitorKutusu = new ComboBox();
        private readonly Button _dugme = new Button();
        private readonly Button _ekranAyarlari = new Button();
        private readonly Label _not = new Label();
        private readonly Label _durum = new Label();
        private readonly string _ayarYolu =
            Path.Combine(AppContext.BaseDirectory, "ayarlar.txt");

        private CancellationTokenSource _iptal;
        private Gonderici _gonderici;
        private string _token;
        private string _kayitliMonitor;
        private string _uyariMetni;
        private bool _calisiyor;

        public MainForm()
        {
            Text = "TabletEkran Gönderici";
            ClientSize = new Size(540, 380);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            var ipEtiket = new Label
            {
                Text = "Tablet IP:",
                Location = new Point(16, 22),
                AutoSize = true
            };
            _ipKutusu.Location = new Point(110, 19);
            _ipKutusu.Width = 400;

            var kodEtiket = new Label
            {
                Text = "Kod:",
                Location = new Point(16, 62),
                AutoSize = true
            };
            _kodKutusu.Location = new Point(110, 59);
            _kodKutusu.Width = 100;
            _kodKutusu.MaxLength = 4;

            var monitorEtiket = new Label
            {
                Text = "Monitör:",
                Location = new Point(16, 102),
                AutoSize = true
            };
            _monitorKutusu.Location = new Point(110, 99);
            _monitorKutusu.Width = 400;
            _monitorKutusu.DropDownStyle = ComboBoxStyle.DropDownList;

            _ekranAyarlari.Text = "Windows ekran ayarlarını aç";
            _ekranAyarlari.Location = new Point(16, 140);
            _ekranAyarlari.Size = new Size(220, 27);
            _ekranAyarlari.Click += EkranAyarlari_Click;

            _not.Text = "Monitör konumu Windows ekran ayarlarından ayarlanır. " +
                "Bu uygulama ekran düzenini değiştirmez.";
            _not.Location = new Point(16, 175);
            _not.Size = new Size(500, 40);
            _not.ForeColor = Color.FromArgb(80, 80, 80);

            _dugme.Text = "Başlat";
            _dugme.Location = new Point(110, 220);
            _dugme.Size = new Size(130, 34);
            _dugme.Click += Dugme_Click;

            _durum.Text = "Hazır.";
            _durum.Location = new Point(16, 270);
            _durum.Size = new Size(500, 90);

            Controls.Add(ipEtiket);
            Controls.Add(_ipKutusu);
            Controls.Add(kodEtiket);
            Controls.Add(_kodKutusu);
            Controls.Add(monitorEtiket);
            Controls.Add(_monitorKutusu);
            Controls.Add(_ekranAyarlari);
            Controls.Add(_not);
            Controls.Add(_dugme);
            Controls.Add(_durum);

            Load += (gonderici, olay) =>
            {
                AyarlariYukle();
                MonitorleriYukle();
            };
            FormClosing += MainForm_FormClosing;
        }

        private void AyarlariYukle()
        {
            try
            {
                if (!File.Exists(_ayarYolu))
                {
                    return;
                }
                foreach (string satir in File.ReadAllLines(_ayarYolu))
                {
                    string temiz = satir.Trim();
                    if (temiz.Length == 0)
                    {
                        continue;
                    }
                    int esit = temiz.IndexOf('=');
                    if (esit < 0)
                    {
                        if (_ipKutusu.Text.Length == 0)
                        {
                            _ipKutusu.Text = temiz;
                        }
                        continue;
                    }
                    string anahtar = temiz.Substring(0, esit).Trim();
                    string deger = temiz.Substring(esit + 1).Trim();
                    if (string.Equals(anahtar, "IP", StringComparison.OrdinalIgnoreCase))
                    {
                        _ipKutusu.Text = deger;
                    }
                    else if (string.Equals(anahtar, "Monitor", StringComparison.OrdinalIgnoreCase))
                    {
                        _kayitliMonitor = deger;
                    }
                }
            }
            catch
            {
                // Ayar dosyasi okunamazsa varsayilanlar kullanilir.
            }
        }

        private void AyarlariKaydet(string ip, string monitorAdi)
        {
            try
            {
                File.WriteAllLines(_ayarYolu, new[]
                {
                    "IP=" + ip,
                    "Monitor=" + (monitorAdi == null ? "" : monitorAdi)
                });
            }
            catch
            {
                // Yalnizca uygulama klasorune yazilir.
            }
        }

        private void MonitorleriYukle()
        {
            _monitorKutusu.Items.Clear();
            Screen[] ekranlar = MonitorYardimci.Sirali();
            if (ekranlar.Length == 0)
            {
                _durum.Text = "Monitör bulunamadı.";
                return;
            }

            int secilecek = -1;
            for (int i = 0; i < ekranlar.Length; i++)
            {
                _monitorKutusu.Items.Add(new MonitorOgesi(i + 1, ekranlar[i]));
                if (_kayitliMonitor != null &&
                    string.Equals(ekranlar[i].DeviceName, _kayitliMonitor,
                        StringComparison.OrdinalIgnoreCase))
                {
                    secilecek = i;
                }
            }
            if (secilecek < 0)
            {
                for (int i = 0; i < ekranlar.Length; i++)
                {
                    if (ekranlar[i].Primary)
                    {
                        secilecek = i;
                        break;
                    }
                }
            }
            if (secilecek < 0)
            {
                secilecek = 0;
            }
            _monitorKutusu.SelectedIndex = secilecek;
        }

        private void AnaMonitoreSec()
        {
            for (int i = 0; i < _monitorKutusu.Items.Count; i++)
            {
                var oge = _monitorKutusu.Items[i] as MonitorOgesi;
                if (oge != null && oge.Ekran.Primary)
                {
                    _monitorKutusu.SelectedIndex = i;
                    return;
                }
            }
        }

        private async void Dugme_Click(object sender, EventArgs e)
        {
            if (_calisiyor)
            {
                if (_iptal != null)
                {
                    _iptal.Cancel();
                }
                Durum("Durduruluyor...");
                return;
            }

            await BaslatAsync();
        }

        private async Task BaslatAsync()
        {
            string ip = _ipKutusu.Text.Trim();
            string kod = _kodKutusu.Text.Trim();

            if (ip.Length == 0)
            {
                Durum("Tablet IP adresi girin.");
                return;
            }
            int kodSayisi;
            if (kod.Length != 4 || !int.TryParse(kod, out kodSayisi))
            {
                Durum("Kod 4 haneli olmalı.");
                return;
            }
            var monitor = _monitorKutusu.SelectedItem as MonitorOgesi;
            if (monitor == null)
            {
                Durum("Monitör seçin.");
                return;
            }

            AyarlariKaydet(ip, monitor.Ekran.DeviceName);

            _calisiyor = true;
            _dugme.Text = "Durdur";
            _ipKutusu.Enabled = false;
            _kodKutusu.Enabled = false;
            _monitorKutusu.Enabled = false;
            _ekranAyarlari.Enabled = false;
            _token = null;
            _uyariMetni = null;
            _iptal = new CancellationTokenSource();
            _gonderici = new Gonderici(ip, monitor.Ekran.DeviceName, 0, 0, 0, Uyari);

            try
            {
                Durum("Eşleşiliyor...");
                _token = await _gonderici.EslesAsync(kod, _iptal.Token);
                Durum("Bağlandı: " + monitor + " — kareler gönderiliyor...");

                var sonuc = await Task.Run(
                    () => _gonderici.GonderAsync(TimeSpan.MaxValue,
                        fps => Durum(string.Format(
                            "{0}Bağlı — {1:0.0} kare/sn",
                            _uyariMetni == null ? "" : "UYARI: " + _uyariMetni + " — ",
                            fps)),
                        _iptal.Token),
                    _iptal.Token);

                Durum(string.Format("Durduruldu — {0} kare gönderildi.", sonuc.KareSayisi));
            }
            catch (OperationCanceledException)
            {
                Durum("Durduruldu.");
            }
            catch (Exception hata)
            {
                Durum("Hata: " + hata.Message);
            }
            finally
            {
                if (!string.IsNullOrEmpty(_token) && _gonderici != null)
                {
                    try
                    {
                        using var kisa = new CancellationTokenSource(2000);
                        await _gonderici.BitirAsync(kisa.Token);
                    }
                    catch
                    {
                        // yoksay
                    }
                }
                if (_gonderici != null)
                {
                    _gonderici.Dispose();
                    _gonderici = null;
                }
                _token = null;
                if (_iptal != null)
                {
                    _iptal.Dispose();
                    _iptal = null;
                }
                _calisiyor = false;
                _dugme.Text = "Başlat";
                _ipKutusu.Enabled = true;
                _kodKutusu.Enabled = true;
                _monitorKutusu.Enabled = true;
                _ekranAyarlari.Enabled = true;
            }
        }

        private void EkranAyarlari_Click(object sender, EventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:display")
                {
                    UseShellExecute = true
                });
                Durum("Windows ekran ayarları açıldı. Ekranı sürükleyerek konumlandırın.");
            }
            catch (Exception hata)
            {
                Durum("Windows ekran ayarları açılamadı: " + hata.Message);
            }
        }

        private void Uyari(string metin)
        {
            _uyariMetni = metin;
            Durum("UYARI: " + metin);
            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(AnaMonitoreSec));
                }
                else
                {
                    AnaMonitoreSec();
                }
            }
            catch
            {
                // Form kapaniyorsa yoksay.
            }
        }

        private void Durum(string metin)
        {
            if (IsDisposed)
            {
                return;
            }
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (!IsDisposed)
                        {
                            _durum.Text = metin;
                        }
                    }));
                }
                catch
                {
                    // Form kapaniyorsa guncelleme atlanir.
                }
            }
            else
            {
                _durum.Text = metin;
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_calisiyor)
            {
                return;
            }
            if (_iptal != null)
            {
                _iptal.Cancel();
            }
            Gonderici gonderici = _gonderici;
            if (gonderici != null && !string.IsNullOrEmpty(_token))
            {
                try
                {
                    using var kisa = new CancellationTokenSource(1500);
                    gonderici.BitirAsync(kisa.Token).Wait(1500);
                }
                catch
                {
                    // En iyi caba.
                }
            }
        }

        private sealed class MonitorOgesi
        {
            public MonitorOgesi(int sira, Screen ekran)
            {
                Sira = sira;
                Ekran = ekran;
            }

            public int Sira { get; private set; }
            public Screen Ekran { get; private set; }

            public override string ToString()
            {
                return MonitorYardimci.Metin(Sira, Ekran);
            }
        }
    }
}
