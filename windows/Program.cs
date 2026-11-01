using System;
using System.Windows.Forms;

namespace TabletEkranGonderici
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // CLI test modunda da ekran koordinatlari fiziksel piksel olsun.
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            if (args.Length > 0 &&
                (string.Equals(args[0], "--test", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[0], "--liste", StringComparison.OrdinalIgnoreCase)))
            {
                Environment.ExitCode = TestModu.Calistir(args);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
