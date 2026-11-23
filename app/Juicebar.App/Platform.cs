using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Juicebar.Core;
using Juicebar.Core.Sources;

namespace Juicebar.App;

// Global Ctrl+Alt+B on Windows: RegisterHotKey on a dedicated thread with its own message loop.
// (Linux and macOS: the user assigns "juicebar --show" to a shortcut in the system settings.)
[SupportedOSPlatform("windows")]
sealed class WinHotkey : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public int x, y;
        public uint lPrivate;
    }

    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

    const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_NOREPEAT = 0x4000, WM_HOTKEY = 0x0312, WM_QUIT = 0x0012;

    // Tried in order until one is free.
    static readonly (uint Mods, uint Key, string Text)[] Choices =
    {
        (MOD_CONTROL | MOD_ALT, 0x42, "Ctrl+Alt+B"),
        (MOD_CONTROL | MOD_ALT, 0x4D, "Ctrl+Alt+M"),
        (MOD_CONTROL | MOD_SHIFT | MOD_ALT, 0x42, "Ctrl+Shift+Alt+B"),
    };

    uint threadId;

    public bool Registered => Combo.Length > 0;
    public string Combo { get; private set; } = "";   // the shortcut in use, empty if all were taken

    public WinHotkey(Action onPress)
    {
        using var ready = new ManualResetEventSlim();
        var t = new Thread(() =>
        {
            threadId = GetCurrentThreadId();
            foreach (var c in Choices)
                if (RegisterHotKey(IntPtr.Zero, 1, c.Mods | MOD_NOREPEAT, c.Key)) { Combo = c.Text; break; }
            ready.Set();
            if (!Registered) return;
            while (GetMessage(out MSG m, IntPtr.Zero, 0, 0) > 0)
                if (m.message == WM_HOTKEY) onPress();
            UnregisterHotKey(IntPtr.Zero, 1);
        }) { IsBackground = true, Name = "Juicebar hotkey" };
        t.Start();
        ready.Wait(2000);
        Log.Write(Registered ? "hotkey: " + Combo : "hotkey: every shortcut tried is already in use");
    }

    public void Dispose()
    {
        if (Registered) PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        Combo = "";
    }
}

// Short alert sound with what each system has at hand.
static class Sound
{
    [DllImport("user32.dll")] static extern bool MessageBeep(uint type);

    public static void Play()
    {
        try
        {
            if (OperatingSystem.IsWindows()) { MessageBeep(0x40); return; }
            if (OperatingSystem.IsMacOS()) { Process.Start("/usr/bin/afplay", "/System/Library/Sounds/Glass.aiff"); return; }
            string? canberra = Run.Which("canberra-gtk-play");
            if (canberra != null) { Process.Start(canberra, "-i message"); return; }
            string? paplay = Run.Which("paplay");
            const string sample = "/usr/share/sounds/freedesktop/stereo/message.oga";
            if (paplay != null && File.Exists(sample)) Process.Start(paplay, sample);
        }
        catch (Exception ex) { Log.Write("sound: " + ex.Message); }
    }
}

static class Shell
{
    // Opens a web page in the default browser (start / open / xdg-open). Only https links are
    // accepted, so this can never be turned into "run this file".
    public static void Open(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            Log.Write("blocked a non-https link");
            return;
        }
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write("open url: " + ex.Message); }
    }
}
