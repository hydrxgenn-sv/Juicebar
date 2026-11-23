using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Juicebar.Core;

namespace Juicebar.App;

// What a card of the details window shows.
public sealed class CardModel
{
    public string Kind = "", Name = "", Detail = "", Eta = "";
    public int Percent = -1;
    public bool Live, Charging, Low;
    public DeviceKind Glyph;
    public int ColorIndex;
    public IReadOnlyList<HistoryPoint>? Points;
}

// Device cards in the chosen style (Glass, Rings, Segments or History).
// Sizes in DIPs at 96 dpi; Avalonia scales them to the screen.
public sealed class CardsView : Control
{
    public const double W = 296;
    public WindowStyle WinStyle { get; set; } = WindowStyle.Glass;
    public bool Light { get; set; }
    public IReadOnlyList<CardModel> Cards { get; set; } = Array.Empty<CardModel>();

    public static double HeightFor(WindowStyle st, int n)
    {
        if (n == 0) return 84;
        int rows = (n + 1) / 2;
        return st switch
        {
            WindowStyle.Rings => rows * 178 + (rows - 1) * 8,
            WindowStyle.Segments => n * 92 + (n - 1) * 8,
            WindowStyle.History => n * 114 + (n - 1) * 8,
            _ => n * 84 + (n - 1) * 8,
        };
    }

    public void Refresh()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(W, HeightFor(WinStyle, Cards.Count));

    public override void Render(DrawingContext dc)
    {
        var ink = Ink.For(Light);
        if (Cards.Count == 0) { Empty(dc, ink); return; }
        for (int i = 0; i < Cards.Count; i++)
        {
            switch (WinStyle)
            {
                case WindowStyle.Rings:
                    double cw = (W - 10) / 2;
                    Rings(dc, ink, new Rect(i % 2 * (cw + 10), i / 2 * 186, cw, 178), Cards[i]);
                    break;
                case WindowStyle.Segments:
                    Segments(dc, ink, new Rect(0, i * 100, W, 92), Cards[i]);
                    break;
                case WindowStyle.History:
                    HistoryCard(dc, ink, new Rect(0, i * 122, W, 114), Cards[i]);
                    break;
                default:
                    Glass(dc, ink, new Rect(0, i * 92, W, 84), Cards[i]);
                    break;
            }
        }
    }

    // ---- shared pieces ----

    Color State(CardModel c, Ink ink)
    {
        if (c.Percent < 0 || !c.Live) return ink.Sub;
        if (c.Low) return Palette.Low(Light);
        if (c.Charging) return Palette.Charge(Light);
        return Palette.Device(c.ColorIndex, Light);
    }

    Color Number(CardModel c, Ink ink) => c.Percent < 0 || !c.Live ? ink.Sub : c.Low ? Palette.Low(Light) : ink.Text;

    static void Box(DrawingContext dc, Ink ink, Rect r, double rad) =>
        dc.DrawRectangle(Draw.B(ink.CardBg), new Pen(Draw.B(ink.CardLine), 1), r.Deflate(0.5), rad, rad);

    static string Pct(CardModel c) => c.Percent >= 0 ? c.Percent + "%" : "--";

    void Empty(DrawingContext dc, Ink ink)
    {
        var r = new Rect(0, 0, W, 84);
        Box(dc, ink, r, 7);
        Glyphs.Paint(dc, DeviceKind.Other, new Rect(14, 18, 24, 24), ink.Sub);
        Draw.Text(dc, Loc.T("no_devices"), 14, FontWeight.SemiBold, ink.Text, new Rect(48, 16, W - 62, 20));
        Draw.Text(dc, Loc.T("no_devices_hint1"), 11.3, FontWeight.Normal, ink.Sub, new Rect(48, 38, W - 62, 16));
        Draw.Text(dc, Loc.T("no_devices_hint2"), 11.3, FontWeight.Normal, ink.Sub, new Rect(48, 55, W - 62, 16));
    }

