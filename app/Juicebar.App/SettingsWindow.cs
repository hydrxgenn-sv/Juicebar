using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Juicebar.Core;

namespace Juicebar.App;

// Settings in five tabs: appearance (icon and window styles with previews), devices (name, color, order,
// hide, forget, battery health), alerts, system monitor and general (language, startup, hotkey, widget, updates).
// Every change is applied and saved at once.
public sealed class SettingsWindow : FlyoutWindow
{
    static readonly (IconStyle Style, string Key)[] IconOptions =
    {
        (IconStyle.Number, "icon_number"), (IconStyle.Cells, "icon_cells"), (IconStyle.Rings, "icon_rings"), (IconStyle.Glyphs, "icon_glyphs"),
    };
    static readonly (WindowStyle Style, string Key)[] WindowOptions =
    {
        (WindowStyle.Glass, "win_glass"), (WindowStyle.Rings, "win_rings"), (WindowStyle.Segments, "win_segments"), (WindowStyle.History, "win_history"),
    };
    static readonly string[] MetricKeys = { "metric_none", "metric_cpu", "metric_ram", "metric_gpu", "metric_gputemp", "metric_cputemp" };

    readonly Settings settings;
    readonly DeviceMonitor monitor;
    bool light;
    int tab;
    TextBlock? sysNow;
    SystemSnapshot? lastSys;

    public Func<string?>? HotkeyState { get; set; }        // shortcut in use; "" = all taken; null = not applicable
    public Func<Task<string>>? CheckNow { get; set; }
    public event Action? Changed;
    public event Action<bool>? StartupToggled;
    public event Action? TestAlert;

    public SettingsWindow(Settings s, DeviceMonitor m)
    {
        settings = s;
        monitor = m;
        Title = "Juicebar";
        Rebuild();
    }

    public void SetLight(bool l)
    {
        if (l == light) return;
        light = l;
        Rebuild();
    }

    public void SetSystem(SystemSnapshot s)
    {
        lastSys = s;
        if (sysNow != null) sysNow.Text = SystemText(s);
    }

    public void ShowCentered()
    {
        Rebuild();
        PlaceCenter();
        Show();
        Activate();
        PlaceCenter();
    }

    void Notify(bool rebuild)
    {
        settings.Save();
        Changed?.Invoke();
        if (rebuild) Dispatcher.UIThread.Post(Rebuild);
    }

