using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Juicebar.Core;

namespace Juicebar.App;

// Per-device colors (by position) and state colors, each in a version for dark and light backgrounds.
static class Palette
{
    public const int Count = 6;

    static readonly (Color Dark, Color Light)[] Devices =
    {
        (Color.FromRgb(96, 205, 255), Color.FromRgb(0, 103, 192)),    // blue
        (Color.FromRgb(199, 155, 255), Color.FromRgb(122, 63, 208)),  // violet
        (Color.FromRgb(255, 190, 92), Color.FromRgb(176, 100, 0)),    // amber
        (Color.FromRgb(72, 220, 190), Color.FromRgb(0, 128, 110)),    // teal
        (Color.FromRgb(255, 140, 190), Color.FromRgb(190, 40, 110)),  // pink
        (Color.FromRgb(180, 230, 90), Color.FromRgb(80, 130, 0)),     // lime
    };

    public static Color Device(int index, bool light)
    {
        var d = Devices[((index % Count) + Count) % Count];
        return light ? d.Light : d.Dark;
    }

    public static Color Accent(bool light) => Device(0, light);
    public static Color Low(bool light) => light ? Color.FromRgb(214, 43, 43) : Color.FromRgb(255, 90, 90);
    public static Color Charge(bool light) => light ? Color.FromRgb(25, 153, 74) : Color.FromRgb(76, 217, 100);
    public static Color Warn(bool light) => Device(2, light);
    public static Color Alpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    // Color for a usage or temperature level: normal, warning, critical.
    public static Color Level(double value, double warnAt, double critAt, bool light) =>
        value >= critAt ? Low(light) : value >= warnAt ? Warn(light) : Device(3, light);
}

// Interface colors for the current theme.
sealed class Ink
{
    public bool Light;
    public Color Bg, Border, Text, Sub, CardBg, CardLine, Track;

    public static Ink For(bool light) => light
        ? new Ink
        {
            Light = true, Bg = Color.FromRgb(243, 243, 243), Border = Color.FromRgb(214, 214, 214), Text = Color.FromRgb(26, 26, 26),
            Sub = Color.FromRgb(104, 104, 104), CardBg = Colors.White, CardLine = Color.FromRgb(230, 230, 230), Track = Color.FromRgb(228, 228, 228),
        }
        : new Ink
        {
            Light = false, Bg = Color.FromRgb(32, 32, 32), Border = Color.FromRgb(64, 64, 64), Text = Colors.White,
            Sub = Color.FromRgb(166, 166, 166), CardBg = Color.FromRgb(44, 44, 44), CardLine = Color.FromRgb(58, 58, 58), Track = Color.FromRgb(70, 70, 70),
        };
}

static class Draw
{
    public static IBrush B(Color c) => new ImmutableSolidColorBrush(c);

    public static IPen P(Color c, double width, bool round = true) =>
        new Pen(B(c), width) { LineCap = round ? PenLineCap.Round : PenLineCap.Flat, LineJoin = PenLineJoin.Round };

    public static FormattedText Text(string s, double size, FontWeight weight, Color c) =>
        new(s ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, B(c));

    // One line of text inside a rectangle, with "..." when it does not fit.
    public static void Text(DrawingContext dc, string s, double size, FontWeight weight, Color c, Rect r,
                            TextAlignment align = TextAlignment.Left, bool vcenter = false)
    {
        FormattedText ft = Text(s, size, weight, c);
        ft.MaxTextWidth = Math.Max(1, r.Width);
        ft.MaxLineCount = 1;
        ft.Trimming = TextTrimming.CharacterEllipsis;
        ft.TextAlignment = align;
        double y = vcenter ? r.Y + (r.Height - ft.Height) / 2 : r.Y;
        dc.DrawText(ft, new Point(r.X, y));
    }

