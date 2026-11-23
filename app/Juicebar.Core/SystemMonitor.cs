using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Juicebar.Core.Sources;

namespace Juicebar.Core;

// One sample of system usage. Values that this computer cannot provide are null.
public sealed class SystemSnapshot
{
    public double? CpuPercent, RamPercent, DiskPercent, GpuPercent, CpuTempC, GpuTempC;
    public double RamUsedGb, RamTotalGb, DiskUsedGb, DiskTotalGb;
    public string GpuName = "", CpuTempSource = "";

    public double? Get(SystemMetric m) => m switch
    {
        SystemMetric.Cpu => CpuPercent,
        SystemMetric.Ram => RamPercent,
        SystemMetric.Gpu => GpuPercent,
        SystemMetric.GpuTemp => GpuTempC,
        SystemMetric.CpuTemp => CpuTempC,
        _ => null,
    };

    public string Describe()
    {
        static string P(double? v) => v.HasValue ? $"{v:0} %" : "n/a";
        static string C(double? v) => v.HasValue ? $"{v:0} °C" : "n/a";
        return string.Create(CultureInfo.InvariantCulture,
            $"CPU {P(CpuPercent)} · RAM {P(RamPercent)} ({RamUsedGb:0.0}/{RamTotalGb:0.0} GB) · Disk {P(DiskPercent)} ({DiskUsedGb:0}/{DiskTotalGb:0} GB)\n" +
            $"GPU {(GpuName.Length > 0 ? GpuName : "-")}: {P(GpuPercent)}, {C(GpuTempC)} · CPU temp {C(CpuTempC)}{(CpuTempSource.Length > 0 ? " (" + CpuTempSource + ")" : "")}");
    }
}

// Reads CPU, RAM, disk and GPU usage and temperatures on Windows, Linux and macOS.
//   CPU usage:  GetSystemTimes (Windows), /proc/stat (Linux), host_statistics (macOS)
//   RAM:        GlobalMemoryStatusEx (Windows), /proc/meminfo (Linux), sysctl + vm_stat (macOS)
//   Disk:       space used on the system drive
//   GPU:        nvidia-smi (NVIDIA on Windows/Linux), amdgpu sysfs (Linux), ioreg (macOS),
//               LibreHardwareMonitor's WMI sensors on Windows when it is running
//   CPU temp:   hwmon (Linux); on Windows LibreHardwareMonitor's WMI, or ACPI (needs admin). Not on macOS.
public sealed class SystemMonitor
{
    ulong prevIdle, prevTotal;
    bool havePrev;
    string? nvidiaSmi;
    bool nvidiaSearched;

    public SystemMonitor()
    {
        try { CpuTicks(out prevIdle, out prevTotal); havePrev = true; } catch { }
    }

    public SystemSnapshot Sample()
    {
        var s = new SystemSnapshot();
        Try(() => s.CpuPercent = Cpu());
        Try(() => Ram(s));
        Try(() => Disk(s));
        Try(() => Gpu(s));
        Try(() => CpuTemp(s));
        return s;
    }

    static void Try(Action a)
    {
        try { a(); } catch { /* that value stays unavailable */ }
    }

    // ---- CPU usage ----

    double? Cpu()
    {
        CpuTicks(out ulong idle, out ulong total);
        double? result = null;
        if (havePrev && total > prevTotal)
            result = Math.Clamp(100.0 * (1 - (double)(idle - prevIdle) / (total - prevTotal)), 0, 100);
        prevIdle = idle;
        prevTotal = total;
        havePrev = true;
        return result;
    }

