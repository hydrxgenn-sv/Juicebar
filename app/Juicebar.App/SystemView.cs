using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Juicebar.Core;

namespace Juicebar.App;

// "System" block of the details window: one row per available value (usage bars and temperatures).
public sealed class SystemView : Control
{
    const double W = CardsView.W, RowH = 22, TitleH = 22;

    public SystemSnapshot? Snapshot { get; set; }
    public bool Light { get; set; }

    readonly record struct Row(string Label, double Fraction, string Value, Color Color);

    List<Row> Rows()
    {
        var rows = new List<Row>();
        SystemSnapshot? s = Snapshot;
        if (s == null) return rows;
        CultureInfo ci = CultureInfo.CurrentCulture;
        if (s.CpuPercent is double cpu) rows.Add(new Row(Loc.T("sys_cpu"), cpu / 100, $"{cpu:0} %", Palette.Level(cpu, 70, 90, Light)));
        if (s.RamPercent is double ram) rows.Add(new Row(Loc.T("sys_ram"), ram / 100, string.Format(ci, "{0:0.0} / {1:0} GB", s.RamUsedGb, s.RamTotalGb), Palette.Level(ram, 80, 92, Light)));
        if (s.DiskPercent is double disk) rows.Add(new Row(Loc.T("sys_disk"), disk / 100, string.Format(ci, "{0:0} / {1:0} GB", s.DiskUsedGb, s.DiskTotalGb), Palette.Level(disk, 85, 95, Light)));
        if (s.GpuPercent is double gpu) rows.Add(new Row(Loc.T("sys_gpu"), gpu / 100, $"{gpu:0} %", Palette.Level(gpu, 70, 90, Light)));
        if (s.CpuTempC is double ct) rows.Add(new Row(Loc.T("sys_cputemp"), ct / 100, $"{ct:0} °C", Palette.Level(ct, 70, 85, Light)));
        if (s.GpuTempC is double gt) rows.Add(new Row(Loc.T("sys_gputemp"), gt / 100, $"{gt:0} °C", Palette.Level(gt, 70, 85, Light)));
        return rows;
    }

    public void Refresh()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int n = Rows().Count;
        return new Size(W, n == 0 ? 0 : TitleH + n * RowH + 10);
    }

    public override void Render(DrawingContext dc)
    {
        List<Row> rows = Rows();
        if (rows.Count == 0) return;
        var ink = Ink.For(Light);
        var box = new Rect(0, 0, W, TitleH + rows.Count * RowH + 10);
        dc.DrawRectangle(Draw.B(ink.CardBg), new Pen(Draw.B(ink.CardLine), 1), box.Deflate(0.5), 7, 7);
        Draw.Text(dc, Loc.T("sys_title"), 11.3, FontWeight.SemiBold, ink.Sub, new Rect(14, 7, W - 28, 16));
        double y = TitleH + 4;
        foreach (Row r in rows)
        {
            Draw.Text(dc, r.Label, 11.3, FontWeight.Normal, ink.Text, new Rect(14, y, 70, 16));
            Draw.Bar(dc, new Rect(88, y + 6, W - 88 - 104, 6), r.Fraction, ink.Track, r.Color);
            Draw.Text(dc, r.Value, 11.3, FontWeight.SemiBold, ink.Text, new Rect(W - 100, y, 86, 16), TextAlignment.Right);
            y += RowH;
        }
    }
}
