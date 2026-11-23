using System.Globalization;
using System.Text;

namespace Juicebar.Core;

// App data folder on each system:
//   Windows: %LOCALAPPDATA%\Juicebar (same as version 1, so settings and history carry over)
//   macOS:   ~/Library/Application Support/Juicebar
//   Linux:   $XDG_CONFIG_HOME/juicebar or ~/.config/juicebar
public static class AppPaths
{
    public static string Dir { get; } = Compute();

    static string Compute()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Juicebar");
        if (OperatingSystem.IsMacOS())
            return Path.Combine(home, "Library", "Application Support", "Juicebar");
        string xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? "";
        return Path.Combine(xdg.Length > 0 ? xdg : Path.Combine(home, ".config"), "juicebar");
    }

    public static string File(string name) => Path.Combine(Dir, name);

    // Juicebar used to be called MonitBat. On the first start its data (settings, history, statistics,
    // devices) is copied over. Copied, not moved: version 1, still named MonitBat, keeps using its folder.
    public static void MigrateFromMonitBat()
    {
        try
        {
            string old = Path.Combine(Path.GetDirectoryName(Dir)!, OperatingSystem.IsLinux() ? "monit" + "bat" : "Monit" + "Bat");
            if (!Directory.Exists(old) || (Directory.Exists(Dir) && Directory.EnumerateFileSystemEntries(Dir).Any())) return;
            Directory.CreateDirectory(Dir);
            foreach (string f in Directory.GetFiles(old))
            {
                string name = Path.GetFileName(f);
                if (name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) continue;
                System.IO.File.Copy(f, Path.Combine(Dir, name), false);
            }
            Log.Write("copied settings and history from " + old);
        }
        catch (Exception ex) { Log.Write("migrate from old name: " + ex.Message); }
    }

    // Earlier versions used Spanish file names.
    public static void MigrateLegacyNames()
    {
        Rename("historial.csv", "history.csv");
        Rename("dispositivos.txt", "devices.txt");
    }

    static void Rename(string oldName, string newName)
    {
        try
        {
            string o = File(oldName), n = File(newName);
            if (System.IO.File.Exists(o) && !System.IO.File.Exists(n)) System.IO.File.Move(o, n);
        }
        catch (Exception ex) { Log.Write($"rename {oldName}: {ex.Message}"); }
    }
}

// Minimal log, mainly to know why the app closed or a reading failed.
public static class Log
{
    const long MaxBytes = 256 * 1024;
    static readonly object sync = new();

    public static void Write(string message)
    {
        try
        {
            lock (sync)
            {
                Directory.CreateDirectory(AppPaths.Dir);
                string path = AppPaths.File("juicebar.log");
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes) info.Delete();
                System.IO.File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
        }
        catch { } // logging must never take the app down
    }
}

// User preferences in config.ini (key=value lines). Older Spanish keys and values are still read.
public sealed class Settings
{
    public IconStyle Icon { get; set; } = IconStyle.Number;
    public WindowStyle Window { get; set; } = WindowStyle.Glass;
    public Lang Language { get; set; } = Lang.En;
    public int LowThreshold { get; set; } = 20;
    public bool NotifyFull { get; set; } = true;
    public bool Sound { get; set; }
    public bool ShowSystem { get; set; } = true;
    public SystemMetric TrayMetric { get; set; } = SystemMetric.None;
    public bool Widget { get; set; }
    public bool WidgetOnTop { get; set; } = true;
    public int WidgetX { get; set; } = int.MinValue;
    public int WidgetY { get; set; } = int.MinValue;
    public bool Hotkey { get; set; } = true;
    public bool CheckUpdates { get; set; } = true;

    static string FilePath => AppPaths.File("config.ini");

    static readonly Dictionary<string, string> LegacyValues = new(StringComparer.OrdinalIgnoreCase)
    {
        ["numero"] = "Number", ["pilas"] = "Cells", ["anillos"] = "Rings", ["glifos"] = "Glyphs",
        ["cristal"] = "Glass", ["segmentos"] = "Segments", ["historial"] = "History",
    };

