using System.Runtime.InteropServices;
using System.Text;
using Juicebar.Core.Sources;

namespace Juicebar.Core;

// A device's state during the session (last real reading, alerts) plus the user's choices.
public sealed class DeviceState
{
    public static int LowLevel = 20;   // from Settings.LowThreshold

    public required string Id { get; init; }
    public string Name { get; set; } = "";
    public string Alias { get; set; } = "";
    public bool Hidden { get; set; }
    public DeviceKind Kind { get; set; }
    public string Source { get; set; } = "";
    public int ColorIndex { get; set; }
    public BatteryReading Last { get; private set; } = BatteryReading.Fail(Loc.T("reading"));
    public int LastPercent { get; private set; } = -1;   // last real percentage of this session
    public DateTime LastPercentTime { get; private set; }
    public bool Warned { get; set; }
    public bool FullWarned { get; set; }
    public bool WasCharging { get; set; }

    public string Display => Alias.Length > 0 ? Alias : Name;
    public bool Live => Last.HasPercent;
    public bool Low => Live && !Last.Charging && Last.Percent < LowLevel;

    // Value drawn by the icons: the current one or, when the device only sleeps, the last known one.
    public int IconPercent => Last.HasPercent ? Last.Percent : Last.Asleep ? LastPercent : -1;

    public void Update(BatteryReading r)
    {
        Last = r;
        if (r.HasPercent) { LastPercent = r.Percent; LastPercentTime = DateTime.Now; }
    }

    public string ShortText()
    {
        if (Last.HasPercent) return Last.Charging ? Loc.F("short_charging", Last.Percent) : Last.Percent + "%";
        if (Last.Asleep) return LastPercent >= 0 ? Loc.F("short_asleep_pct", LastPercent) : Loc.T("short_asleep");
        if (Last.Status == Loc.T("reading")) return Loc.T("short_reading");
        return Last.Status.StartsWith(Loc.T("disconnected_missing")[..5]) ? Loc.T("short_disconnected") : Loc.T("short_unavailable");
    }

    public string Detail()
    {
        if (Last.HasPercent)
        {
            string s = Last.Charging ? Loc.T("charging") : Last.Full ? Loc.T("charged") : Low ? Loc.T("low") : Loc.T("connected");
            return Last.Approximate ? Loc.F("approx", s) : s;
        }
        if (Last.Asleep && LastPercent >= 0) return Loc.F("asleep_last", LastPercentTime.ToString("HH:mm"));
        return Last.Status;
    }
}

// Polls every source, merges the results without duplicates and keeps each device's state.
// Poll() runs in the background; Apply() and the rest run on the UI thread.
public sealed class DeviceMonitor
{
    readonly List<IBatterySource> sources;
    readonly Dictionary<IBatterySource, (DateTime Next, IReadOnlyList<DeviceReading> Last)> cache = new();
    readonly List<DeviceState> devices = new();

    public KnownDevices Known { get; } = new();
    public History History { get; } = new();
    public IReadOnlyList<DeviceState> All => devices;
    public IReadOnlyList<DeviceState> Visible => devices.Where(d => !d.Hidden).ToList();
    public IReadOnlyList<IBatterySource> Sources => sources;

    // In priority order: when two sources see the same hardware, the first one wins.
    public static IReadOnlyList<IBatterySource> AllSources() => new IBatterySource[]
    {
        new CompxSource(),
        new CorsairVoidSource(),
        new LogitechSource(),
        new HeadsetControlSource(),
        new WindowsBluetoothSource(),
        new LinuxSource(),
        new MacSource(),
    };

    public DeviceMonitor()
    {
        sources = AllSources().Where(s => s.IsSupported).ToList();
    }

    public void Load()
    {
        AppPaths.MigrateLegacyNames();
        Known.Load();
        History.Load();
        foreach (DeviceEntry e in Known.All) Ensure(e.Id);
        Sync();
    }

    // force: query the fast sources now (when the window opens) even if it is not their turn.
    public IReadOnlyList<DeviceReading> Poll(bool force)
    {
        HidInventory hid = HidInventory.Scan();
        DateTime now = DateTime.UtcNow;
        Task<IReadOnlyList<DeviceReading>>[] tasks = sources.Select(s => Task.Run(() =>
        {
            lock (cache)
                if (cache.TryGetValue(s, out var c) && now < c.Next && !(force && s.Interval <= TimeSpan.FromSeconds(10)))
                    return c.Last;
            IReadOnlyList<DeviceReading> r;
            try { r = s.Poll(hid, null); }
            catch (Exception ex) { Log.Write($"{s.Name}: {ex.Message}"); r = Array.Empty<DeviceReading>(); }
            lock (cache) cache[s] = (now + s.Interval, r);
            return r;
        })).ToArray();
        Task.WaitAll(tasks);

        // No duplicates across sources: same VID:PID or same name already seen in an earlier source.
        var claimedHw = new Dictionary<(ushort, ushort), string>();
        var claimedName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<DeviceReading>();
        foreach (var t in tasks)
            foreach (DeviceReading r in t.Result)
            {
                if (r.Vid != 0 && claimedHw.TryGetValue((r.Vid, r.Pid), out string? hwSrc) && hwSrc != r.Source) continue;
                string key = Normalize(r.Name);
                if (claimedName.TryGetValue(key, out string? nameSrc) && nameSrc != r.Source) continue;
                if (r.Vid != 0) claimedHw[(r.Vid, r.Pid)] = r.Source;
                claimedName[key] = r.Source;
                merged.Add(r);
            }
        return merged;
    }

