using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Juicebar.Core.Sources;

// Runs an external program and returns its output, or null if it is missing, fails or takes too long.
public static class Run
{
    public static string? Capture(string file, string args, int timeoutMs, IDictionary<string, string>? env = null)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (env != null) foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;
            using Process? p = Process.Start(psi);
            if (p == null) return null;
            Task<string> output = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { }
                return null;
            }
            return output.Result;
        }
        catch { return null; }
    }

    // Finds an executable in the system folder (Windows) or in absolute PATH entries.
    // The app's own folder and relative PATH entries (".", "bin"...) are skipped on purpose,
    // so a file planted next to Juicebar or in the working directory is never run.
    public static string? Which(string name)
    {
        string[] names = OperatingSystem.IsWindows() ? new[] { name + ".exe" } : new[] { name };
        var dirs = new List<string>();
        if (OperatingSystem.IsWindows()) dirs.Add(Environment.SystemDirectory);
        dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                          .Select(d => d.Trim().Trim('"')).Where(Path.IsPathFullyQualified));
        if (OperatingSystem.IsMacOS()) dirs.AddRange(new[] { "/opt/homebrew/bin", "/usr/local/bin" });
        foreach (string d in dirs)
            foreach (string n in names)
            {
                try
                {
                    string p = Path.Combine(d, n);
                    if (File.Exists(p)) return p;
                }
                catch { }
            }
        return null;
    }

    public static ushort Hex16(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return ushort.TryParse(s, NumberStyles.HexNumber, null, out ushort v) ? v : (ushort)0;
    }

    public static DeviceKind KindFromName(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("mouse") || n.Contains("ratón") || n.Contains("raton")) return DeviceKind.Mouse;
        if (n.Contains("keyboard") || n.Contains("teclado")) return DeviceKind.Keyboard;
        if (n.Contains("headset") || n.Contains("headphone") || n.Contains("buds") || n.Contains("auricular") || n.Contains("airpods")) return DeviceKind.Headset;
        if (n.Contains("controller") || n.Contains("gamepad") || n.Contains("mando") || n.Contains("dualsense") || n.Contains("xbox")) return DeviceKind.Controller;
        return DeviceKind.Other;
    }
}

// HeadsetControl (https://github.com/Sapd/HeadsetControl), when installed: headsets from SteelSeries,
// HyperX, Logitech G, Corsair, Roccat, Audeze and others. Uses its JSON output ("headsetcontrol -b -o json").
public sealed class HeadsetControlSource : IBatterySource
{
    public string Name => "HeadsetControl (headsets from many brands)";
    public string Platforms => "Windows, Linux, macOS, when HeadsetControl is installed";
    public bool IsSupported => true;
    public TimeSpan Interval => TimeSpan.FromSeconds(30);

    public IReadOnlyList<DeviceReading> Poll(HidInventory hid, StringBuilder? log)
    {
        var result = new List<DeviceReading>();
        string? exe = Run.Which("headsetcontrol");
        if (exe == null) { log?.AppendLine("  HeadsetControl is not installed."); return result; }
        string? json = Run.Capture(exe, "-b -o json", 8000);
        log?.AppendLine($"  {exe}: {(json == null ? "no response" : json.Length + " characters")}");
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("devices", out JsonElement devices)) return result;
            foreach (JsonElement d in devices.EnumerateArray())
            {
                string name = Str(d, "device") ?? Str(d, "product") ?? Loc.T("name_headset");
                ushort vid = Run.Hex16(Str(d, "id_vendor")), pid = Run.Hex16(Str(d, "id_product"));
                BatteryReading b = BatteryReading.Fail(Loc.T("unavailable"));
                if (d.TryGetProperty("battery", out JsonElement bat))
                {
                    string status = Str(bat, "status") ?? "";
                    int level = bat.TryGetProperty("level", out JsonElement lv) && lv.TryGetInt32(out int l) ? l : -1;
                    b = status switch
                    {
                        "BATTERY_AVAILABLE" when level >= 0 => BatteryReading.Level(level),
                        "BATTERY_CHARGING" when level >= 0 => BatteryReading.Level(level, charging: true),
                        "BATTERY_CHARGING" => new BatteryReading { Charging = true, Status = Loc.T("charging") },
                        "BATTERY_UNAVAILABLE" => BatteryReading.Fail(Loc.T("off_or_range")),
                        _ => BatteryReading.Fail(Loc.T("no_reply")),
                    };
                }
                result.Add(new DeviceReading
                {
                    Id = $"headsetcontrol:{vid:x4}:{pid:x4}",
                    Name = name,
                    Kind = DeviceKind.Headset,
                    Source = Name,
                    Battery = b,
                    Vid = vid,
                    Pid = pid,
                });
            }
        }
        catch (Exception ex) { log?.AppendLine("  invalid JSON: " + ex.Message); }
        return result;
    }

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

