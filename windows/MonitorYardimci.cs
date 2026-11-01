using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace TabletEkranGonderici
{
    /// <summary>
    /// Monitorleri ana monitor once olacak sekilde siralar ve metin uretir.
    /// Boylece --monitor 1 her zaman ana monitor olur.
    /// </summary>
    internal static class MonitorYardimci
    {
        public static Screen[] Sirali()
        {
            List<Screen> liste = Screen.AllScreens.ToList();
            liste.Sort((a, b) =>
            {
                if (a.Primary != b.Primary)
                {
                    return a.Primary ? -1 : 1;
                }
                int karsilastirma = a.Bounds.X.CompareTo(b.Bounds.X);
                if (karsilastirma != 0)
                {
                    return karsilastirma;
                }
                return a.Bounds.Y.CompareTo(b.Bounds.Y);
            });
            return liste.ToArray();
        }

        public static string Metin(int sira, Screen ekran)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0}: {1}x{2} @ ({3},{4}){5} — {6}",
                sira,
                ekran.Bounds.Width,
                ekran.Bounds.Height,
                ekran.Bounds.X,
                ekran.Bounds.Y,
                ekran.Primary ? " (Ana)" : "",
                ekran.DeviceName);
        }
    }
}