    static string Normalize(string name) => new string(name.Where(char.IsLetterOrDigit).ToArray());

    // Applies a poll: updates states, history and the known list.
    public void Apply(IReadOnlyList<DeviceReading> readings)
    {
        var seen = new HashSet<string>();
        foreach (DeviceReading r in readings)
        {
            seen.Add(r.Id);
            Known.Remember(r);
            DeviceState d = Ensure(r.Id);
            d.Source = r.Source;
            d.Update(r.Battery);
            if (r.Battery.HasPercent) History.Add(r.Id, r.Battery.Percent, r.Battery.Charging);
        }
        foreach (DeviceState d in devices)
            if (!seen.Contains(d.Id))
                d.Update(BatteryReading.Fail(Loc.T("disconnected_missing")));
        Sync();
    }

    // Copies the user's choices (order, name, hidden, color) from the known list to the states.
    public void Sync()
    {
        devices.RemoveAll(d => Known.Find(d.Id) == null);
        devices.Sort((a, b) => Known.IndexOf(a.Id).CompareTo(Known.IndexOf(b.Id)));
        for (int i = 0; i < devices.Count; i++)
        {
            DeviceState d = devices[i];
            DeviceEntry e = Known.Find(d.Id)!;
            d.Name = e.Name;
            d.Kind = e.Kind;
            d.Alias = e.Alias;
            d.Hidden = e.Hidden;
            d.ColorIndex = e.Color >= 0 ? e.Color : Known.IndexOf(d.Id);
        }
    }

    public void Forget(string id)
    {
        Known.Forget(id);
        Sync();
    }

    public void Move(string id, int delta)
    {
        Known.Move(id, delta);
        Sync();
    }

    public void Change(string id, Action<DeviceEntry> edit)
    {
        DeviceEntry? e = Known.Find(id);
        if (e == null) return;
        edit(e);
        Known.Save();
        Sync();
    }

    DeviceState Ensure(string id)
    {
        DeviceState? d = devices.Find(x => x.Id == id);
        if (d != null) return d;
        d = new DeviceState { Id = id };
        devices.Add(d);
        return d;
    }

    public string EtaText(DeviceState d)
    {
        if (!d.Live) return d.Last.Asleep ? Loc.T("eta_asleep") : Loc.T("eta_offline");
        if (d.Last.Charging) return Loc.T("charging");
        TimeSpan? r = History.Remaining(d.Id);
        if (r == null) return Loc.T("eta_estimating");
        TimeSpan t = r.Value;
        if (t.TotalHours >= 10) return Loc.F("eta_h", Math.Round(t.TotalHours));
        if (t.TotalHours >= 1) return Loc.F("eta_hm", (int)t.TotalHours, t.Minutes);
        return Loc.F("eta_m", Math.Max(1, (int)t.TotalMinutes));
    }

    // Battery health line for a device, or a "not enough data" message.
    public string HealthText(string id)
    {
        Stats.Summary? s = History.Stats.For(id);
        if (s == null) return Loc.T("dev_stats_none");
        string full = s.FullChargeHours >= 48 ? Loc.F("days", Math.Round(s.FullChargeHours / 24, 1)) : Loc.F("hours", Math.Round(s.FullChargeHours, 1));
        return Loc.F("dev_stats", Math.Round(s.Cycles, 1), Math.Round(s.DrainPerHour, 1), full);
    }

    // Plain-text report of what the system sees and what each source answers (English only).
    public static string Diagnose()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Juicebar - diagnostics   {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        sb.AppendLine($"Data folder: {AppPaths.Dir}");
        sb.AppendLine();

        HidInventory hid = HidInventory.Scan();
        sb.AppendLine("HID devices (VID:PID  usage pages  name):");
        foreach (var c in hid.All.OrderBy(c => c.Vid).ThenBy(c => c.Pid))
            sb.AppendLine($"  {c.Vid:X4}:{c.Pid:X4}  [{c.Pages}]  in {c.MaxIn} out {c.MaxOut} feat {c.MaxFeature}  {c.Product}");
        sb.AppendLine();

        foreach (IBatterySource s in AllSources())
        {
            sb.AppendLine($"== {s.Name} ==");
            sb.AppendLine($"Systems: {s.Platforms}. On this computer: {(s.IsSupported ? "active" : "not applicable")}.");
            if (!s.IsSupported) { sb.AppendLine(); continue; }
            var log = new StringBuilder();
            IReadOnlyList<DeviceReading> r;
            try { r = s.Poll(hid, log); }
            catch (Exception ex) { r = Array.Empty<DeviceReading>(); log.AppendLine("  error: " + ex.Message); }
            sb.Append(log);
            if (r.Count == 0) sb.AppendLine("  No devices.");
            foreach (DeviceReading d in r)
            {
                BatteryReading b = d.Battery;
                string val = b.HasPercent ? $"{b.Percent}%{(b.Charging ? " charging" : "")}{(b.Approximate ? " (approx.)" : "")}" : b.Status;
                sb.AppendLine($"  -> {d.Name} [{d.Kind}, {d.Id}]: {val}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("== System monitor ==");
        var sys = new SystemMonitor();
        Thread.Sleep(500);
        SystemSnapshot snap = sys.Sample();
        sb.AppendLine(snap.Describe());
        sb.AppendLine();
        sb.AppendLine("An access error usually means another program holds the device exclusively (for example iCUE or the vendor's software).");
        if (OperatingSystem.IsLinux())
            sb.AppendLine("On Linux, if HID devices cannot be opened, install the included udev rule (70-juicebar.rules).");
        return sb.ToString();
    }
}
