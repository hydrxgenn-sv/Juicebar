using System.Diagnostics;
using HidSharp;
using HidSharp.Reports;

namespace Juicebar.Core;

// A HID interface with what the sources need: usage pages and declared reports.
// On Windows each top-level collection is its own interface; on Linux (hidraw) and macOS one interface
// can hold several collections, so reports are matched by their ID.
public sealed class HidCollection
{
    public required HidDevice Device { get; init; }
    public ushort Vid { get; init; }
    public ushort Pid { get; init; }
    public string Product { get; init; } = "";
    public string Path => Device.DevicePath;
    public HashSet<ushort> UsagePages { get; } = new();
    public List<(byte Id, int Length)> Inputs { get; } = new();
    public List<(byte Id, int Length)> Outputs { get; } = new();
    public List<(byte Id, int Length)> Features { get; } = new();
    public int MaxIn { get; init; }
    public int MaxOut { get; init; }
    public int MaxFeature { get; init; }

    public bool HasPage(ushort page) => UsagePages.Contains(page);
    public bool HasInput(byte id) => Inputs.Any(r => r.Id == id);
    public bool HasOutput(byte id) => Outputs.Any(r => r.Id == id);

    // Bytes to write for an output report. Windows requires the collection's maximum length;
    // Linux and macOS get that report's own length so no extra bytes are sent.
    public int OutputLength(byte id)
    {
        if (OperatingSystem.IsWindows() || Outputs.Count == 0) return MaxOut;
        foreach (var r in Outputs) if (r.Id == id) return r.Length;
        return MaxOut;
    }

    public string Pages => string.Join(",", UsagePages.OrderBy(p => p).Select(p => p.ToString("X4")));
    public override string ToString() => $"{Vid:X4}:{Pid:X4} {Product}";
}

public sealed class HidInventory
{
    public IReadOnlyList<HidCollection> All { get; }
    public static HidInventory Empty { get; } = new(new List<HidCollection>());

    HidInventory(List<HidCollection> all) { All = all; }

    public IEnumerable<HidCollection> Vendor(ushort vid) => All.Where(c => c.Vid == vid);

    public static HidInventory Scan()
    {
        List<HidDevice> devices;
        try { devices = DeviceList.Local.GetHidDevices().ToList(); }
        catch (Exception ex) { Log.Write("HID: " + ex.Message); return Empty; }

        var list = new List<HidCollection>();
        foreach (HidDevice d in devices)
        {
            try { list.Add(Describe(d)); }
            catch { /* removed while enumerating */ }
        }
        return new HidInventory(list);
    }

    static HidCollection Describe(HidDevice d)
    {
        string product = "";
        try { product = d.GetProductName() ?? ""; } catch { }
        var c = new HidCollection
        {
            Device = d,
            Vid = (ushort)d.VendorID,
            Pid = (ushort)d.ProductID,
            Product = product.Trim(),
            MaxIn = Safe(d.GetMaxInputReportLength),
            MaxOut = Safe(d.GetMaxOutputReportLength),
            MaxFeature = Safe(d.GetMaxFeatureReportLength),
        };
        try
        {
            ReportDescriptor rd = d.GetReportDescriptor();
            foreach (DeviceItem item in rd.DeviceItems)
                foreach (uint usage in item.Usages.GetAllValues())
                    c.UsagePages.Add((ushort)(usage >> 16));
            foreach (Report r in rd.InputReports) c.Inputs.Add((r.ReportID, r.Length));
            foreach (Report r in rd.OutputReports) c.Outputs.Add((r.ReportID, r.Length));
            foreach (Report r in rd.FeatureReports) c.Features.Add((r.ReportID, r.Length));
        }
        catch { /* descriptor not readable (e.g. keyboards the OS keeps exclusively) */ }
        return c;
    }

    static int Safe(Func<int> f)
    {
        try { return f(); } catch { return 0; }
    }
}

public static class HidIo
{
    // Opens the interface, sends an output report and waits for an input report that passes 'match'.
    // Returns null when no answer arrives in time.
    public static byte[]? Exchange(HidCollection c, byte[] request, int timeoutMs, Func<byte[], bool> match)
    {
        using HidStream s = c.Device.Open();
        return Exchange(s, c, request, timeoutMs, match);
    }

    public static byte[]? Exchange(HidStream s, HidCollection c, byte[] request, int timeoutMs, Func<byte[], bool> match)
    {
        var buf = new byte[Math.Max(request.Length, c.OutputLength(request[0]))];
        Array.Copy(request, buf, request.Length);
        s.WriteTimeout = 1000;
        s.Write(buf);

        var sw = Stopwatch.StartNew();
        var rb = new byte[Math.Max(64, c.MaxIn)];
        while (true)
        {
            int left = timeoutMs - (int)sw.ElapsedMilliseconds;
            if (left <= 0) return null;
            s.ReadTimeout = left;
            int n;
            try { n = s.Read(rb, 0, rb.Length); }
            catch (TimeoutException) { return null; }
            if (n <= 0) continue;
            byte[] r = rb.AsSpan(0, n).ToArray();
            if (match(r)) return r;
        }
    }

    public static string Hex(byte[]? b) => b == null ? "(no response)" : BitConverter.ToString(b).Replace('-', ' ');
}
