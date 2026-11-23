using System.Globalization;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Juicebar.Core;

namespace Juicebar.App;

// Tray icons, periodic polling, alerts, system monitor, widget, hotkey, update check and the windows.
sealed class TrayController
{
    const int Slots = 6;   // maximum per-device tray icons; one more slot holds the system metric icon

    readonly Application app;
    readonly IClassicDesktopStyleApplicationLifetime desktop;
    readonly Settings settings = Settings.Load();
    readonly DeviceMonitor monitor = new();
    readonly SystemMonitor system = new();
    readonly StatusWindow status = new();
    readonly ToastWindow toast = new();
    SettingsWindow? settingsWindow;
    WidgetWindow? widget;
    IDisposable? hotkey;
    string? hotkeyCombo;
    readonly TrayIcon[] icons = new TrayIcon[Slots + 1];
    readonly string?[] keys = new string?[Slots + 1];
    readonly Bitmap?[] bitmaps = new Bitmap?[Slots + 1];
    readonly List<NativeMenuItem> startupItems = new(), widgetItems = new();
    readonly DispatcherTimer timer, sysTimer, updateTimer;
    int busy, sysBusy;
    string lastUpdate = "";
    SystemSnapshot? lastSys;
    Updater.Release? update;
    bool updateNotified;
    Lang menuLang;

    public TrayController(Application app, IClassicDesktopStyleApplicationLifetime desktop, bool showAtStart)
    {
        this.app = app;
        this.desktop = desktop;
        Loc.Current = settings.Language;
        DeviceState.LowLevel = settings.LowThreshold;
        monitor.Load();

        // Icons are always created in the same order: Windows remembers per position whether each one is pinned.
        var list = new TrayIcons();
        for (int i = 0; i <= Slots; i++)
        {
            var t = new TrayIcon { IsVisible = false, ToolTipText = "Juicebar" };
            t.Clicked += (s, e) => Toggle();
            icons[i] = t;
            list.Add(t);
        }
        menuLang = settings.Language;
        RebuildMenus();
        TrayIcon.SetIcons(app, list);

        status.SettingsRequested += ShowSettings;
        app.ActualThemeVariantChanged += (s, e) => { RefreshWindows(); Render(); };
        RefreshWindows();
        Render();

        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += (s, e) => Poll(false);
        timer.Start();
        sysTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        sysTimer.Tick += (s, e) => { if (SystemNeeded) SampleSystem(); };
        sysTimer.Start();
        updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        updateTimer.Tick += (s, e) => AutoCheckUpdates();
        updateTimer.Start();

        Poll(true);
        SampleSystem();
        ApplyHotkey();
        ApplyWidget();
        DispatcherTimer.RunOnce(AutoCheckUpdates, TimeSpan.FromSeconds(20));

        SingleInstance.Listen(() => Dispatcher.UIThread.Post(ShowStatus));
        if (showAtStart) ShowStatus();
    }

    // ---- menu ----

    void RebuildMenus()
    {
        startupItems.Clear();
        widgetItems.Clear();
        foreach (TrayIcon t in icons) t.Menu = BuildMenu();
    }

    NativeMenu BuildMenu()
    {
        var m = new NativeMenu();
        m.Items.Add(Item(Loc.T("menu_show"), ShowStatus));
        m.Items.Add(Item(Loc.T("menu_settings"), ShowSettings));
        var w = new NativeMenuItem(Loc.T("menu_widget")) { ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = settings.Widget };
        w.Click += (s, e) => { settings.Widget = !settings.Widget; settings.Save(); ApplyWidget(); };
        widgetItems.Add(w);
        m.Items.Add(w);
        var startup = new NativeMenuItem(Loc.T("menu_startup")) { ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = Autostart.IsEnabled() };
        startup.Click += (s, e) => SetStartup(!Autostart.IsEnabled());
        startupItems.Add(startup);
        m.Items.Add(startup);
        m.Items.Add(Item(Loc.T("menu_diag"), () => new DiagWindow().Show()));
        if (update != null)
        {
            string url = update.Url;
            m.Items.Add(Item(Loc.F("menu_update", update.Tag), () => Shell.Open(url)));
        }
        m.Items.Add(new NativeMenuItemSeparator());
        m.Items.Add(Item(Loc.T("menu_exit"), Exit));
        return m;
    }