    public void Rebuild()
    {
        Title = Loc.T("settings_title");
        RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
        var ink = Ink.For(light);

        var close = new IconButton(IconButton.Shape.Close) { Color = ink.Sub, Light = light, Width = 28, Height = 28 };
        close.Click += Hide;
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 6) };
        header.Children.Add(new TextBlock { Text = Loc.T("settings_title"), FontSize = 17.3, FontWeight = FontWeight.SemiBold, Foreground = Draw.B(ink.Text), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        DockPanel.SetDock(header, Dock.Top);

        var hint = new TextBlock { Text = Loc.T("applied_hint"), FontSize = 11.3, Foreground = Draw.B(ink.Sub), Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(hint, Dock.Bottom);

        sysNow = null;
        var tabs = new TabControl
        {
            Items =
            {
                Tab("tab_appearance", Appearance(ink)),
                Tab("tab_devices", Devices(ink)),
                Tab("tab_alerts", Alerts(ink)),
                Tab("tab_system", SystemTab(ink)),
                Tab("tab_general", General(ink)),
            },
            SelectedIndex = tab,
        };
        tabs.SelectionChanged += (s, e) => { if (tabs.SelectedIndex >= 0) tab = tabs.SelectedIndex; };

        SetPanel(new Border
        {
            Width = 700,
            Height = 660,
            Background = Draw.B(ink.Bg),
            BorderBrush = Draw.B(ink.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20, 14, 20, 14),
            Child = new DockPanel { Children = { header, hint, tabs } },
        });
    }

    static TabItem Tab(string key, Control content) => new()
    {
        Header = Loc.T(key),
        FontSize = 14,
        Content = new ScrollViewer { Content = content, Padding = new Thickness(0, 10, 8, 0), VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto },
    };

    static TextBlock Section(string key, Ink ink) =>
        new() { Text = Loc.T(key), FontSize = 13.3, FontWeight = FontWeight.SemiBold, Foreground = Draw.B(ink.Text), Margin = new Thickness(0, 4, 0, 6) };

    static TextBlock Note(string text, Ink ink) =>
        new() { Text = text, FontSize = 11.3, Foreground = Draw.B(ink.Sub), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) };

    CheckBox Check(string key, bool value, Ink ink, Action<bool> set, bool rebuild = false)
    {
        var c = new CheckBox { Content = Loc.T(key), IsChecked = value, Foreground = Draw.B(ink.Text) };
        c.IsCheckedChanged += (s, e) => { set(c.IsChecked == true); Notify(rebuild); };
        return c;
    }

    // ---- Appearance ----

    Control Appearance(Ink ink) => new StackPanel
    {
        Spacing = 4,
        Children =
        {
            Section("sec_tray_icon", ink),
            Row(IconOptions.Select(o => Tile(108, settings.Icon == o.Style, IconPreview(o.Style), Loc.T(o.Key), () => { settings.Icon = o.Style; Notify(true); }, ink))),
            Section("sec_window", ink),
            Row(WindowOptions.Select(o => Tile(236, settings.Window == o.Style, WindowPreview(o.Style), Loc.T(o.Key), () => { settings.Window = o.Style; Notify(true); }, ink))),
        },
    };

    static Control Row(IEnumerable<Control> tiles)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*,10,*,10,*"), Margin = new Thickness(0, 0, 0, 8) };
        int col = 0;
        foreach (Control t in tiles)
        {
            Grid.SetColumn(t, col);
            grid.Children.Add(t);
            col += 2;
        }
        return grid;
    }

    Control Tile(double height, bool selected, Control preview, string name, Action onClick, Ink ink)
    {
        Color accent = Palette.Accent(light);
        Color hover = light ? Color.FromRgb(160, 160, 160) : Color.FromRgb(120, 120, 120);
        var badge = new Border
        {
            Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Draw.B(accent),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -4, -4, 0), IsVisible = selected, Child = new CheckMark { Color = light ? Colors.White : Color.FromRgb(20, 20, 20) },
        };
        var content = new StackPanel
        {
            Children =
            {
                preview,
                new TextBlock { Text = name, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Draw.B(ink.Text), HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 8, 0, 0) },
            },
        };
        var tile = new Border
        {
            Height = height,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Background = Draw.B(ink.CardBg),
            BorderBrush = Draw.B(selected ? accent : ink.CardLine),
            BorderThickness = new Thickness(selected ? 2 : 1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new Panel { Children = { content, badge } },
        };
        tile.PointerEntered += (s, e) => { if (!selected) tile.BorderBrush = Draw.B(hover); };
        tile.PointerExited += (s, e) => { if (!selected) tile.BorderBrush = Draw.B(ink.CardLine); };
        tile.PointerReleased += (s, e) => { if (e.InitialPressMouseButton == MouseButton.Left && !selected) onClick(); };
        return tile;
    }

    Control IconPreview(IconStyle style)
    {
        var m = new IconInput(82, true, false, 0, DeviceKind.Mouse);
        var h = new IconInput(64, true, false, 1, DeviceKind.Headset);
        IEnumerable<Bitmap> bitmaps = style == IconStyle.Number
            ? new[] { IconArt.Number(m, light, 32), IconArt.Number(h, light, 32) }
            : new[] { IconArt.Combined(style, new[] { m, h }, light, 32) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        foreach (Bitmap b in bitmaps)
        {
            var img = new Image { Source = b, Width = 32, Height = 32 };
            RenderOptions.SetBitmapInterpolationMode(img, BitmapInterpolationMode.None);
            row.Children.Add(img);
        }
        return new Border { Height = 58, CornerRadius = new CornerRadius(5), Background = Draw.B(light ? Color.FromRgb(232, 232, 232) : Color.FromRgb(28, 28, 28)), Child = row };
    }

    Control WindowPreview(WindowStyle style)
    {
        var panel = new StatusPanel { IsHitTestVisible = false };
        panel.Update(style, light, SampleCards(), Loc.F("last_update", "23:20:05"));
        Color w1 = light ? Color.FromRgb(185, 210, 246) : Color.FromRgb(22, 38, 74);
        Color w2 = light ? Color.FromRgb(242, 212, 230) : Color.FromRgb(67, 32, 106);
        var gradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(w1, 0), new GradientStop(w2, 1) },
        };
        return new Border { Height = 186, CornerRadius = new CornerRadius(6), Background = gradient, Padding = new Thickness(8), Child = new Viewbox { Stretch = Stretch.Uniform, Child = panel } };
    }

    static List<CardModel> SampleCards() => new()
    {
        new CardModel { Kind = Loc.T("kind_mouse"), Name = "Scyrox V8", Glyph = DeviceKind.Mouse, ColorIndex = 0, Percent = 82, Live = true, Detail = Loc.T("connected"), Eta = Loc.F("eta_h", 38), Points = Curve(100, 82) },
        new CardModel { Kind = Loc.T("kind_headset"), Name = "Corsair Void Elite", Glyph = DeviceKind.Headset, ColorIndex = 1, Percent = 14, Live = true, Low = true, Detail = Loc.T("low"), Eta = Loc.F("eta_hm", 1, 40), Points = Curve(90, 14) },
    };

    static List<HistoryPoint> Curve(int from, int to)
    {
        var l = new List<HistoryPoint>();
        DateTime start = DateTime.Now.AddHours(-23);
        for (int i = 0; i <= 23; i++) l.Add(new HistoryPoint(start.AddHours(i), (int)Math.Round(from + (to - from) * i / 23.0), false));
        return l;
    }

    // ---- Devices ----

    Control Devices(Ink ink)
    {
        var list = new StackPanel { Spacing = 8 };
        if (monitor.Known.All.Count == 0) list.Children.Add(Note(Loc.T("dev_empty"), ink));
        foreach (DeviceEntry e in monitor.Known.All.ToList()) list.Children.Add(DeviceRow(e, ink));
        return list;
    }

    Control DeviceRow(DeviceEntry e, Ink ink)
    {
        DeviceState? st = monitor.All.FirstOrDefault(d => d.Id == e.Id);
        int colorIndex = st?.ColorIndex ?? Math.Max(0, monitor.Known.IndexOf(e.Id));

        var swatch = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = Draw.B(Palette.Device(colorIndex, light)),
            Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
        };
        ToolTip.SetTip(swatch, Loc.T("dev_color"));
        swatch.PointerReleased += (s, a) =>
        {
            monitor.Change(e.Id, x => x.Color = (colorIndex + 1) % Palette.Count);
            Notify(true);
        };

        var alias = new TextBox { Text = e.Alias, Watermark = e.Name, MinWidth = 200, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(alias, Loc.T("dev_name_hint"));
        alias.TextChanged += (s, a) =>
        {
            monitor.Change(e.Id, x => x.Alias = alias.Text?.Trim() ?? "");
            Notify(false);
        };

        Button Small(string text, string tip, Action act)
        {
            var b = new Button { Content = text, Padding = new Thickness(10, 4), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(b, tip);
            b.Click += (s, a) => { act(); Notify(true); };
            return b;
        }

        var hide = new CheckBox { Content = Loc.T("dev_hide"), IsChecked = e.Hidden, Foreground = Draw.B(ink.Text), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        hide.IsCheckedChanged += (s, a) => { monitor.Change(e.Id, x => x.Hidden = hide.IsChecked == true); Notify(false); };

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto,Auto") };
        void Put(Control c, int col) { Grid.SetColumn(c, col); top.Children.Add(c); }
        Put(swatch, 0);
        Put(alias, 1);
        Put(Small("↑", Loc.T("dev_up"), () => monitor.Move(e.Id, -1)), 2);
        Put(Small("↓", Loc.T("dev_down"), () => monitor.Move(e.Id, +1)), 3);
        Put(hide, 4);
        Put(Small(Loc.T("dev_forget"), Loc.T("dev_forget"), () => monitor.Forget(e.Id)), 5);

        string state = st != null ? st.ShortText() : "";
        var info = new TextBlock
        {
            Text = Loc.F("dev_info", Glyphs.KindLabel(e.Kind) + " · " + e.Name, state),
            FontSize = 11.3, Foreground = Draw.B(ink.Sub), Margin = new Thickness(34, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var health = new TextBlock
        {
            Text = monitor.HealthText(e.Id), FontSize = 11.3, Foreground = Draw.B(ink.Text), Margin = new Thickness(34, 2, 0, 0), TextWrapping = TextWrapping.Wrap,
        };
        return new Border
        {
            Background = Draw.B(ink.CardBg), BorderBrush = Draw.B(ink.CardLine), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10),
            Child = new StackPanel { Children = { top, info, health } },
        };
    }

    // ---- Alerts ----

    Control Alerts(Ink ink)
    {
        var label = new TextBlock { Text = Loc.F("alert_threshold", settings.LowThreshold), FontSize = 13.3, Foreground = Draw.B(ink.Text) };
        var slider = new Slider { Minimum = 5, Maximum = 50, TickFrequency = 5, IsSnapToTickEnabled = true, Value = settings.LowThreshold, Width = 380, HorizontalAlignment = HorizontalAlignment.Left };
        slider.ValueChanged += (s, e) =>
        {
            settings.LowThreshold = (int)Math.Round(slider.Value);
            label.Text = Loc.F("alert_threshold", settings.LowThreshold);
            Notify(false);
        };
        var test = new Button { Content = Loc.T("alert_test"), Margin = new Thickness(0, 8, 0, 0) };
        test.Click += (s, e) => TestAlert?.Invoke();
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                label, slider,
                Check("alert_full", settings.NotifyFull, ink, v => settings.NotifyFull = v),
                Check("alert_sound", settings.Sound, ink, v => settings.Sound = v),
                test,
            },
        };
    }

    // ---- System monitor ----

    Control SystemTab(Ink ink)
    {
        var metric = new ComboBox
        {
            ItemsSource = MetricKeys.Select(Loc.T).ToList(),
            SelectedIndex = (int)settings.TrayMetric,
            Width = 280,
        };
        metric.SelectionChanged += (s, e) =>
        {
            if (metric.SelectedIndex < 0) return;
            settings.TrayMetric = (SystemMetric)metric.SelectedIndex;
            Notify(false);
        };
        sysNow = new TextBlock { Text = SystemText(lastSys), FontSize = 12.5, Foreground = Draw.B(ink.Text), TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace") };
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Check("sys_show", settings.ShowSystem, ink, v => settings.ShowSystem = v),
                new TextBlock { Text = Loc.T("sys_tray"), FontSize = 13.3, Foreground = Draw.B(ink.Text), Margin = new Thickness(0, 6, 0, 0) },
                metric,
                Section("sys_now", ink),
                new Border { Background = Draw.B(ink.CardBg), BorderBrush = Draw.B(ink.CardLine), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10), Child = sysNow },
                Note(Loc.T("sys_note"), ink),
            },
        };
    }

    static string SystemText(SystemSnapshot? s)
    {
        if (s == null) return Loc.T("reading");
        string na = Loc.T("sys_na");
        string P(double? v) => v.HasValue ? $"{v:0} %" : na;
        string C(double? v) => v.HasValue ? $"{v:0} °C" : na;
        return $"{Loc.T("sys_cpu"),-10} {P(s.CpuPercent)}\n" +
               $"{Loc.T("sys_ram"),-10} {P(s.RamPercent)}  ({s.RamUsedGb:0.0} / {s.RamTotalGb:0.0} GB)\n" +
               $"{Loc.T("sys_disk"),-10} {P(s.DiskPercent)}  ({s.DiskUsedGb:0} / {s.DiskTotalGb:0} GB)\n" +
               $"{Loc.T("sys_gpu"),-10} {P(s.GpuPercent)}  {s.GpuName}\n" +
               $"{Loc.T("sys_gputemp"),-10} {C(s.GpuTempC)}\n" +
               $"{Loc.T("sys_cputemp"),-10} {C(s.CpuTempC)}{(s.CpuTempSource.Length > 0 ? "  (" + s.CpuTempSource + ")" : "")}";
    }

    // ---- General ----

    Control General(Ink ink)
    {
        var language = new ComboBox { ItemsSource = new[] { "English", "Español" }, SelectedIndex = (int)settings.Language, Width = 200 };
        language.SelectionChanged += (s, e) =>
        {
            if (language.SelectedIndex < 0 || (Lang)language.SelectedIndex == settings.Language) return;
            settings.Language = (Lang)language.SelectedIndex;
            Loc.Current = settings.Language;
            Notify(true);
        };

        var startup = new CheckBox { Content = Loc.T("gen_startup"), IsChecked = Autostart.IsEnabled(), Foreground = Draw.B(ink.Text) };
        startup.IsCheckedChanged += (s, e) => StartupToggled?.Invoke(startup.IsChecked == true);

        var panel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = Loc.T("gen_language"), FontSize = 13.3, Foreground = Draw.B(ink.Text) },
                language,
                startup,
            },
        };

        if (OperatingSystem.IsWindows())
        {
            panel.Children.Add(Check("gen_hotkey", settings.Hotkey, ink, v => settings.Hotkey = v, rebuild: true));
            string? combo = settings.Hotkey ? HotkeyState?.Invoke() : null;
            if (combo != null) panel.Children.Add(Note(combo.Length > 0 ? Loc.F("gen_hotkey_combo", combo) : Loc.T("gen_hotkey_busy"), ink));
        }
        else panel.Children.Add(Note(Loc.T("gen_hotkey_other"), ink));

        panel.Children.Add(Check("gen_widget", settings.Widget, ink, v => settings.Widget = v));
        panel.Children.Add(Check("gen_widget_top", settings.WidgetOnTop, ink, v => settings.WidgetOnTop = v));
        panel.Children.Add(Note(Loc.T("gen_widget_hint"), ink));

        var status = new TextBlock { FontSize = 11.3, Foreground = Draw.B(ink.Sub), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var check = new Button { Content = Loc.T("gen_check_now") };
        check.Click += async (s, e) =>
        {
            if (CheckNow == null) return;
            status.Text = Loc.T("upd_checking");
            status.Text = await CheckNow();
        };
        panel.Children.Add(Check("gen_updates", settings.CheckUpdates, ink, v => settings.CheckUpdates = v));
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { check, status } });
        panel.Children.Add(Note(Loc.F("gen_version", Updater.CurrentText), ink));
        return panel;
    }
}

// Check mark of the selected option.
public sealed class CheckMark : Control
{
    public Color Color { get; set; } = Colors.White;

    public override void Render(DrawingContext dc)
    {
        double s = Bounds.Width / 16;
        IPen pen = Draw.P(Color, 1.6 * s);
        dc.DrawLine(pen, new Point(4.5 * s, 8.2 * s), new Point(7 * s, 10.6 * s));
        dc.DrawLine(pen, new Point(7 * s, 10.6 * s), new Point(11.5 * s, 5.6 * s));
    }
}
