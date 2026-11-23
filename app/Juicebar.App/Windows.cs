using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Juicebar.Core;

namespace Juicebar.App;

// Drawn button (gear or close) with a hover highlight.
public sealed class IconButton : Control
{
    public enum Shape { Gear, Close }

    readonly Shape shape;
    bool hover;
    public Color Color { get; set; } = Colors.Gray;
    public bool Light { get; set; }
    public event Action? Click;

    public IconButton(Shape s)
    {
        shape = s;
        Width = 26;
        Height = 26;
        Margin = new Thickness(4, 0, 0, 0);
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    protected override void OnPointerEntered(PointerEventArgs e) { hover = true; InvalidateVisual(); base.OnPointerEntered(e); }
    protected override void OnPointerExited(PointerEventArgs e) { hover = false; InvalidateVisual(); base.OnPointerExited(e); }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left) Click?.Invoke();
        base.OnPointerReleased(e);
    }

    public override void Render(DrawingContext dc)
    {
        var all = new Rect(Bounds.Size);
        dc.FillRectangle(Brushes.Transparent, all);   // so the whole surface receives the mouse
        if (hover) dc.DrawRectangle(Draw.B(Light ? Color.FromArgb(30, 0, 0, 0) : Color.FromArgb(40, 255, 255, 255)), null, all, 5, 5);
        double cx = all.Width / 2, cy = all.Height / 2;
        if (shape == Shape.Close)
        {
            IPen pen = Draw.P(Color, 1.4);
            dc.DrawLine(pen, new Point(9, 9), new Point(all.Width - 9, all.Height - 9));
            dc.DrawLine(pen, new Point(all.Width - 9, 9), new Point(9, all.Height - 9));
            return;
        }
        IPen tooth = Draw.P(Color, 2.4, round: false), ring = Draw.P(Color, 1.4);
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4, c = Math.Cos(a), s = Math.Sin(a);
            dc.DrawLine(tooth, new Point(cx + c * 5.2, cy + s * 5.2), new Point(cx + c * 7.4, cy + s * 7.4));
        }
        dc.DrawEllipse(null, ring, new Point(cx, cy), 4.9, 4.9);
        dc.DrawEllipse(null, ring, new Point(cx, cy), 1.9, 1.9);
    }
}

// Details panel: title, gear, close, cards, system block and last reading time.
// Also used (without interaction) as a thumbnail in Settings.
public sealed class StatusPanel : Border
{
    readonly TextBlock title = new() { FontSize = 15.3, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    readonly IconButton gear = new(IconButton.Shape.Gear), close = new(IconButton.Shape.Close);
    readonly CardsView cards = new();
    readonly SystemView system = new() { Margin = new Thickness(0, 8, 0, 0) };
    readonly TextBlock footer = new() { FontSize = 11.3, Margin = new Thickness(6, 10, 0, 0) };

    public event Action? GearClicked, CloseClicked;

    public StatusPanel()
    {
        Width = 320;
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(12, 10, 12, 12);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(6, 0, -2, 8) };
        Grid.SetColumn(gear, 1);
        Grid.SetColumn(close, 2);
        header.Children.Add(title);
        header.Children.Add(gear);
        header.Children.Add(close);
        Child = new StackPanel { Children = { header, cards, system, footer } };
        gear.Click += () => GearClicked?.Invoke();
        close.Click += () => CloseClicked?.Invoke();
    }

    public void Update(WindowStyle style, bool light, IReadOnlyList<CardModel> list, string time)
    {
        var ink = Ink.For(light);
        Background = Draw.B(ink.Bg);
        BorderBrush = Draw.B(ink.Border);
        title.Text = Loc.T("title_batteries");
        title.Foreground = Draw.B(ink.Text);
        footer.Foreground = Draw.B(ink.Sub);
        footer.Text = time.Length > 0 ? time : Loc.T("reading_devices");
        foreach (IconButton b in new[] { gear, close })
        {
            b.Color = ink.Sub;
            b.Light = light;
            b.InvalidateVisual();
        }
        cards.WinStyle = style;
        cards.Light = light;
        cards.Cards = list;
        cards.Refresh();
        system.Light = light;
        system.Refresh();
    }

    public void SetSystem(SystemSnapshot? snapshot)
    {
        system.Snapshot = snapshot;
        system.Refresh();
    }
}

// Base of the floating windows: no frame, always on top, with a shadow and rounded corners.
// Esc or close only hide them. Without transparency support they are drawn square and without a shadow.
public class FlyoutWindow : Window
{
    Border? frame, panel;

