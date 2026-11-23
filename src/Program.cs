using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MonitBat
{
    static class Program
    {
        public const string ShowEventName = @"Local\MonitBat_Show";

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Un error en la interfaz se registra y la app sigue: sin esto .NET muestra un cuadro que permite cerrarla.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Log.Write("error en la interfaz: " + e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("error fatal: " + e.ExceptionObject);

            // MonitBat.exe --diag  ->  solo la ventana de diagnóstico
            if (HasArg(args, "diag"))
            {
                Application.Run(new DiagForm());
                return;
            }

            bool createdNew;
            using (new Mutex(true, @"Local\MonitBat_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    // Ya hay una instancia en la bandeja: se le pide que muestre su ventana.
                    EventWaitHandle show;
                    if (EventWaitHandle.TryOpenExisting(ShowEventName, out show))
                        using (show) show.Set();
                    return;
                }
                Log.Write("inicio (pid " + System.Diagnostics.Process.GetCurrentProcess().Id + ")");
                AppDomain.CurrentDomain.ProcessExit += (s, e) => Log.Write("fin del proceso");
                // MonitBat.exe --show  ->  abre también la ventana (accesos directos); sin argumento, solo el icono.
                Application.Run(new TrayApp(HasArg(args, "show")));
            }
        }

        static bool HasArg(string[] args, string name)
        {
            return args.Length > 0 && args[0].Trim('-', '/').Equals(name, StringComparison.OrdinalIgnoreCase);
        }
    }

    // Registro mínimo para saber por qué se cerró la app. Un "inicio" sin "fin del proceso" antes del
    // siguiente "inicio" significa que Windows u otro programa la terminó a la fuerza.
    static class Log
    {
        const long MaxBytes = 256 * 1024;
        static readonly object sync = new object();
        static readonly string path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonitBat", "monitbat.log");

        public static void Write(string message)
        {
            try
            {
                lock (sync)
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > MaxBytes) info.Delete();
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
                }
            }
            catch { } // el registro nunca debe tumbar la app
        }
    }
}