// Windows: battery the system exposes for Bluetooth devices (DEVPKEY_Bluetooth_Battery,
// the same value Settings > Bluetooth shows).
public sealed class WindowsBluetoothSource : IBatterySource
{
    public string Name => "Windows Bluetooth";
    public string Platforms => "Windows";
    public bool IsSupported => OperatingSystem.IsWindows();
    public TimeSpan Interval => TimeSpan.FromSeconds(60);

    public IReadOnlyList<DeviceReading> Poll(HidInventory hid, StringBuilder? log)
    {
        var result = new List<DeviceReading>();
        if (!OperatingSystem.IsWindows()) return result;
        foreach (var (id, name, percent) in Win.BluetoothBatteries())
        {
            if (result.Any(r => r.Name == name)) continue;   // several device nodes of the same device
            log?.AppendLine($"  {name}: {percent}%");
            result.Add(new DeviceReading
            {
                Id = "bt:" + id,
                Name = name,
                Kind = Run.KindFromName(name),
                Source = Name,
                Battery = BatteryReading.Level(percent),
            });
        }
        return result;
    }

    [SupportedOSPlatform("windows")]
    static class Win
    {
        [StructLayout(LayoutKind.Sequential)]
        struct DEVPROPKEY { public Guid fmtid; public uint pid; }

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr hwnd, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SP_DEVINFO_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key, out uint type, byte[]? buffer, uint size, out uint required, uint flags);
        [DllImport("setupapi.dll")]
        static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        const uint DIGCF_PRESENT = 0x2, DIGCF_ALLCLASSES = 0x4;
        static DEVPROPKEY Battery = new() { fmtid = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), pid = 2 };
        static DEVPROPKEY FriendlyName = new() { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
        static DEVPROPKEY Description = new() { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 2 };
        static DEVPROPKEY InstanceId = new() { fmtid = new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"), pid = 256 };

        public static List<(string Id, string Name, int Percent)> BluetoothBatteries()
        {
            var list = new List<(string, string, int)>();
            IntPtr set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (set == new IntPtr(-1)) return list;
            try
            {
                var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
                for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
                {
                    byte[]? b = Prop(set, ref data, ref Battery);
                    if (b == null || b.Length < 1 || b[0] > 100) continue;
                    string name = Text(set, ref data, ref FriendlyName) ?? Text(set, ref data, ref Description) ?? Loc.T("name_bluetooth");
                    string id = Text(set, ref data, ref InstanceId) ?? name;
                    list.Add((id, name, b[0]));
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return list;
        }

        static byte[]? Prop(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key)
        {
            SetupDiGetDevicePropertyW(set, ref data, ref key, out _, null, 0, out uint size, 0);
            if (size == 0) return null;
            var buf = new byte[size];
            return SetupDiGetDevicePropertyW(set, ref data, ref key, out _, buf, size, out _, 0) ? buf : null;
        }

        static string? Text(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key)
        {
            byte[]? b = Prop(set, ref data, ref key);
            return b == null ? null : Encoding.Unicode.GetString(b).TrimEnd('\0');
        }
    }
}

// Linux: UPower (Bluetooth, Logitech through the kernel, controllers...) and OpenRazer when installed.
public sealed class LinuxSource : IBatterySource
{
    static readonly Regex HidPath = new(@"hid-[0-9A-Fa-f]{4}:([0-9A-Fa-f]{4}):([0-9A-Fa-f]{4})", RegexOptions.Compiled);

    public string Name => "Linux system (UPower and OpenRazer)";
    public string Platforms => "Linux";
    public bool IsSupported => OperatingSystem.IsLinux();
    public TimeSpan Interval => TimeSpan.FromSeconds(30);

    public IReadOnlyList<DeviceReading> Poll(HidInventory hid, StringBuilder? log)
    {
        var result = new List<DeviceReading>();
        ReadUPower(result, log);
        ReadOpenRazer(result, log);
        return result;
    }

    void ReadUPower(List<DeviceReading> result, StringBuilder? log)
    {
        string? exe = Run.Which("upower");
        if (exe == null) { log?.AppendLine("  upower is not installed."); return; }
        string? dump = Run.Capture(exe, "--dump", 5000, new Dictionary<string, string> { ["LC_ALL"] = "C", ["LANG"] = "C" });
        if (dump == null) { log?.AppendLine("  upower did not answer."); return; }

        foreach (string block in dump.Replace("\r", "").Split("\n\n"))
        {
            if (!block.StartsWith("Device:")) continue;
            string path = block.Split('\n')[0][7..].Trim();
            if (path.Contains("DisplayDevice") || path.Contains("line_power")) continue;
            var props = new Dictionary<string, string>();
            string type = "";
            foreach (string raw in block.Split('\n').Skip(1))
            {
                string line = raw.Trim();
                int colon = line.IndexOf(':');
                if (colon > 0) props[line[..colon].Trim()] = line[(colon + 1)..].Trim();
                else if (line.Length > 0 && raw.StartsWith("  ") && !raw.StartsWith("    ")) type = line;
            }
            if (props.GetValueOrDefault("power supply") == "yes") continue;   // the laptop's own battery
            DeviceKind kind = type switch
            {
                "mouse" => DeviceKind.Mouse,
                "keyboard" => DeviceKind.Keyboard,
                "headset" or "headphones" => DeviceKind.Headset,
                "gaming-input" => DeviceKind.Controller,
                _ => DeviceKind.Other,
            };
            string native = props.GetValueOrDefault("native-path") ?? path;
            string name = props.GetValueOrDefault("model") ?? "";
            if (name.Length == 0) name = native;
            string pctText = (props.GetValueOrDefault("percentage") ?? "").TrimEnd('%').Trim();
            string state = props.GetValueOrDefault("state") ?? "";
            bool charging = state == "charging" || state == "pending-charge";
            bool full = state == "fully-charged";
            BatteryReading b = double.TryParse(pctText, NumberStyles.Float, CultureInfo.InvariantCulture, out double pct)
                ? BatteryReading.Level((int)Math.Round(pct), charging, full, approximate: (props.GetValueOrDefault("battery-level") ?? "none") != "none")
                : BatteryReading.Fail(Loc.T("unavailable"));
            ushort vid = 0, pid = 0;
            Match m = HidPath.Match(native);
            if (m.Success) { vid = Run.Hex16(m.Groups[1].Value); pid = Run.Hex16(m.Groups[2].Value); }
            log?.AppendLine($"  {name} ({type}, {native}): {pctText}% {state}");
            result.Add(new DeviceReading
            {
                Id = "upower:" + (props.GetValueOrDefault("serial") is { Length: > 0 } serial ? serial : native),
                Name = name,
                Kind = kind == DeviceKind.Other ? Run.KindFromName(name) : kind,
                Source = Name,
                Battery = b,
                Vid = vid,
                Pid = pid,
            });
        }
    }

    // OpenRazer publishes charge_level (0-255) and charge_status in sysfs.
    void ReadOpenRazer(List<DeviceReading> result, StringBuilder? log)
    {
        const string root = "/sys/bus/hid/drivers";
        if (!Directory.Exists(root)) return;
        foreach (string driver in Directory.GetDirectories(root, "razer*"))
            foreach (string dev in Directory.GetDirectories(driver))
            {
                string levelFile = Path.Combine(dev, "charge_level");
                if (!File.Exists(levelFile)) continue;
                try
                {
                    int raw = int.Parse(File.ReadAllText(levelFile).Trim());
                    bool charging = File.Exists(Path.Combine(dev, "charge_status")) && File.ReadAllText(Path.Combine(dev, "charge_status")).Trim() == "1";
                    string typeFile = Path.Combine(dev, "device_type");
                    string name = File.Exists(typeFile) ? File.ReadAllText(typeFile).Trim() : "Razer";
                    string dir = Path.GetFileName(dev);   // 0003:1532:00AB.0007
                    string[] parts = dir.Split(':', '.');
                    ushort vid = parts.Length > 2 ? Run.Hex16(parts[1]) : (ushort)0, pid = parts.Length > 2 ? Run.Hex16(parts[2]) : (ushort)0;
                    log?.AppendLine($"  OpenRazer {name}: {raw}/255");
                    DeviceKind k = Run.KindFromName(name);
                    result.Add(new DeviceReading
                    {
                        Id = $"razer:{vid:x4}:{pid:x4}",
                        Name = name,
                        Kind = k != DeviceKind.Other ? k : DeviceKind.Mouse,
                        Source = Name,
                        Battery = BatteryReading.Level((int)Math.Round(raw * 100 / 255.0), charging),
                        Vid = vid,
                        Pid = pid,
                    });
                }
                catch (Exception ex) { log?.AppendLine($"  OpenRazer {dev}: {ex.Message}"); }
            }
    }
}

// macOS: Bluetooth devices with a battery according to system_profiler (mice, keyboards, headsets, AirPods).
public sealed class MacSource : IBatterySource
{
    public string Name => "macOS Bluetooth";
    public string Platforms => "macOS";
    public bool IsSupported => OperatingSystem.IsMacOS();
    public TimeSpan Interval => TimeSpan.FromSeconds(60);

    public IReadOnlyList<DeviceReading> Poll(HidInventory hid, StringBuilder? log)
    {
        var result = new List<DeviceReading>();
        string? json = Run.Capture("/usr/sbin/system_profiler", "SPBluetoothDataType -json", 20000);
        if (json == null) { log?.AppendLine("  system_profiler did not answer."); return result; }
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            foreach (JsonElement section in doc.RootElement.GetProperty("SPBluetoothDataType").EnumerateArray())
            {
                if (!section.TryGetProperty("device_connected", out JsonElement connected)) continue;
                foreach (JsonElement wrapper in connected.EnumerateArray())
                    foreach (JsonProperty dev in wrapper.EnumerateObject())
                    {
                        int pct = Pct(dev.Value, "device_batteryLevelMain");
                        if (pct < 0) pct = Pct(dev.Value, "device_batteryLevel");
                        if (pct < 0)
                        {
                            int l = Pct(dev.Value, "device_batteryLevelLeft"), r = Pct(dev.Value, "device_batteryLevelRight");
                            pct = l >= 0 && r >= 0 ? Math.Min(l, r) : Math.Max(l, r);
                        }
                        if (pct < 0) continue;
                        string minor = dev.Value.TryGetProperty("device_minorType", out JsonElement mt) ? mt.GetString() ?? "" : "";
                        log?.AppendLine($"  {dev.Name} ({minor}): {pct}%");
                        result.Add(new DeviceReading
                        {
                            Id = "macbt:" + (dev.Value.TryGetProperty("device_address", out JsonElement addr) ? addr.GetString() : dev.Name),
                            Name = dev.Name,
                            Kind = Run.KindFromName(minor + " " + dev.Name),
                            Source = Name,
                            Battery = BatteryReading.Level(pct),
                        });
                    }
            }
        }
        catch (Exception ex) { log?.AppendLine("  invalid JSON: " + ex.Message); }
        return result;
    }

    static int Pct(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out JsonElement v)) return -1;
        string s = (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()) ?? "";
        return int.TryParse(s.TrimEnd('%').Trim(), out int p) ? p : -1;
    }
}