    // ---- Glass: glyph, name, percentage, bar and state ----
    void Glass(DrawingContext dc, Ink ink, Rect r, CardModel c)
    {
        Color col = State(c, ink);
        Box(dc, ink, r, 7);
        double x = r.X + 14, w = r.Width - 28;
        dc.DrawEllipse(Draw.B(Palette.Alpha(col, (byte)(Light ? 34 : 46))), null, new Point(x + 17, r.Y + 27), 17, 17);
        Glyphs.Paint(dc, c.Glyph, new Rect(x + 7, r.Y + 17, 20, 20), col);

        double tx = x + 46, tw = w - 46 - 90;
        Draw.Text(dc, c.Kind, 11.3, FontWeight.Normal, ink.Sub, new Rect(tx, r.Y + 10, tw, 16));
        Draw.Text(dc, c.Name, 14, FontWeight.SemiBold, ink.Text, new Rect(tx, r.Y + 26, tw, 20));
        Draw.Text(dc, Pct(c), 26.7, FontWeight.SemiBold, Number(c, ink), new Rect(x + w - 100, r.Y + 9, 100, 36), TextAlignment.Right);

        Draw.Bar(dc, new Rect(x, r.Y + 54, w, 8), c.Percent / 100.0, ink.Track, col);
        Draw.Text(dc, c.Detail, 11.3, FontWeight.Normal, c.Low ? Palette.Low(Light) : ink.Sub, new Rect(x, r.Y + 64, w, 16));
    }

    // ---- Rings: big ring with the percentage in the middle ----
    void Rings(DrawingContext dc, Ink ink, Rect r, CardModel c)
    {
        Color col = State(c, ink);
        Box(dc, ink, r, 7);
        var center = new Point(r.X + r.Width / 2, r.Y + 14 + 48);
        dc.DrawEllipse(null, Draw.P(ink.Track, 9), center, 43.5, 43.5);
        if (c.Percent > 0) Draw.Arc(dc, center, 43.5, Math.Min(c.Percent, 100) / 100.0, Draw.P(col, 9));
        Draw.Text(dc, Pct(c), 22.7, FontWeight.SemiBold, Number(c, ink), new Rect(center.X - 48, center.Y - 48, 96, 96), TextAlignment.Center, vcenter: true);

        Draw.Text(dc, c.Kind, 10.7, FontWeight.Normal, ink.Sub, new Rect(r.X + 8, r.Y + 116, r.Width - 16, 15), TextAlignment.Center);
        Draw.Text(dc, c.Name, 13.3, FontWeight.SemiBold, ink.Text, new Rect(r.X + 8, r.Y + 131, r.Width - 16, 19), TextAlignment.Center);

        FormattedText chipText = Draw.Text(c.Detail, 10.7, FontWeight.Normal, col);
        double cw = Math.Min(chipText.Width + 16, r.Width - 16), ch = 20;
        var chip = new Rect(r.X + (r.Width - cw) / 2, r.Y + 152, cw, ch);
        dc.DrawRectangle(Draw.B(Palette.Alpha(col, (byte)(Light ? 30 : 45))), null, chip, ch / 2, ch / 2);
        Draw.Text(dc, c.Detail, 10.7, FontWeight.Normal, col, chip.Deflate(new Thickness(6, 0)), TextAlignment.Center, vcenter: true);
    }

    // ---- Segments: big number and a 12-LED bar ----
    void Segments(DrawingContext dc, Ink ink, Rect r, CardModel c)
    {
        Color col = State(c, ink);
        Box(dc, ink, r, 6);
        double x = r.X + 14, w = r.Width - 28;
        Draw.Text(dc, (c.Kind + " · " + c.Name).ToUpperInvariant(), 10, FontWeight.SemiBold, ink.Sub, new Rect(x, r.Y + 10, w * 0.58, 14));
        Draw.Text(dc, c.Detail, 10, FontWeight.SemiBold, c.Low ? Palette.Low(Light) : ink.Sub, new Rect(x + w * 0.58, r.Y + 10, w * 0.42, 14), TextAlignment.Right);

        FormattedText big = Draw.Text(c.Percent >= 0 ? c.Percent.ToString() : "--", 34.7, FontWeight.SemiBold, c.Percent >= 0 && c.Live ? col : ink.Sub);
        double y = r.Y + 20;
        dc.DrawText(big, new Point(x, y));
        if (c.Percent >= 0)
        {
            FormattedText small = Draw.Text("%", 16, FontWeight.Normal, ink.Sub);
            dc.DrawText(small, new Point(x + big.Width + 3, y + big.Baseline - small.Baseline));
        }

        const int n = 12;
        double gap = 3, sw = (w - (n - 1) * gap) / n, sy = r.Y + 68, sh = 12;
        int lit = c.Percent > 0 ? (int)Math.Round(Math.Min(c.Percent, 100) / 100.0 * n) : 0;
        if (c.Percent > 0 && lit == 0) lit = 1;
        for (int i = 0; i < n; i++)
        {
            var seg = new Rect(x + i * (sw + gap), sy, sw, sh);
            if (i < lit && !Light && c.Live)
                dc.DrawRectangle(Draw.B(Palette.Alpha(col, 55)), null, seg.Inflate(2), 4, 4);
            dc.DrawRectangle(Draw.B(i < lit ? col : ink.Track), null, seg, 2, 2);
        }
    }