    static NativeMenuItem Item(string text, Action action)
    {
        var i = new NativeMenuItem(text);
        i.Click += (s, e) => action();
        return i;
    }

    void SetStartup(bool on)
    {
        Autostart.Set(on);
        bool now = Autostart.IsEnabled();
        foreach (NativeMenuItem i in startupItems) i.IsChecked = now;
    }

    // ---- windows ----

    bool WindowLight => app.ActualThemeVariant == ThemeVariant.Light;

    void Toggle()
    {
        if (status.IsVisible) status.Hide();
        else ShowStatus();
    }

    void ShowStatus()
    {
        RefreshWindows();
        status.ShowNearTray();
        Poll(true);   // fresh data when opening
        SampleSystem();
    }

    void ShowSettings()
    {
        if (settingsWindow == null)
        {
            settingsWindow = new SettingsWindow(settings, monitor)
            {
                HotkeyState = () => hotkeyCombo,
                CheckNow = CheckNowAsync,
            };
            settingsWindow.Changed += OnSettingsChanged;
            settingsWindow.StartupToggled += SetStartup;
            settingsWindow.TestAlert += () => Notify(Loc.T("alert_test_title"), Loc.T("alert_test_body"), Palette.Accent(WindowLight));
        }
        settingsWindow.SetLight(WindowLight);
        if (lastSys != null) settingsWindow.SetSystem(lastSys);
        settingsWindow.ShowCentered();
    }

    void OnSettingsChanged()
    {
        Loc.Current = settings.Language;
        DeviceState.LowLevel = settings.LowThreshold;
        monitor.Sync();
        if (menuLang != settings.Language)
        {
            menuLang = settings.Language;
            RebuildMenus();
        }
        Array.Clear(keys);
        RefreshWindows();
        Render();
        ApplyHotkey();
        ApplyWidget();
        if (SystemNeeded) SampleSystem();
    }

    void RefreshWindows()
    {
        List<CardModel> cards = Cards();
        status.Update(settings.Window, WindowLight, cards, lastUpdate);
        status.SetSystem(settings.ShowSystem ? lastSys : null);
        widget?.Update(cards, WindowLight);
        settingsWindow?.SetLight(WindowLight);
        toast.SetLight(WindowLight);
    }

    List<CardModel> Cards() => monitor.Visible.Select(d => new CardModel
    {
        Kind = Glyphs.KindLabel(d.Kind),
        Name = d.Display,
        Glyph = d.Kind,
        ColorIndex = d.ColorIndex,
        Percent = d.IconPercent,
        Live = d.Live,
        Charging = d.Live && d.Last.Charging,
        Low = d.Low,
        Detail = d.Detail(),
        Eta = monitor.EtaText(d),
        Points = monitor.History.Of(d.Id),
    }).ToList();

    void ApplyWidget()
    {
        if (settings.Widget)
        {
            if (widget == null)
            {
                widget = new WidgetWindow(settings);
                widget.OpenRequested += ShowStatus;
            }
            widget.Update(Cards(), WindowLight);
            if (!widget.IsVisible) widget.ShowAt();
        }
        else widget?.Hide();
        foreach (NativeMenuItem i in widgetItems) i.IsChecked = settings.Widget;
    }

