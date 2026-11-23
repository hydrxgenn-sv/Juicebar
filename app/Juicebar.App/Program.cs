using System.IO.Pipes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Juicebar.Core;

namespace Juicebar.App;

static class Program
{
    static Mutex? instance;   // kept alive while the app runs

    // Juicebar           -> tray icons only
    // Juicebar --show    -> also opens the window (or asks the running instance to show it)
    // Juicebar --diag    -> diagnostics window only
    [STAThread]
    public static int Main(string[] args)
    {
        bool diag = HasArg(args, "diag"), show = HasArg(args, "show");
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("fatal error: " + e.ExceptionObject);
        AppPaths.MigrateFromMonitBat();
        Loc.Current = Settings.Load().Language;

        if (!diag)
        {
            instance = SingleInstance.TryAcquire();
            if (instance == null)
            {
                SingleInstance.SignalShow();
                return 0;
            }
            Log.Write($"start (pid {Environment.ProcessId}, {System.Runtime.InteropServices.RuntimeInformation.OSDescription})");
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Log.Write("process end");
        }

        App.StartDiag = diag;
        App.StartShow = show;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, diag ? ShutdownMode.OnMainWindowClose : ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .LogToTrace();

    static bool HasArg(string[] args, string name) =>
        args.Any(a => a.Trim('-', '/').Equals(name, StringComparison.OrdinalIgnoreCase));
}

public sealed class App : Application
{
    public static bool StartDiag, StartShow;
    TrayController? tray;

    public override void Initialize()
    {
        Name = "Juicebar";
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Default;   // follow the system theme
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // A UI error is logged and the app keeps running.
        Dispatcher.UIThread.UnhandledException += (s, e) =>
        {
            Log.Write("UI error: " + e.Exception);
            e.Handled = true;
        };
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (StartDiag) desktop.MainWindow = new DiagWindow();
            else tray = new TrayController(this, desktop, StartShow);
        }
        base.OnFrameworkInitializationCompleted();
    }
}

// One instance per user. A second launch tells the first one, through a named pipe
// (works on Windows, Linux and macOS), to show its window.
static class SingleInstance
{
    static string Id => "Juicebar-" + Environment.UserName;

    public static Mutex? TryAcquire()
    {
        var m = new Mutex(true, (OperatingSystem.IsWindows() ? @"Local\" : "") + Id, out bool created);
        if (created) return m;
        m.Dispose();
        return null;
    }

    public static void SignalShow()
    {
        try
        {
            using var c = new NamedPipeClientStream(".", Id + ".show", PipeDirection.Out);
            c.Connect(1500);
            c.WriteByte(1);
        }
        catch (Exception ex) { Log.Write("signal running instance: " + ex.Message); }
    }

    public static void Listen(Action onShow)
    {
        var t = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var s = new NamedPipeServerStream(Id + ".show", PipeDirection.In, 1);
                    s.WaitForConnection();
                    onShow();
                }
                catch (Exception ex)
                {
                    Log.Write("show-window pipe: " + ex.Message);
                    Thread.Sleep(2000);
                }
            }
        }) { IsBackground = true, Name = "Juicebar show" };
        t.Start();
    }
}