    // ---- History: 24 h chart and time left ----
    void HistoryCard(DrawingContext dc, Ink ink, Rect r, CardModel c)
    {
        Color col = State(c, ink);
        Box(dc, ink, r, 7);
        double x = r.X + 14, w = r.Width - 28;
        Draw.Text(dc, c.Kind, 10.7, FontWeight.Normal, ink.Sub, new Rect(x, r.Y + 9, w - 90, 15));
        Draw.Text(dc, c.Name, 14, FontWeight.SemiBold, ink.Text, new Rect(x, r.Y + 23, w - 90, 20));
        Draw.Text(dc, Pct(c), 24, FontWeight.SemiBold, c.Percent >= 0 && c.Live ? col : ink.Sub, new Rect(x + w - 100, r.Y + 9, 100, 32), TextAlignment.Right);

        Spark(dc, ink, new Rect(x, r.Y + 50, w, 40), c.Points, col);

        Draw.Text(dc, Loc.T("last_24h"), 10.7, FontWeight.Normal, ink.Sub, new Rect(x, r.Y + 94, w / 2, 15));
        Draw.Text(dc, c.Eta, 10.7, FontWeight.Normal, c.Low ? Palette.Low(Light) : ink.Sub, new Rect(x + w * 0.3, r.Y + 94, w * 0.7, 15), TextAlignment.Right);
    }

    void Spark(DrawingContext dc, Ink ink, Rect a, IReadOnlyList<HistoryPoint>? points, Color col)
    {
        var dash = new Pen(Draw.B(Palette.Alpha(ink.Sub, 60)), 1) { DashStyle = new DashStyle(new double[] { 3, 4 }, 0) };
        dc.DrawLine(dash, new Point(a.X, a.Y + a.Height / 2), new Point(a.Right, a.Y + a.Height / 2));

        DateTime now = DateTime.Now, from = now - History.Span;
        var pts = new List<Point>();
        if (points != null)
            foreach (HistoryPoint p in points)
            {
                if (p.Time < from) continue;
                double px = a.X + a.Width * ((p.Time - from).TotalSeconds / History.Span.TotalSeconds);
                double py = a.Bottom - 2 - (a.Height - 4) * Math.Min(p.Percent, 100) / 100.0;
                pts.Add(new Point(px, py));
            }
        if (pts.Count > 0) pts.Add(new Point(a.Right, pts[^1].Y));   // the last reading still holds
        if (pts.Count < 3)
        {
            Draw.Text(dc, Loc.T("collecting"), 10.7, FontWeight.Normal, ink.Sub, a, TextAlignment.Center, vcenter: true);
            return;
        }

        var area = new StreamGeometry();
        using (StreamGeometryContext g = area.Open())
        {
            g.BeginFigure(pts[0], true);
            foreach (Point p in pts.Skip(1)) g.LineTo(p);
            g.LineTo(new Point(a.Right, a.Bottom));
            g.LineTo(new Point(pts[0].X, a.Bottom));
            g.EndFigure(true);
        }
        var line = new StreamGeometry();
        using (StreamGeometryContext g = line.Open())
        {
            g.BeginFigure(pts[0], false);
            foreach (Point p in pts.Skip(1)) g.LineTo(p);
            g.EndFigure(false);
        }
        dc.DrawGeometry(Draw.B(Palette.Alpha(col, (byte)(Light ? 36 : 44))), null, area);
        dc.DrawGeometry(null, Draw.P(col, 2), line);
    }
}
