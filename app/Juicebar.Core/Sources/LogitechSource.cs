using System.Text;
using HidSharp;

namespace Juicebar.Core.Sources;

// Logitech HID++ 2.0 (VID 0x046D): mice and keyboards on Unifying/Bolt/Lightspeed receivers or connected directly.
// Uses long reports (ID 0x11, 20 bytes) on the usage page 0xFF00 collection.
//   request:  11 <device index> <feature index> <(function << 4) | swId> <parameters...>
//   0x0000 Root:            function 0 = index of a feature
//   0x1004 Unified Battery: function 1 = status (byte 4 %, byte 5 level step, byte 6 charging)
//   0x1000 Battery Status:  function 0 = level (byte 4 %, byte 6 state)
//   0x1001 Battery Voltage: function 0 = millivolts (bytes 4-5) -> approximate percent
//   0x0005 Device Name:     function 0 = length, 1 = name chunk, 2 = type
// Read-only. Coexists with G Hub (answers are matched by swId).
// Disabled on Linux: the kernel's hid-logitech-hidpp driver already publishes the battery (system source).
public sealed class LogitechSource : IBatterySource
{
    public const ushort Vid = 0x046D;
    const byte Long = 0x11, SwId = 0x0B;

    public string Name => "Logitech HID++ 2.0";
    public string Platforms => "Windows and macOS (on Linux the kernel provides it)";
    public bool IsSupported => !OperatingSystem.IsLinux();
    public TimeSpan Interval => TimeSpan.FromSeconds(20);

    sealed class Slot
    {
        public bool Probed, Exists;
        public byte Unified, Status, Voltage;
        public string Name = "";
        public DeviceKind Kind = DeviceKind.Other;
        public DateTime RetryAfter;
    }

    readonly Dictionary<string, Slot> slots = new();

    public IReadOnlyList<DeviceReading> Poll(HidInventory hid, StringBuilder? log)
    {
        var result = new List<DeviceReading>();
        foreach (HidCollection c in hid.Vendor(Vid).Where(c => c.HasPage(0xFF00) && c.HasOutput(Long)))
        {
            bool receiver = IsReceiver(c.Product);
            byte[] indices = receiver ? new byte[] { 1, 2, 3, 4, 5, 6 } : new byte[] { 0xFF };
            HidStream? s = null;
            try
            {
                s = c.Device.Open();
                foreach (byte idx in indices)
                {
                    string key = c.Path + "#" + idx;
                    if (!slots.TryGetValue(key, out Slot? slot)) slots[key] = slot = new Slot();
                    if (!slot.Exists && DateTime.UtcNow < slot.RetryAfter) continue;

                    BatteryReading? b = Read(s, c, idx, slot, log);
                    if (b == null)
                    {
                        if (!slot.Exists) { slot.RetryAfter = DateTime.UtcNow.AddSeconds(60); continue; }
                        b = BatteryReading.Fail(Loc.T("asleep_off"), asleep: true);
                    }
                    result.Add(new DeviceReading
                    {
                        Id = idx == 0xFF ? $"logitech:{c.Pid:x4}" : $"logitech:{c.Pid:x4}:{idx}",
                        Name = slot.Name.Length > 0 ? slot.Name : receiver ? Loc.F("name_logitech_slot", idx) : CleanProduct(c.Product),
                        Kind = slot.Kind,
                        Source = Name,
                        Battery = b,
                        Vid = idx == 0xFF ? Vid : (ushort)0,   // on receivers VID:PID belongs to the receiver, not the device
                        Pid = idx == 0xFF ? c.Pid : (ushort)0,
                    });
                }
            }
            catch (Exception ex) { log?.AppendLine($"  {c} error: {ex.Message}"); }
            finally { s?.Dispose(); }
        }
        return result;
    }

    static bool IsReceiver(string product) =>
        product.Contains("Receiver", StringComparison.OrdinalIgnoreCase) || product.Contains("Unifying", StringComparison.OrdinalIgnoreCase) ||
        product.Contains("Lightspeed", StringComparison.OrdinalIgnoreCase) || product.Contains("Bolt", StringComparison.OrdinalIgnoreCase);

    static string CleanProduct(string p) => p.Length > 0 ? p : Loc.T("name_logitech");

