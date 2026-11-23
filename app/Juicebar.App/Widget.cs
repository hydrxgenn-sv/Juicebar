using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Juicebar.Core;

namespace Juicebar.App;

// Compact list for the desktop widget: glyph, name, percentage and a thin bar per device.
public sealed class WidgetView : Control
{
    const double W = 230, RowH = 32;
    public IReadOnlyList<CardModel> Cards { get; set; } = Array.Empty<CardModel>();
    public bool Light { get; set; }

    public void Refresh()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(W, Math.Max(1, Cards.Count) * RowH);

    public override void Render(DrawingContext dc)
    {
        var ink = Ink.For(Light);
        dc.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));   // whole surface draggable
        if (Cards.Count == 0)
        {
            Draw.Text(dc, Loc.T("no_devices"), 11.3, FontWeight.Normal, ink.Sub, new Rect(0, 8, W, 16));
            return;
        }
        double y = 0;
        foreach (CardModel c in Cards)
        {
            Color col = c.Percent < 0 || !c.Live ? ink.Sub : c.Low ? Palette.Low(Light) : c.Charging ? Palette.Charge(Light) : Palette.Device(c.ColorIndex, Light);
            dc.DrawEllipse(Draw.B(Palette.Alpha(col, (byte)(Light ? 34 : 46))), null, new Point(11, y + 13), 11, 11);
            Glyphs.Paint(dc, c.Glyph, new Rect(4, y + 6, 14, 14), col, 2.2);
            Draw.Text(dc, c.Name, 11.3, FontWeight.Normal, ink.Text, new Rect(30, y + 5, W - 30 - 48, 16));
            Draw.Text(dc, c.Percent >= 0 ? c.Percent + "%" : "--", 13.3, FontWeight.SemiBold,
                      c.Percent >= 0 && c.Live ? (c.Low ? Palette.Low(Light) : ink.Text) : ink.Sub, new Rect(W - 48, y + 3, 48, 18), TextAlignment.Right);
            Draw.Bar(dc, new Rect(30, y + 23, W - 30, 3), c.Percent / 100.0, ink.Track, col);
            y += RowH;
        }
    }
}

// Desktop widget: drag to move (the position is remembered), double-click to open the details window.
public sealed class WidgetWindow : FlyoutWindow
{
    readonly Settings settings;
    readonly WidgetView view = new();
    readonly Border panel;
    readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public event Action? OpenRequested;

    public WidgetWindow(Settings s)
    {
        settings = s;
        Title = Loc.T("widget_title");
        ShowActivated = false;
        panel = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(12, 10, 12, 6), Child = view };
        SetPanel(panel);
        panel.PointerPressed += (sender, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount >= 2) OpenRequested?.Invoke();
            else BeginMoveDrag(e);
        };
        PositionChanged += (sender, e) => { saveTimer.Stop(); saveTimer.Start(); };
        saveTimer.Tick += (sender, e) =>
        {
            saveTimer.Stop();
            if (!IsVisible) return;
            settings.WidgetX = Position.X;
            settings.WidgetY = Position.Y;
            settings.Save();
        };
    }

    public void Update(IReadOnlyList<CardModel> cards, bool light)
    {
        var ink = Ink.For(light);
        panel.Background = Draw.B(Palette.Alpha(ink.Bg, 235));
        panel.BorderBrush = Draw.B(ink.Border);
        view.Cards = cards;
        view.Light = light;
        view.Refresh();
        Topmost = settings.WidgetOnTop;
    }

    // At the remembered position if it is still on a screen; otherwise at the top right.
    public void ShowAt()
    {
        var p = new PixelPoint(settings.WidgetX, settings.WidgetY);
        bool valid = settings.WidgetX != int.MinValue && Screens.All.Any(sc => sc.Bounds.Contains(p));
        if (valid) Position = p;
        else if (Screens.Primary is Screen scr)
        {
            Size sz = CurrentSize();
            Position = new PixelPoint(scr.WorkingArea.Right - (int)(sz.Width * scr.Scaling) - 24, scr.WorkingArea.Y + 24);
        }
        Show();
    }
}
