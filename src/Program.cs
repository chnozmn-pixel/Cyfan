// Cyfan - giris noktasi.
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Cyfan
{
    static class Program
    {
        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int pid);

        [STAThread]
        static int Main(string[] args)
        {
            Settings.Load();
            L.Load();

            if (args.Length > 0 && args[0] == "--selftest")
            {
                AttachConsole(-1);   // varsa cagiran konsola yaz
                return SelfTest.Run(args.Length > 1 ? args[1] : null);
            }

            // Klasoru birlestir (+ istege bagli parcalama):
            //   Cyfan.exe --merge <klasor> <parca_saniye> [cikti_dosyasi]
            if (args.Length > 1 && args[0] == "--merge")
            {
                AttachConsole(-1);
                double seg = 0;
                if (args.Length > 2) double.TryParse(args[2],
                    System.Globalization.NumberStyles.Any, Util.Inv, out seg);
                return SelfTest.RunMerge(args[1], seg, args.Length > 3 ? args[3] : null);
            }

            // Yerlesim dokumu (tani icin):  Cyfan.exe --dumplayout
            if (args.Length > 0 && args[0] == "--dumplayout")
            {
                AttachConsole(-1);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                return SelfTest.DumpLayout();
            }

            // Gercek bir video dosyasi uzerinde butun islemleri calistirir:
            //   Cyfan.exe --testfile <video> [cikti klasoru]
            if (args.Length > 1 && args[0] == "--testfile")
            {
                AttachConsole(-1);
                return SelfTest.RunOnFile(args[1], args.Length > 2 ? args[2] : null);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                MessageBox.Show("Beklenmeyen hata:\r\n\r\n" + e.Exception.Message, App.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.Run(new MainForm());
            return 0;
        }
    }
}