    public static Settings Load()
    {
        var s = new Settings();
        try
        {
            if (!System.IO.File.Exists(FilePath)) return s;
            foreach (string line in System.IO.File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line[..eq].Trim().ToLowerInvariant(), val = line[(eq + 1)..].Trim();
                if (LegacyValues.TryGetValue(val, out string? mapped)) val = mapped;
                switch (key)
                {
                    case "icon" or "icono":
                        if (Enum.TryParse(val, true, out IconStyle i) && Enum.IsDefined(i)) s.Icon = i;
                        break;
                    case "window" or "ventana":
                        if (Enum.TryParse(val, true, out WindowStyle w) && Enum.IsDefined(w)) s.Window = w;
                        break;
                    case "language":
                        if (Enum.TryParse(val, true, out Lang l) && Enum.IsDefined(l)) s.Language = l;
                        break;
                    case "tray_metric":
                        if (Enum.TryParse(val, true, out SystemMetric m) && Enum.IsDefined(m)) s.TrayMetric = m;
                        break;
                    case "low_threshold": if (int.TryParse(val, out int t)) s.LowThreshold = Math.Clamp(t, 5, 50); break;
                    case "notify_full": s.NotifyFull = Bool(val, s.NotifyFull); break;
                    case "sound": s.Sound = Bool(val, s.Sound); break;
                    case "show_system": s.ShowSystem = Bool(val, s.ShowSystem); break;
                    case "widget": s.Widget = Bool(val, s.Widget); break;
                    case "widget_on_top": s.WidgetOnTop = Bool(val, s.WidgetOnTop); break;
                    case "widget_x": if (int.TryParse(val, out int x)) s.WidgetX = x; break;
                    case "widget_y": if (int.TryParse(val, out int y)) s.WidgetY = y; break;
                    case "hotkey": s.Hotkey = Bool(val, s.Hotkey); break;
                    case "check_updates": s.CheckUpdates = Bool(val, s.CheckUpdates); break;
                }
            }
        }
        catch (Exception ex) { Log.Write("read config: " + ex.Message); }
        return s;
    }

    static bool Bool(string v, bool fallback) =>
        v is "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) ? true
        : v is "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase) ? false
        : fallback;

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Dir);
            static string B(bool b) => b ? "1" : "0";
            var sb = new StringBuilder();
            sb.Append($"icon={Icon.ToString().ToLowerInvariant()}\n");
            sb.Append($"window={Window.ToString().ToLowerInvariant()}\n");
            sb.Append($"language={Language.ToString().ToLowerInvariant()}\n");
            sb.Append($"low_threshold={LowThreshold}\n");
            sb.Append($"notify_full={B(NotifyFull)}\n");
            sb.Append($"sound={B(Sound)}\n");
            sb.Append($"show_system={B(ShowSystem)}\n");
            sb.Append($"tray_metric={TrayMetric.ToString().ToLowerInvariant()}\n");
            sb.Append($"widget={B(Widget)}\n");
            sb.Append($"widget_on_top={B(WidgetOnTop)}\n");
            if (WidgetX != int.MinValue) sb.Append($"widget_x={WidgetX}\nwidget_y={WidgetY}\n");
            sb.Append($"hotkey={B(Hotkey)}\n");
            sb.Append($"check_updates={B(CheckUpdates)}\n");
            System.IO.File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
        }
        catch (Exception ex) { Log.Write("save config: " + ex.Message); }
    }
}

public readonly record struct HistoryPoint(DateTime Time, int Percent, bool Charging);

// Readings of the last 24 h per device in history.csv (date;id;percent;charging).
// Version 1 used 'm' (mouse) and 'h' (headset) as ids: they are translated on load.
public sealed class History
{
    public static readonly TimeSpan Span = TimeSpan.FromHours(24);
    const string Fmt = "yyyy-MM-ddTHH:mm:ss";
    static readonly Dictionary<string, string> LegacyIds = new() { ["m"] = "compx:f5f7", ["h"] = "corsair:0a51" };
    readonly Dictionary<string, List<HistoryPoint>> data = new();
    static string FilePath => AppPaths.File("history.csv");

    public Stats Stats { get; } = new();

    public IReadOnlyList<HistoryPoint> Of(string id) => List(id);

    List<HistoryPoint> List(string id)
    {
        if (!data.TryGetValue(id, out var l)) data[id] = l = new List<HistoryPoint>();
        return l;
    }

    // Loads the file and rewrites it without anything older than 24 h (and with the new ids).
    public void Load()
    {
        Stats.Load();
        try
        {
            if (!System.IO.File.Exists(FilePath)) return;
            DateTime from = DateTime.Now - Span;
            var keep = new StringBuilder();
            foreach (string line in System.IO.File.ReadAllLines(FilePath))
            {
                string[] f = line.Split(';');
                if (f.Length != 4) continue;
                if (!DateTime.TryParseExact(f[0], Fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime t)) continue;
                if (!int.TryParse(f[2], out int p) || t < from) continue;
                string id = LegacyIds.TryGetValue(f[1], out string? mapped) ? mapped : f[1];
                List(id).Add(new HistoryPoint(t, p, f[3] == "1"));
                keep.Append(f[0]).Append(';').Append(id).Append(';').Append(p).Append(';').Append(f[3]).Append('\n');
            }
            System.IO.File.WriteAllText(FilePath, keep.ToString());
        }
        catch (Exception ex) { Log.Write("read history: " + ex.Message); }
    }