    void ApplyHotkey()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (settings.Hotkey && hotkey == null)
        {
            var h = new WinHotkey(() => Dispatcher.UIThread.Post(Toggle));
            hotkeyCombo = h.Combo;
            hotkey = h;
        }
        else if (!settings.Hotkey && hotkey != null)
        {
            hotkey.Dispose();
            hotkey = null;
            hotkeyCombo = null;
        }
    }

    // ---- battery polling and alerts ----

    void Poll(bool force)
    {
        if (Interlocked.Exchange(ref busy, 1) == 1) return;
        Task.Run(() =>
        {
            try
            {
                IReadOnlyList<DeviceReading> r = monitor.Poll(force);
                Dispatcher.UIThread.Post(() => Apply(r));
            }
            catch (Exception ex) { Log.Write("poll: " + ex.Message); }
            finally { Interlocked.Exchange(ref busy, 0); }
        });
    }

    void Apply(IReadOnlyList<DeviceReading> readings)
    {
        monitor.Apply(readings);
        foreach (DeviceState d in monitor.Visible) CheckAlerts(d);
        lastUpdate = Loc.F("last_update", DateTime.Now.ToString("HH:mm:ss"));
        RefreshWindows();
        Render();
    }

    void CheckAlerts(DeviceState d)
    {
        if (!d.Live) return;
        if (d.Low)
        {
            if (!d.Warned) Notify(Loc.T("alert_low_title"), $"{d.Display}: {d.Last.Percent}%", Palette.Low(WindowLight));
            d.Warned = true;
        }
        else if (d.Last.Charging || d.Last.Percent >= DeviceState.LowLevel + 5)
            d.Warned = false;

        bool full = d.Last.Full || (d.Last.Charging && d.Last.Percent >= 100);
        if (full && d.WasCharging && !d.FullWarned && settings.NotifyFull)
        {
            Notify(Loc.T("alert_full_title"), Loc.F("alert_full_body", d.Display), Palette.Charge(WindowLight));
            d.FullWarned = true;
        }
        if (!d.Last.Charging && !d.Last.Full) d.FullWarned = false;
        d.WasCharging = d.Last.Charging || d.Last.Full;
    }

    void Notify(string title, string body, Color color)
    {
        toast.Notify(title, body, color, status.IsVisible ? status : null);
        if (settings.Sound) Sound.Play();
    }

    // ---- system monitor ----

    bool SystemNeeded =>
        settings.TrayMetric != SystemMetric.None || (settings.ShowSystem && status.IsVisible) || settingsWindow?.IsVisible == true;

    void SampleSystem()
    {
        if (Interlocked.Exchange(ref sysBusy, 1) == 1) return;
        Task.Run(() =>
        {
            try
            {
                SystemSnapshot s = system.Sample();
                Dispatcher.UIThread.Post(() =>
                {
                    lastSys = s;
                    status.SetSystem(settings.ShowSystem ? s : null);
                    settingsWindow?.SetSystem(s);
                    RenderMetric(TrayLight(), TrayPixels());
                });
            }
            catch (Exception ex) { Log.Write("system monitor: " + ex.Message); }
            finally { Interlocked.Exchange(ref sysBusy, 0); }
        });
    }

    // ---- updates ----

    async void AutoCheckUpdates()
    {
        if (!settings.CheckUpdates) return;
        Updater.Result res = await Updater.CheckAsync();
        if (res.Release is { } r && Updater.IsNewer(r)) SetUpdate(r);
    }

    async Task<string> CheckNowAsync()
    {
        Updater.Result res = await Updater.CheckAsync();
        if (!res.Ok) return Loc.T("upd_error");
        if (res.Release is not { } r) return Loc.T("upd_none");
        if (!Updater.IsNewer(r)) return Loc.T("upd_latest");
        SetUpdate(r);
        return Loc.F("upd_new", r.Tag);
    }

    void SetUpdate(Updater.Release r)
    {
        update = r;
        RebuildMenus();
        if (updateNotified) return;
        updateNotified = true;
        Notify(Loc.T("alert_update_title"), Loc.F("alert_update_body", r.Tag), Palette.Accent(WindowLight));
    }

    // ---- tray icons ----

    static IconInput Input(DeviceState d) => new(d.IconPercent, d.Live, d.Live && d.Last.Charging, d.ColorIndex, d.Kind);

    void Render()
    {
        bool light = TrayLight();
        int px = TrayPixels();
        IReadOnlyList<DeviceState> devs = monitor.Visible;

        if (settings.Icon == IconStyle.Number && devs.Count > 0)
        {
            int n = Math.Min(devs.Count, Slots);
            for (int i = 0; i < Slots; i++)
            {
                if (i >= n) { icons[i].IsVisible = false; continue; }
                DeviceState d = devs[i];
                IconInput input = Input(d);
                SetIcon(i, $"n/{input}/{light}/{px}", () => IconArt.Number(input, light, px));
                icons[i].ToolTipText = Tip($"{Glyphs.KindLabel(d.Kind)} · {d.Display}: {d.ShortText()}");
                icons[i].IsVisible = true;
            }
        }
        else
        {
            // Combined styles: one icon with the two devices with the lowest battery.
            List<IconInput> pick = devs.OrderBy(d => d.IconPercent < 0 ? 1000 : d.IconPercent).Take(2)
                                       .OrderBy(d => d.ColorIndex).Select(Input).ToList();
            SetIcon(0, $"{settings.Icon}/{string.Join("|", pick)}/{light}/{px}", () => IconArt.Combined(settings.Icon, pick, light, px));
            icons[0].ToolTipText = Tip(devs.Count == 0 ? Loc.T("tip_none") : string.Join("\n", devs.Select(d => $"{d.Display}: {d.ShortText()}")));
            icons[0].IsVisible = true;
            for (int i = 1; i < Slots; i++) icons[i].IsVisible = false;
        }
        RenderMetric(light, px);
    }

    void RenderMetric(bool light, int px)
    {
        SystemMetric m = settings.TrayMetric;
        double? v = lastSys?.Get(m);
        TrayIcon t = icons[Slots];
        if (m == SystemMetric.None || v == null) { t.IsVisible = false; return; }
        double value = Math.Round(v.Value);
        SetIcon(Slots, $"m/{m}/{value}/{light}/{px}", () => IconArt.Metric(m, value, light, px));
        bool temp = m is SystemMetric.CpuTemp or SystemMetric.GpuTemp;
        string key = m switch
        {
            SystemMetric.Cpu => "metric_cpu",
            SystemMetric.Ram => "metric_ram",
            SystemMetric.Gpu => "metric_gpu",
            SystemMetric.GpuTemp => "metric_gputemp",
            _ => "metric_cputemp",
        };
        t.ToolTipText = Tip($"{Loc.T(key)}: {value.ToString(CultureInfo.CurrentCulture)}{(temp ? " °C" : " %")}");
        t.IsVisible = true;
    }

    static string Tip(string s) => s.Length > 120 ? s[..120] : s;   // Windows cuts tray tooltips at 127

    void SetIcon(int slot, string key, Func<Bitmap> draw)
    {
        if (keys[slot] == key) return;
        keys[slot] = key;
        Bitmap bmp = draw();
        icons[slot].Icon = new WindowIcon(bmp);
        bitmaps[slot]?.Dispose();
        bitmaps[slot] = bmp;
    }

    // Real icon size: 16 px times the scale on Windows; on Linux and macOS the panel shrinks it.
    int TrayPixels()
    {
        double scale = status.Screens.Primary?.Scaling ?? 1;
        return OperatingSystem.IsWindows() ? (int)Math.Round(16 * scale) : 32;
    }

    // Text color of the icons follows the system bar (not always the window theme).
    bool TrayLight()
    {
        if (OperatingSystem.IsWindows()) return WindowsTaskbarLight();
        string desktopName = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";
        if (OperatingSystem.IsLinux() && !desktopName.Contains("KDE", StringComparison.OrdinalIgnoreCase)) return false;   // GNOME and most: dark bar
        return WindowLight;
    }

    [SupportedOSPlatform("windows")]
    static bool WindowsTaskbarLight()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch { return false; }
    }

    void Exit()
    {
        Log.Write("user chose Exit");
        timer.Stop();
        sysTimer.Stop();
        updateTimer.Stop();
        hotkey?.Dispose();
        foreach (TrayIcon t in icons)
        {
            t.IsVisible = false;
            t.Dispose();
        }
        desktop.Shutdown();
    }
}
