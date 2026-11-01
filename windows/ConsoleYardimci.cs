using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TabletEkranGonderici
{
    internal static class ConsoleYardimci
    {
        private const int EbeveynSureci = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        public static void Hazirla()
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch
            {
                // Konsol kod sayfasi degistirilemezse varsayilan kalir.
            }
            if (Console.IsOutputRedirected)
            {
                return;
            }
            if (!AttachConsole(EbeveynSureci))
            {
                AllocConsole();
            }
        }
    }
}
