using System.Text;

namespace Juicebar.Core;

public enum DeviceKind { Mouse, Headset, Keyboard, Controller, Other }

public enum IconStyle { Number, Cells, Rings, Glyphs }

public enum WindowStyle { Glass, Rings, Segments, History }

public enum SystemMetric { None, Cpu, Ram, Gpu, GpuTemp, CpuTemp }

// Result of reading a device's battery. Without a percentage, Status explains why.
public sealed class BatteryReading
{
    public int Percent { get; init; } = -1;     // -1 = no data
    public bool Charging { get; init; }
    public bool Full { get; init; }
    public bool Asleep { get; init; }           // present but not answering (sleep)
    public bool Approximate { get; init; }      // estimated level (from voltage or coarse steps)
    public string Status { get; init; } = "";

    public bool HasPercent => Percent >= 0;

    public static BatteryReading Fail(string status, bool asleep = false) => new() { Status = status, Asleep = asleep };

    public static BatteryReading Level(int percent, bool charging = false, bool full = false, bool approximate = false) =>
        new() { Percent = Math.Clamp(percent, 0, 100), Charging = charging, Full = full, Approximate = approximate };
}

// One device seen by a source in one poll.
public sealed class DeviceReading
{
    public required string Id { get; init; }        // stable across sessions, e.g. "compx:f5f7"
    public required string Name { get; init; }
    public DeviceKind Kind { get; init; }
    public required string Source { get; init; }
    public required BatteryReading Battery { get; init; }
    public ushort Vid { get; init; }                // 0 when the source does not know it
    public ushort Pid { get; init; }
}

// One way of getting batteries: a HID protocol, an external tool or the operating system.
public interface IBatterySource
{
    string Name { get; }
    string Platforms { get; }       // shown in diagnostics
    bool IsSupported { get; }       // on this operating system
    TimeSpan Interval { get; }      // slow sources are polled less often
    IReadOnlyList<DeviceReading> Poll(HidInventory hid, StringBuilder? log);
}