    // Records the reading when the percentage or charging state changed, or every 30 min anyway.
    public void Add(string id, int percent, bool charging)
    {
        var l = List(id);
        DateTime now = DateTime.Now;
        var point = new HistoryPoint(now, percent, charging);
        if (l.Count > 0)
        {
            HistoryPoint last = l[^1];
            if (last.Percent == percent && last.Charging == charging && now - last.Time < TimeSpan.FromMinutes(30)) return;
            Stats.Record(id, last, point);
        }
        l.Add(point);
        while (l.Count > 0 && l[0].Time < now - Span) l.RemoveAt(0);
        try
        {
            Directory.CreateDirectory(AppPaths.Dir);
            System.IO.File.AppendAllText(FilePath,
                $"{now.ToString(Fmt, CultureInfo.InvariantCulture)};{id};{percent};{(charging ? 1 : 0)}\n");
        }
        catch (Exception ex) { Log.Write("save history: " + ex.Message); }
    }

    // Time left from the drain rate since the last charge (or the last level increase).
    // Null until at least 30 min and 2 % of drain have been measured.
    public TimeSpan? Remaining(string id)
    {
        var l = List(id);
        if (l.Count < 2) return null;
        HistoryPoint end = l[^1];
        if (end.Charging) return null;
        int i = l.Count - 1;
        while (i > 0 && !l[i - 1].Charging && l[i - 1].Percent >= l[i].Percent) i--;
        HistoryPoint start = l[i];
        double hours = (end.Time - start.Time).TotalHours;
        int drop = start.Percent - end.Percent;
        if (hours < 0.5 || drop < 2) return null;
        double left = end.Percent / (drop / hours);
        return left > 24 * 14 ? null : TimeSpan.FromHours(left);
    }
}

// Battery health: daily totals per device in stats.csv (date;id;discharged;charged;minutes).
// From them: estimated charge cycles, average drain per hour and how long a full charge lasts.
public sealed class Stats
{
    sealed class Day { public double Discharged, Charged, Minutes; }

    public sealed record Summary(double Cycles, double DrainPerHour, double FullChargeHours, int Days);

    const int KeepDays = 400;
    readonly Dictionary<(string Date, string Id), Day> days = new();
    static string FilePath => AppPaths.File("stats.csv");

    public void Load()
    {
        try
        {
            if (!System.IO.File.Exists(FilePath)) return;
            string oldest = DateTime.Today.AddDays(-KeepDays).ToString("yyyy-MM-dd");
            foreach (string line in System.IO.File.ReadAllLines(FilePath))
            {
                string[] f = line.Split(';');
                if (f.Length != 5 || string.CompareOrdinal(f[0], oldest) < 0) continue;
                var d = new Day
                {
                    Discharged = Num(f[2]),
                    Charged = Num(f[3]),
                    Minutes = Num(f[4]),
                };
                days[(f[0], f[1])] = d;
            }
        }
        catch (Exception ex) { Log.Write("read stats: " + ex.Message); }
    }

