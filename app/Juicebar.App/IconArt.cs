using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Juicebar.Core;

namespace Juicebar.App;

// A device's state as a tray icon draws it.
public readonly record struct IconInput(int Percent, bool Live, bool Charging, int ColorIndex, DeviceKind Kind);

// Tray icons drawn on a 16x16 grid scaled to the real size.
// Number: one icon per device. Cells, Rings and Glyphs: one icon with up to two devices.
// Red = below the low threshold, green = charging, dimmed = old reading or no data.
static class IconArt
{
    static Color Fg(bool light) => light ? Color.FromRgb(26, 26, 26) : Colors.White;
    static Color Dim(bool light) => Palette.Alpha(Fg(light), 140);
    static Color Track(bool light) => Palette.Alpha(Fg(light), (byte)(light ? 55 : 70));
    static bool Low(IconInput d) => d.Percent >= 0 && d.Live && !d.Charging && d.Percent < DeviceState.LowLevel;

    static Color State(IconInput d, bool light, bool deviceColor)
    {
        if (d.Percent < 0 || !d.Live) return Dim(light);
        if (d.Charging) return Palette.Charge(light);
        if (Low(d)) return Palette.Low(light);
        return deviceColor ? Palette.Device(d.ColorIndex, light) : Fg(light);
    }

    static Bitmap Render(int px, Action<DrawingContext, double> draw)
    {
        var bmp = new RenderTargetBitmap(new PixelSize(px, px), new Vector(96, 96));
        using (DrawingContext dc = bmp.CreateDrawingContext()) draw(dc, px / 16.0);
        return bmp;
    }

    // ---- Number: big number and a bar in the device's color ----
    public static Bitmap Number(IconInput d, bool light, int px)
    {
        bool known = d.Percent >= 0;
        Color num = !known || !d.Live ? Dim(light) : Low(d) ? Palette.Low(light) : Fg(light);
        return Render(px, (dc, k) => NumberArt(dc, k, px, known ? d.Percent.ToString() : "--", known,
                                               num, known ? d.Percent / 100.0 : 0, State(d, light, true), light));
    }

    // ---- System metric: same look, value in % or °C; the bar turns amber/red when high ----
    public static Bitmap Metric(SystemMetric m, double value, bool light, int px)
    {
        bool temp = m is SystemMetric.CpuTemp or SystemMetric.GpuTemp;
        Color bar = temp ? Palette.Level(value, 70, 85, light) : Palette.Level(value, 70, 90, light);
        return Render(px, (dc, k) => NumberArt(dc, k, px, Math.Round(value).ToString(CultureInfo.InvariantCulture), true,
                                               Fg(light), Math.Clamp(value / 100.0, 0, 1), bar, light));
    }