    BatteryReading? Read(HidStream s, HidCollection c, byte idx, Slot slot, StringBuilder? log)
    {
        if (!slot.Probed)
        {
            int unified = FeatureIndex(s, c, idx, 0x1004, log);
            if (unified < 0) return null;   // no device in that slot (or asleep and never seen)
            slot.Exists = true;
            slot.Unified = (byte)unified;
            if (slot.Unified == 0) slot.Status = (byte)Math.Max(0, FeatureIndex(s, c, idx, 0x1000, log));
            if (slot.Unified == 0 && slot.Status == 0) slot.Voltage = (byte)Math.Max(0, FeatureIndex(s, c, idx, 0x1001, log));
            ReadName(s, c, idx, slot, log);
            slot.Probed = true;
        }

        if (slot.Unified != 0)
        {
            byte[]? r = Call(s, c, idx, slot.Unified, 1, null, log);
            if (r == null) return null;
            int soc = r[4], level = r[5], charge = r[6];
            bool charging = charge == 1 || charge == 2, full = charge == 3;
            if (soc == 0 && level != 0)   // only reports coarse steps
            {
                int approx = (level & 8) != 0 ? 90 : (level & 4) != 0 ? 60 : (level & 2) != 0 ? 20 : 5;
                return BatteryReading.Level(approx, charging, full, approximate: true);
            }
            return BatteryReading.Level(soc, charging, full);
        }
        if (slot.Status != 0)
        {
            byte[]? r = Call(s, c, idx, slot.Status, 0, null, log);
            if (r == null) return null;
            int st = r[6];
            if (st == 5 || st == 6) return BatteryReading.Fail(Loc.T(st == 5 ? "invalid_battery" : "thermal_error"));
            return BatteryReading.Level(r[4], charging: st == 1 || st == 2 || st == 4, full: st == 3);
        }
        if (slot.Voltage != 0)
        {
            byte[]? r = Call(s, c, idx, slot.Voltage, 0, null, log);
            if (r == null) return null;
            int mv = (r[4] << 8) | r[5];
            return BatteryReading.Level(FromVoltage(mv), charging: (r[6] & 0x80) != 0, approximate: true);
        }
        return BatteryReading.Fail(Loc.T("no_battery_info"));
    }

    // Approximate discharge curve of a lithium cell (same idea Solaar uses).
    static readonly (int Mv, int Pct)[] Curve =
    {
        (4186, 100), (4067, 90), (3989, 80), (3922, 70), (3859, 60), (3811, 50),
        (3778, 40), (3751, 30), (3717, 20), (3671, 10), (3646, 5), (3579, 2), (3500, 0),
    };

    static int FromVoltage(int mv)
    {
        if (mv >= Curve[0].Mv) return 100;
        for (int i = 1; i < Curve.Length; i++)
            if (mv >= Curve[i].Mv)
            {
                var (hiMv, hiPct) = Curve[i - 1];
                var (loMv, loPct) = Curve[i];
                return loPct + (hiPct - loPct) * (mv - loMv) / (hiMv - loMv);
            }
        return 0;
    }

    void ReadName(HidStream s, HidCollection c, byte idx, Slot slot, StringBuilder? log)
    {
        int f = FeatureIndex(s, c, idx, 0x0005, log);
        if (f <= 0) return;
        byte[]? len = Call(s, c, idx, (byte)f, 0, null, log);
        if (len == null) return;
        var sb = new StringBuilder();
        for (int pos = 0; pos < len[4] && pos < 64; pos += 16)
        {
            byte[]? part = Call(s, c, idx, (byte)f, 1, new[] { (byte)pos }, log);
            if (part == null) break;
            for (int i = 4; i < 20 && sb.Length < len[4]; i++) if (part[i] != 0) sb.Append((char)part[i]);
        }
        slot.Name = sb.ToString().Trim();
        byte[]? type = Call(s, c, idx, (byte)f, 2, null, log);
        if (type != null)
            slot.Kind = type[4] switch
            {
                0 or 2 => DeviceKind.Keyboard,
                3 or 4 or 5 => DeviceKind.Mouse,
                _ => DeviceKind.Other,
            };
    }

    // Index of a feature: >0 supported, 0 not supported, -1 no answer.
    static int FeatureIndex(HidStream s, HidCollection c, byte idx, ushort feature, StringBuilder? log)
    {
        byte[]? r = Call(s, c, idx, 0x00, 0, new[] { (byte)(feature >> 8), (byte)feature }, log, allowError: true);
        if (r == null) return -1;
        if (r[2] == 0xFF) return 0;
        return r[4];
    }

    static byte[]? Call(HidStream s, HidCollection c, byte idx, byte feature, int function, byte[]? args, StringBuilder? log, bool allowError = false)
    {
        var req = new byte[20];
        req[0] = Long;
        req[1] = idx;
        req[2] = feature;
        req[3] = (byte)((function << 4) | SwId);
        if (args != null) Array.Copy(args, 0, req, 4, Math.Min(args.Length, 16));
        byte[]? r = HidIo.Exchange(s, c, req, 400, b =>
            b.Length >= 7 && b[0] == Long && b[1] == idx &&
            ((b[2] == feature && (b[3] & 0x0F) == SwId) || (b[2] == 0xFF && b[3] == feature)));
        log?.AppendLine($"  {c} dev {idx:X2} feature {feature:X2}/{function}: {HidIo.Hex(r)}");
        if (r == null) return null;
        if (r[2] == 0xFF && !allowError) return null;
        return r;
    }
}