    static double Num(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;

    // Adds what happened between two consecutive history points. Gaps over 2 h are ignored
    // (the app was closed or the device was off), so they do not distort the drain rate.
    public void Record(string id, HistoryPoint prev, HistoryPoint cur)
    {
        double minutes = (cur.Time - prev.Time).TotalMinutes;
        if (minutes <= 0 || minutes > 120) return;
        string date = cur.Time.ToString("yyyy-MM-dd");
        if (!days.TryGetValue((date, id), out Day? d)) days[(date, id)] = d = new Day();
        if (!prev.Charging && !cur.Charging && cur.Percent <= prev.Percent)
        {
            d.Discharged += prev.Percent - cur.Percent;
            d.Minutes += minutes;
        }
        else if (cur.Percent > prev.Percent)
            d.Charged += cur.Percent - prev.Percent;
        Save();
    }

    public Summary? For(string id)
    {
        double dis = 0, min = 0;
        int n = 0;
        foreach (var kv in days)
            if (kv.Key.Id == id) { dis += kv.Value.Discharged; min += kv.Value.Minutes; n++; }
        if (dis < 5 || min < 60) return null;
        double rate = dis / (min / 60);
        return new Summary(dis / 100, rate, 100 / rate, n);
    }

    void Save()
    {
        try
        {
            var sb = new StringBuilder();
            foreach (var kv in days.OrderBy(k => k.Key.Date))
                sb.Append(string.Create(CultureInfo.InvariantCulture,
                    $"{kv.Key.Date};{kv.Key.Id};{kv.Value.Discharged:0.#};{kv.Value.Charged:0.#};{kv.Value.Minutes:0.#}\n"));
            Directory.CreateDirectory(AppPaths.Dir);
            System.IO.File.WriteAllText(FilePath, sb.ToString());
        }
        catch (Exception ex) { Log.Write("save stats: " + ex.Message); }
    }
}

// A device seen at least once, with the user's choices for it.
public sealed class DeviceEntry
{
    public required string Id { get; init; }
    public DeviceKind Kind { get; set; }
    public ushort Vid { get; set; }
    public ushort Pid { get; set; }
    public string Name { get; set; } = "";
    public string Alias { get; set; } = "";
    public bool Hidden { get; set; }
    public int Color { get; set; } = -1;        // -1 = automatic (by position)

    public string Display => Alias.Length > 0 ? Alias : Name;
}

// Known devices in devices.txt (id;kind;vid;pid;name;alias;hidden;color), in the user's order.
// Used to show missing devices as "Disconnected" and to keep names, order and colors.
public sealed class KnownDevices
{
    readonly List<DeviceEntry> entries = new();
    static string FilePath => AppPaths.File("devices.txt");

    public IReadOnlyList<DeviceEntry> All => entries;

    public DeviceEntry? Find(string id) => entries.Find(e => e.Id == id);

    public int IndexOf(string id) => entries.FindIndex(e => e.Id == id);

    public void Load()
    {
        try
        {
            if (!System.IO.File.Exists(FilePath)) return;
            foreach (string line in System.IO.File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                string[] f = line.Split(';');
                if (f.Length < 5 || f[0].Length == 0 || Find(f[0]) != null) continue;
                if (!Enum.TryParse(f[1], true, out DeviceKind kind)) kind = DeviceKind.Other;
                ushort.TryParse(f[2], NumberStyles.HexNumber, null, out ushort vid);
                ushort.TryParse(f[3], NumberStyles.HexNumber, null, out ushort pid);
                entries.Add(new DeviceEntry
                {
                    Id = f[0], Kind = kind, Vid = vid, Pid = pid, Name = f[4],
                    Alias = f.Length > 5 ? f[5] : "",
                    Hidden = f.Length > 6 && f[6] == "1",
                    Color = f.Length > 7 && int.TryParse(f[7], out int c) ? c : -1,
                });
            }
        }
        catch (Exception ex) { Log.Write("read devices: " + ex.Message); }
    }

    // Adds or updates what the source reports (not the user's choices); saves when something changed.
    public DeviceEntry Remember(DeviceReading r)
    {
        DeviceEntry? e = Find(r.Id);
        if (e == null)
        {
            e = new DeviceEntry { Id = r.Id, Kind = r.Kind, Vid = r.Vid, Pid = r.Pid, Name = r.Name };
            entries.Add(e);
            Save();
            return e;
        }
        if (e.Kind != r.Kind || e.Vid != r.Vid || e.Pid != r.Pid || e.Name != r.Name)
        {
            e.Kind = r.Kind; e.Vid = r.Vid; e.Pid = r.Pid; e.Name = r.Name;
            Save();
        }
        return e;
    }

    public void Forget(string id)
    {
        if (entries.RemoveAll(e => e.Id == id) > 0) Save();
    }

    public void Move(string id, int delta)
    {
        int i = IndexOf(id), j = i + delta;
        if (i < 0 || j < 0 || j >= entries.Count) return;
        (entries[i], entries[j]) = (entries[j], entries[i]);
        Save();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Dir);
            static string Clean(string s) => s.Replace(';', ',').Replace('\n', ' ');
            var sb = new StringBuilder();
            foreach (DeviceEntry e in entries)
                sb.Append($"{e.Id};{e.Kind};{e.Vid:X4};{e.Pid:X4};{Clean(e.Name)};{Clean(e.Alias)};{(e.Hidden ? 1 : 0)};{e.Color}\n");
            System.IO.File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
        }
        catch (Exception ex) { Log.Write("save devices: " + ex.Message); }
    }
}
