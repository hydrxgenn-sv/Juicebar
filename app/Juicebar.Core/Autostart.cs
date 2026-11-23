using System.Runtime.Versioning;
using System.Security;

namespace Juicebar.Core;

// "Start with the system" on each platform:
//   Windows: value Juicebar under HKCU\...\Run
//   Linux:   ~/.config/autostart/juicebar.desktop
//   macOS:   ~/Library/LaunchAgents/com.juicebar.app.plist
public static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "Juicebar";

    static string Exe => Environment.ProcessPath ?? "";
    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    static string LinuxFile
    {
        get
        {
            string xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? "";
            return Path.Combine(xdg.Length > 0 ? xdg : Path.Combine(Home, ".config"), "autostart", "juicebar.desktop");
        }
    }

    static string MacFile => Path.Combine(Home, "Library", "LaunchAgents", "com.juicebar.app.plist");

    public static bool IsEnabled()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return WinEnabled();
            if (OperatingSystem.IsMacOS()) return File.Exists(MacFile);
            return File.Exists(LinuxFile);
        }
        catch { return false; }
    }

    public static void Set(bool enable)
    {
        try
        {
            if (OperatingSystem.IsWindows()) { WinSet(enable); return; }
            string file = OperatingSystem.IsMacOS() ? MacFile : LinuxFile;
            if (!enable) { if (File.Exists(file)) File.Delete(file); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, OperatingSystem.IsMacOS() ? MacPlist() : LinuxDesktop());
        }
        catch (Exception ex) { Log.Write("autostart: " + ex.Message); }
    }

    static string LinuxDesktop() =>
        "[Desktop Entry]\nType=Application\nName=Juicebar\nComment=Peripheral batteries\n" +
        $"Exec=\"{Exe}\"\nIcon=juicebar\nX-GNOME-Autostart-enabled=true\nTerminal=false\n";

    static string MacPlist() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
        "<plist version=\"1.0\"><dict>\n" +
        "  <key>Label</key><string>com.juicebar.app</string>\n" +
        $"  <key>ProgramArguments</key><array><string>{SecurityElement.Escape(Exe)}</string></array>\n" +
        "  <key>RunAtLoad</key><true/>\n" +
        "</dict></plist>\n";

    [SupportedOSPlatform("windows")]
    static bool WinEnabled()
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
        // Full path: version 1 used the same value name with a different executable.
        return k?.GetValue(RunValue) is string v && string.Equals(v.Trim('"'), Exe, StringComparison.OrdinalIgnoreCase);
    }

    [SupportedOSPlatform("windows")]
    static void WinSet(bool enable)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
        if (enable) k.SetValue(RunValue, "\"" + Exe + "\"");
        else k.DeleteValue(RunValue, false);
    }
}