    public FlyoutWindow()
    {
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        KeyDown += (s, e) => { if (e.Key == Key.Escape) Hide(); };
        Closing += (s, e) =>
        {
            if (e.CloseReason == WindowCloseReason.WindowClosing && !e.IsProgrammatic)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    protected void SetPanel(Border p)
    {
        panel = p;
        p.BoxShadow = BoxShadows.Parse("0 6 20 0 #59000000");
        frame = new Border { Padding = new Thickness(16), Background = Brushes.Transparent, Child = p };
        Content = frame;
        ApplyTransparencyFallback();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ApplyTransparencyFallback();
    }

    void ApplyTransparencyFallback()
    {
        if (frame == null || panel == null || !IsVisible || ActualTransparencyLevel != WindowTransparencyLevel.None) return;
        frame.Padding = new Thickness(0);
        panel.CornerRadius = new CornerRadius(0);
        panel.BoxShadow = default;
    }

    protected Size CurrentSize()
    {
        if (IsVisible && ClientSize.Width > 0) return ClientSize;
        if (Content is Control c) { c.Measure(Size.Infinity); return c.DesiredSize; }
        return new Size(320, 300);
    }

    // Next to the tray: bottom right (Windows and most panels) or top right when the
    // system bar is at the top (macOS, GNOME).
    public void PlaceNearTray()
    {
        Screen? scr = Screens.Primary;
        if (scr == null) return;
        PixelRect wa = scr.WorkingArea, b = scr.Bounds;
        Size sz = CurrentSize();
        int w = (int)Math.Ceiling(sz.Width * scr.Scaling), h = (int)Math.Ceiling(sz.Height * scr.Scaling);
        bool top = wa.Y > b.Y || OperatingSystem.IsMacOS();
        Position = new PixelPoint(wa.Right - w, top ? wa.Y : wa.Bottom - h);
    }

    public void PlaceCenter()
    {
        Screen? scr = Screens.Primary;
        if (scr == null) return;
        PixelRect wa = scr.WorkingArea;
        Size sz = CurrentSize();
        int w = (int)Math.Ceiling(sz.Width * scr.Scaling), h = (int)Math.Ceiling(sz.Height * scr.Scaling);
        Position = new PixelPoint(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2);
    }
}

// Details window next to the tray.
public sealed class StatusWindow : FlyoutWindow
{
    readonly StatusPanel panel = new();
    public event Action? SettingsRequested;

    public StatusWindow()
    {
        Title = "Juicebar";
        SetPanel(panel);
        panel.CloseClicked += Hide;
        panel.GearClicked += () => SettingsRequested?.Invoke();
    }

    public void Update(WindowStyle style, bool light, IReadOnlyList<CardModel> cards, string time) =>
        panel.Update(style, light, cards, time);

    public void SetSystem(SystemSnapshot? snapshot) => panel.SetSystem(snapshot);

    public void ShowNearTray()
    {
        PlaceNearTray();
        Show();
        Activate();
        Dispatcher.UIThread.Post(PlaceNearTray, DispatcherPriority.Loaded);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ClientSizeProperty && IsVisible) PlaceNearTray();   // style or device count changed
    }
}

// Juicebar's own alert next to the tray (same on the three systems). Closes after 8 s or on click.
public sealed class ToastWindow : FlyoutWindow
{
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(8) };
    bool light;

    public ToastWindow()
    {
        Title = "Juicebar";
        ShowActivated = false;
        timer.Tick += (s, e) => { timer.Stop(); Hide(); };
        PointerReleased += (s, e) => Hide();
    }

    public void SetLight(bool l) => light = l;

    // 'above': the details window, when open; the alert goes right on top of it instead of behind it.
    public void Notify(string title, string body, Color titleColor, Window? above = null)
    {
        var ink = Ink.For(light);
        SetPanel(new Border
        {
            Width = 300,
            Background = Draw.B(ink.Bg),
            BorderBrush = Draw.B(ink.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Draw.B(titleColor) },
                    new TextBlock { Text = body, FontSize = 13, Foreground = Draw.B(ink.Text), TextWrapping = TextWrapping.Wrap },
                },
            },
        });
        void Place()
        {
            PlaceNearTray();
            if (above == null || !above.IsVisible || Screens.Primary is not Screen scr) return;
            int h = (int)Math.Ceiling(CurrentSize().Height * scr.Scaling);
            Position = new PixelPoint(Position.X, above.Position.Y - h + 16);
        }
        Place();
        Show();
        Dispatcher.UIThread.Post(Place, DispatcherPriority.Loaded);
        timer.Stop();
        timer.Start();
    }
}

// Diagnostics report: what the system sees and what each source answers.
public sealed class DiagWindow : Window
{
    readonly TextBox box = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        FontFamily = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace"),
        FontSize = 12.5,
    };

    public DiagWindow()
    {
        Title = Loc.T("diag_title");
        try { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Juicebar/Assets/juicebar.png"))); }
        catch (Exception ex) { Log.Write("window icon: " + ex.Message); }
        Width = 960;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        box.Text = Loc.T("diag_analyzing");
        var again = new Button { Content = Loc.T("diag_again"), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        again.Click += (s, e) => Run();
        DockPanel.SetDock(again, Dock.Bottom);
        Content = new DockPanel { Children = { again, box } };
        KeyDown += (s, e) => { if (e.Key == Key.F5) Run(); };
        Opened += (s, e) => Run();
    }

    void Run()
    {
        box.Text = Loc.T("diag_analyzing");
        Task.Run(() =>
        {
            string report;
            try { report = DeviceMonitor.Diagnose(); }
            catch (Exception ex) { report = "Error: " + ex; }
            Dispatcher.UIThread.Post(() => box.Text = report);
        });
    }
}