    // Arc from the top (12 o'clock) clockwise, 'fraction' of a full turn.
    public static void Arc(DrawingContext dc, Point c, double r, double fraction, IPen pen)
    {
        if (fraction <= 0) return;
        if (fraction >= 0.999) { dc.DrawEllipse(null, pen, c, r, r); return; }
        double a0 = -Math.PI / 2, a1 = a0 + 2 * Math.PI * fraction;
        var p0 = new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0));
        var p1 = new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1));
        var g = new StreamGeometry();
        using (StreamGeometryContext ctx = g.Open())
        {
            ctx.BeginFigure(p0, false);
            ctx.ArcTo(p1, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        dc.DrawGeometry(null, pen, g);
    }

    public static void Bar(DrawingContext dc, Rect t, double fraction, Color track, Color fill)
    {
        dc.DrawRectangle(B(track), null, t, t.Height / 2, t.Height / 2);
        if (fraction <= 0) return;
        double fw = Math.Max(t.Height, t.Width * Math.Min(fraction, 1));
        dc.DrawRectangle(B(fill), null, new Rect(t.X, t.Y, fw, t.Height), t.Height / 2, t.Height / 2);
    }
}

// 24-unit stroke glyphs per device kind.
static class Glyphs
{
    static readonly Geometry HeadsetBand = Geometry.Parse("M4.5,14 V12 A7.5,7.5 0 0 1 19.5,12 V14");
    static readonly Geometry Pad = Geometry.Parse("M7,8 H17 A4,4 0 0 1 21,12 L21.5,15.5 A2.5,2.5 0 0 1 17.3,17.2 L15.5,15 H8.5 L6.7,17.2 A2.5,2.5 0 0 1 2.5,15.5 L3,12 A4,4 0 0 1 7,8 Z");

    public static string KindLabel(DeviceKind k) => k switch
    {
        DeviceKind.Mouse => Loc.T("kind_mouse"),
        DeviceKind.Headset => Loc.T("kind_headset"),
        DeviceKind.Keyboard => Loc.T("kind_keyboard"),
        DeviceKind.Controller => Loc.T("kind_controller"),
        _ => Loc.T("kind_other"),
    };

    public static void Paint(DrawingContext dc, DeviceKind kind, Rect box, Color color, double stroke = 1.9)
    {
        double s = box.Width / 24.0;
        IPen pen = Draw.P(color, stroke);
        using (dc.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation(box.X, box.Y)))
        {
            switch (kind)
            {
                case DeviceKind.Mouse:
                    dc.DrawRectangle(null, pen, new Rect(6.5, 2.5, 11, 19), 5.5, 5.5);
                    dc.DrawLine(pen, new Point(12, 2.5), new Point(12, 9));
                    break;
                case DeviceKind.Headset:
                    dc.DrawGeometry(null, pen, HeadsetBand);
                    dc.DrawRectangle(null, pen, new Rect(3, 14, 4, 7), 1.6, 1.6);
                    dc.DrawRectangle(null, pen, new Rect(17, 14, 4, 7), 1.6, 1.6);
                    break;
                case DeviceKind.Keyboard:
                    dc.DrawRectangle(null, pen, new Rect(2.5, 6.5, 19, 11), 2, 2);
                    foreach (double x in new[] { 6.5, 9.5, 12.5, 15.5, 18 }) dc.DrawLine(pen, new Point(x, 10), new Point(x + 0.01, 10));
                    dc.DrawLine(pen, new Point(8, 14), new Point(16, 14));
                    break;
                case DeviceKind.Controller:
                    dc.DrawGeometry(null, pen, Pad);
                    dc.DrawLine(pen, new Point(7, 10.5), new Point(7, 13.5));
                    dc.DrawLine(pen, new Point(5.5, 12), new Point(8.5, 12));
                    dc.DrawLine(pen, new Point(16, 11), new Point(16.01, 11));
                    dc.DrawLine(pen, new Point(18, 13), new Point(18.01, 13));
                    break;
                default:
                    dc.DrawRectangle(null, pen, new Rect(3, 7.5, 16, 9), 2, 2);
                    dc.DrawLine(pen, new Point(21, 10.5), new Point(21, 13.5));
                    break;
            }
        }
    }
}