    static void CpuTicks(out ulong idle, out ulong total)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!Win.GetSystemTimes(out long i, out long k, out long u)) throw new InvalidOperationException();
            idle = (ulong)i;
            total = (ulong)(k + u);   // kernel time already includes idle time
            return;
        }
        if (OperatingSystem.IsMacOS())
        {
            var ticks = new uint[4];   // user, system, idle, nice
            int count = 4;
            if (Mac.host_statistics(Mac.mach_host_self(), 3, ticks, ref count) != 0) throw new InvalidOperationException();
            idle = ticks[2];
            total = (ulong)ticks[0] + ticks[1] + ticks[2] + ticks[3];
            return;
        }
        string[] f = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        ulong[] v = f.Skip(1).Take(8).Select(ulong.Parse).ToArray();
        idle = v[3] + (v.Length > 4 ? v[4] : 0);   // idle + iowait
        total = 0;
        foreach (ulong x in v) total += x;
    }

    // ---- RAM ----

    static void Ram(SystemSnapshot s)
    {
        double total, available;
        if (OperatingSystem.IsWindows())
        {
            var m = new Win.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Win.MEMORYSTATUSEX>() };
            if (!Win.GlobalMemoryStatusEx(ref m)) return;
            total = m.ullTotalPhys;
            available = m.ullAvailPhys;
        }
        else if (OperatingSystem.IsMacOS())
        {
            total = double.Parse(Run.Capture("/usr/sbin/sysctl", "-n hw.memsize", 3000)?.Trim() ?? "0", CultureInfo.InvariantCulture);
            string vm = Run.Capture("/usr/bin/vm_stat", "", 3000) ?? "";
            double page = double.Parse(Regex.Match(vm, @"page size of (\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
            double Pages(string name) => double.Parse(Regex.Match(vm, name + @":\s+(\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
            available = (Pages("Pages free") + Pages("Pages inactive") + Pages("Pages speculative")) * page;
        }
        else
        {
            var info = File.ReadLines("/proc/meminfo").Select(l => l.Split(':')).Where(p => p.Length == 2)
                .ToDictionary(p => p[0].Trim(), p => double.Parse(p[1].Trim().Split(' ')[0], CultureInfo.InvariantCulture) * 1024);
            total = info["MemTotal"];
            available = info.TryGetValue("MemAvailable", out double a) ? a : info["MemFree"];
        }
        if (total <= 0) return;
        s.RamTotalGb = total / 1e9;
        s.RamUsedGb = (total - available) / 1e9;
        s.RamPercent = 100 * (total - available) / total;
    }

    // ---- Disk ----

    static void Disk(SystemSnapshot s)
    {
        string root = OperatingSystem.IsWindows() ? Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\" : "/";
        var d = new DriveInfo(root);
        if (!d.IsReady || d.TotalSize <= 0) return;
        s.DiskTotalGb = d.TotalSize / 1e9;
        s.DiskUsedGb = (d.TotalSize - d.AvailableFreeSpace) / 1e9;
        s.DiskPercent = 100.0 * (d.TotalSize - d.AvailableFreeSpace) / d.TotalSize;
    }

    // ---- GPU ----

    void Gpu(SystemSnapshot s)
    {
        if (!nvidiaSearched)
        {
            nvidiaSearched = true;
            nvidiaSmi = Run.Which("nvidia-smi");
            if (nvidiaSmi == null && OperatingSystem.IsWindows())
            {
                string p = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
                if (File.Exists(p)) nvidiaSmi = p;
            }
        }
        if (nvidiaSmi != null)
        {
            string? o = Run.Capture(nvidiaSmi, "--query-gpu=name,utilization.gpu,temperature.gpu --format=csv,noheader,nounits", 4000);
            string[]? f = o?.Split('\n')[0].Split(',');
            if (f != null && f.Length >= 3)
            {
                s.GpuName = f[0].Trim();
                if (double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double u)) s.GpuPercent = u;
                if (double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double t)) s.GpuTempC = t;
                return;
            }
        }
        if (OperatingSystem.IsLinux()) LinuxGpu(s);
        else if (OperatingSystem.IsMacOS()) MacGpu(s);
        else if (OperatingSystem.IsWindows()) WindowsSensors(s);
    }

    static void LinuxGpu(SystemSnapshot s)
    {
        foreach (string card in Directory.GetDirectories("/sys/class/drm", "card?"))
        {
            string busy = Path.Combine(card, "device", "gpu_busy_percent");
            if (!File.Exists(busy)) continue;
            s.GpuName = "AMD GPU";
            s.GpuPercent = double.Parse(File.ReadAllText(busy).Trim(), CultureInfo.InvariantCulture);
            string hw = Path.Combine(card, "device", "hwmon");
            if (Directory.Exists(hw))
                foreach (string h in Directory.GetDirectories(hw))
                {
                    string t = Path.Combine(h, "temp1_input");
                    if (File.Exists(t)) { s.GpuTempC = double.Parse(File.ReadAllText(t).Trim(), CultureInfo.InvariantCulture) / 1000; break; }
                }
            return;
        }
    }

    static void MacGpu(SystemSnapshot s)
    {
        string? o = Run.Capture("/usr/sbin/ioreg", "-r -d 1 -w 0 -c IOAccelerator", 4000);
        if (o == null) return;
        Match m = Regex.Match(o, "\"Device Utilization %\"=(\\d+)");
        if (m.Success) s.GpuPercent = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        Match n = Regex.Match(o, "\"model\"\\s*=\\s*<?\"([^\"]+)\"");
        s.GpuName = n.Success ? n.Groups[1].Value : "GPU";
    }

    // ---- CPU temperature ----

    static void CpuTemp(SystemSnapshot s)
    {
        if (OperatingSystem.IsLinux())
        {
            string[] preferred = { "coretemp", "k10temp", "zenpower", "cpu_thermal", "acpitz" };
            var hwmons = Directory.Exists("/sys/class/hwmon") ? Directory.GetDirectories("/sys/class/hwmon") : Array.Empty<string>();
            foreach (string want in preferred)
                foreach (string h in hwmons)
                {
                    string nameFile = Path.Combine(h, "name"), temp = Path.Combine(h, "temp1_input");
                    if (!File.Exists(nameFile) || !File.Exists(temp) || File.ReadAllText(nameFile).Trim() != want) continue;
                    s.CpuTempC = double.Parse(File.ReadAllText(temp).Trim(), CultureInfo.InvariantCulture) / 1000;
                    s.CpuTempSource = want;
                    return;
                }
        }
        else if (OperatingSystem.IsWindows() && s.CpuTempC == null)
            WindowsSensors(s);
    }

    // LibreHardwareMonitor (or OpenHardwareMonitor) publishes its sensors through WMI while it runs.
    // Without it, ACPI thermal zones are tried (usually needs administrator rights).
    [SupportedOSPlatform("windows")]
    static void WindowsSensors(SystemSnapshot s)
    {
        foreach (string ns in new[] { @"root\LibreHardwareMonitor", @"root\OpenHardwareMonitor" })
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(ns, "SELECT Name, Identifier, SensorType, Value FROM Sensor");
                foreach (ManagementBaseObject o in searcher.Get())
                {
                    string name = o["Name"]?.ToString() ?? "", id = o["Identifier"]?.ToString() ?? "", type = o["SensorType"]?.ToString() ?? "";
                    if (o["Value"] is not float v) continue;
                    bool cpu = id.Contains("cpu", StringComparison.OrdinalIgnoreCase);
                    bool gpu = id.Contains("gpu", StringComparison.OrdinalIgnoreCase);
                    if (type == "Temperature" && cpu && s.CpuTempC == null &&
                        (name.Contains("Package") || name.Contains("Tctl") || name.Contains("Tdie") || name == "CPU Core"))
                    {
                        s.CpuTempC = v;
                        s.CpuTempSource = "LibreHardwareMonitor";
                    }
                    else if (type == "Temperature" && gpu && s.GpuTempC == null && name.Contains("Core"))
                        s.GpuTempC = v;
                    else if (type == "Load" && gpu && s.GpuPercent == null && name.Contains("Core"))
                    {
                        s.GpuPercent = v;
                        if (s.GpuName.Length == 0) s.GpuName = "GPU";
                    }
                }
                return;
            }
            catch (ManagementException) { /* that program is not running */ }
        }
        if (s.CpuTempC != null) return;
        try
        {
            using var acpi = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            foreach (ManagementBaseObject o in acpi.Get())
                if (o["CurrentTemperature"] is uint t && t > 2732)
                {
                    s.CpuTempC = t / 10.0 - 273.15;
                    s.CpuTempSource = "ACPI";
                    return;
                }
        }
        catch { /* not available without administrator rights */ }
    }

    // ---- native calls ----

    static class Win
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    }

    static class Mac
    {
        [DllImport("/usr/lib/libSystem.dylib")]
        public static extern uint mach_host_self();

        [DllImport("/usr/lib/libSystem.dylib")]
        public static extern int host_statistics(uint host, int flavor, uint[] info, ref int count);
    }
}