    static void NumberArt(DrawingContext dc, double k, int px, string text, bool known, Color num, double fraction, Color bar, bool light)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), 13 * k, Brushes.Black);
        Geometry? geo = ft.BuildGeometry(new Point(0, 0));
        if (geo != null && geo.Bounds.Width > 0)
        {
            // Centered by the glyphs' real box, so 1, 2 or 3 digits look the same.
            Rect bb = geo.Bounds;
            double sc = Math.Min(1, Math.Min(15 * k / bb.Width, known ? 10 * k / Math.Max(1, bb.Height) : 1));
            Matrix m = Matrix.CreateTranslation(-(bb.X + bb.Width / 2), -(bb.Y + bb.Height / 2)) *
                       Matrix.CreateScale(sc, sc) * Matrix.CreateTranslation(px / 2.0, (known ? 5.5 : 6) * k);
            using (dc.PushTransform(m)) dc.DrawGeometry(Draw.B(num), null, geo);
        }

        // Bar: rows 13-14, on whole pixels so it stays sharp.
        int bx = (int)Math.Round(k), by = (int)Math.Round(13 * k), bw = px - 2 * bx, bh = Math.Max(2, (int)Math.Round(2 * k));
        dc.FillRectangle(Draw.B(Track(light)), new Rect(bx, by, bw, bh));
        if (known && fraction > 0)
        {
            int fw = Math.Max(bh, (int)Math.Round(bw * Math.Min(fraction, 1)));
            dc.FillRectangle(Draw.B(bar), new Rect(bx, by, fw, bh));
        }
    }

    // ---- Cells, Rings or Glyphs with one or two devices (no devices: Number with "--") ----
    public static Bitmap Combined(IconStyle style, IReadOnlyList<IconInput> list, bool light, int px)
    {
        if (list.Count == 0 || style == IconStyle.Number)
            return Number(list.Count > 0 ? list[0] : new IconInput(-1, false, false, 0, DeviceKind.Other), light, px);
        IconInput a = list[0];
        IconInput? b = list.Count > 1 ? list[1] : null;
        return Render(px, (dc, k) =>
        {
            switch (style)
            {
                case IconStyle.Cells:
                {
                    int ki = Math.Max(1, px / 16), o = (px - 16 * ki) / 2;
                    if (b == null) Cell(dc, ki, o + 5 * ki, o, a, light);
                    else { Cell(dc, ki, o + 1 * ki, o, a, light); Cell(dc, ki, o + 9 * ki, o, b.Value, light); }
                    break;
                }
                case IconStyle.Rings:
                    Ring(dc, px, 6.6 * k, 1.8 * k, a, light);           // outer: first device
                    if (b != null) Ring(dc, px, 3.3 * k, 1.8 * k, b.Value, light);
                    break;
                default:
                    if (b == null) Glyph(dc, a, 4 * k, k, light);
                    else { Glyph(dc, a, 0, k, light); Glyph(dc, b.Value, 8 * k, k, light); }
                    break;
            }
        });
    }

    // 6x13 px cell with a 2 px terminal; 4x11 px fill from the bottom (whole pixels).
    static void Cell(DrawingContext dc, int k, int x, int y, IconInput d, bool light)
    {
        bool known = d.Percent >= 0;
        IBrush line = Draw.B(!known || !d.Live ? Dim(light) : Low(d) ? Palette.Low(light) : Fg(light));
        void Px(IBrush b, int x0, int y0, int w, int h) => dc.FillRectangle(b, new Rect(x + x0 * k, y + y0 * k, w * k, h * k));
        Px(line, 2, 1, 2, 1);    // terminal
        Px(line, 1, 2, 4, 1);    // top edge
        Px(line, 1, 14, 4, 1);   // bottom edge
        Px(line, 0, 3, 1, 11);   // left side
        Px(line, 5, 3, 1, 11);   // right side
        if (!known) return;
        int h11 = (int)Math.Round(11 * Math.Min(d.Percent, 100) / 100.0);
        if (d.Percent > 0 && h11 == 0) h11 = 1;
        Px(Draw.B(State(d, light, false)), 1, 3 + 11 - h11, 4, h11);
    }

    static void Ring(DrawingContext dc, int px, double r, double w, IconInput d, bool light)
    {
        var c = new Point(px / 2.0, px / 2.0);
        dc.DrawEllipse(null, Draw.P(Track(light), w), c, r, r);
        if (d.Percent > 0) Draw.Arc(dc, c, r, Math.Min(d.Percent, 100) / 100.0, Draw.P(State(d, light, true), w));
    }

    // Device silhouette on half the grid (8x16) moved by 'ox': faint base and a fill from the bottom.
    static void Glyph(DrawingContext dc, IconInput d, double ox, double k, bool light)
    {
        var (top, bottom) = d.Kind switch
        {
            DeviceKind.Headset => (5.7, 14.4),
            DeviceKind.Keyboard => (5.0, 12.0),
            DeviceKind.Controller => (6.0, 12.0),
            DeviceKind.Mouse => (1.5, 14.5),
            _ => (2.6, 13.0),
        };
        Shape(dc, d.Kind, ox, k, Palette.Alpha(Fg(light), (byte)(light ? 70 : 80)));
        if (d.Percent <= 0) return;
        double p = Math.Min(d.Percent, 100) / 100.0;
        double y = (bottom - (bottom - top) * p) * k;
        using (dc.PushClip(new Rect(ox, y, 8 * k, 16 * k - y)))
            Shape(dc, d.Kind, ox, k, State(d, light, true));
    }

    static void Shape(DrawingContext dc, DeviceKind kind, double ox, double k, Color c)
    {
        IBrush b = Draw.B(c);
        Rect R(double x, double y, double w, double h) => new(ox + x * k, y * k, w * k, h * k);
        switch (kind)
        {
            case DeviceKind.Mouse:
                dc.DrawRectangle(b, null, R(1.4, 1.5, 5.6, 13), 2.8 * k, 2.8 * k);
                break;
            case DeviceKind.Headset:
                dc.DrawGeometry(null, Draw.P(c, 1.3 * k, false), HeadsetBand(ox, k));
                dc.DrawRectangle(b, null, R(0.1, 9.6, 2.2, 4.8), 1 * k, 1 * k);
                dc.DrawRectangle(b, null, R(5.7, 9.6, 2.2, 4.8), 1 * k, 1 * k);
                break;
            case DeviceKind.Keyboard:
                dc.DrawRectangle(b, null, R(0.5, 5, 7, 7), 1.2 * k, 1.2 * k);
                break;
            case DeviceKind.Controller:
                dc.DrawRectangle(b, null, R(0.5, 6, 7, 6), 3 * k, 3 * k);
                break;
            default:
                dc.DrawRectangle(b, null, R(3, 2.6, 2, 1.2), 0, 0);
                dc.DrawRectangle(b, null, R(1, 3.6, 6, 9.4), 1 * k, 1 * k);
                break;
        }
    }

    static Geometry HeadsetBand(double ox, double k)
    {
        var g = new StreamGeometry();
        using (StreamGeometryContext ctx = g.Open())
        {
            ctx.BeginFigure(new Point(ox + 0.9 * k, 10 * k), false);
            ctx.LineTo(new Point(ox + 0.9 * k, 8.8 * k));
            ctx.ArcTo(new Point(ox + 7.1 * k, 8.8 * k), new Size(3.1 * k, 3.1 * k), 0, false, SweepDirection.Clockwise);
            ctx.LineTo(new Point(ox + 7.1 * k, 10 * k));
            ctx.EndFigure(false);
        }
        return g;
    }
}
