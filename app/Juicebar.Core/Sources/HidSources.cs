using System.Text;

namespace Juicebar.Core.Sources;

// Mice with a Compx chip (VID 0x3554: Scyrox and other brands using the same chip).
// Vendor HID protocol: 17-byte output/input report, ID 0x08, usage page 0xFF02.
//   request:  08 <cmd> 00 00 00 <len> <data...> <checksum>      checksum = 0x55 - sum(bytes 0..15)
//   cmd 0x03: link state with the mouse (answered by the receiver itself)
//   cmd 0x04: battery -> byte 6 = percent, byte 7 = 0 discharging / 1 charging / 2 full
public sealed class CompxSource : IBatterySource
{
    public const ushort Vid = 0x3554;
    static readonly Dictionary<ushort, string> Models = new() { [0xF5F7] = "Scyrox V8" };

    public string Name => "Compx-chip mice (Scyrox and others)";
    public string Platforms => "Windows, Linux, macOS";
    public bool IsSupported => true;
    public TimeSpan Interval => TimeSpan.FromSeconds(10);

    static byte[] Packet(byte cmd)
    {
        var p = new byte[17];
        p[0] = 0x08;
        p[1] = cmd;
        p[16] = Checksum(p);
        return p;
    }

    static byte Checksum(byte[] p)
    {
        int sum = 0;
        for (int i = 0; i < 16; i++) sum += p[i];
        return (byte)(0x55 - sum);
    }

    static Func<byte[], bool> Reply(byte cmd) => r => r.Length >= 17 && r[0] == 0x08 && r[1] == cmd && r[16] == Checksum(r);

    public IReadOnlyList<DeviceReading> Poll(HidInventory hid, StringBuilder? log)
    {
        var result = new List<DeviceReading>();
        var candidates = hid.Vendor(Vid).Where(c => c.HasPage(0xFF02) && (c.HasOutput(0x08) || c.MaxOut == 17));
        foreach (var group in candidates.GroupBy(c => c.Pid))
        {
            BatteryReading reading = BatteryReading.Fail(Loc.T("no_receiver_reply"));
            foreach (HidCollection c in group)
            {
                reading = ReadOne(c, log);
                if (reading.HasPercent) break;
            }
            result.Add(new DeviceReading
            {
                Id = $"compx:{group.Key:x4}",
                Name = ModelName(group.Key, group.First().Product),
                Kind = DeviceKind.Mouse,
                Source = Name,
                Battery = reading,
                Vid = Vid,
                Pid = group.Key,
            });
        }
        return result;
    }

    static BatteryReading ReadOne(HidCollection c, StringBuilder? log)
    {
        try
        {
            byte[]? bat = HidIo.Exchange(c, Packet(0x04), 1500, Reply(0x04));
            log?.AppendLine($"  {c} battery (cmd 04): {HidIo.Hex(bat)}");
            if (bat != null && bat[2] == 0 && bat[6] <= 100)
                return BatteryReading.Level(bat[6], charging: bat[7] == 1, full: bat[7] == 2);

            // No battery: the link state (answered by the receiver) explains why.
            byte[]? link = HidIo.Exchange(c, Packet(0x03), 600, Reply(0x03));
            log?.AppendLine($"  {c} link (cmd 03): {HidIo.Hex(link)}");
            return link == null
                ? BatteryReading.Fail(Loc.T("no_receiver_reply"))
                : BatteryReading.Fail(Loc.T("asleep_move"), asleep: true);
        }
        catch (Exception ex)
        {
            log?.AppendLine($"  {c} error: {ex.Message}");
            return BatteryReading.Fail(Loc.F("unavailable_err", ex.Message));
        }
    }

    static string ModelName(ushort pid, string product)
    {
        if (Models.TryGetValue(pid, out string? name)) return name;
        string s = product.Replace("Dongle", "", StringComparison.OrdinalIgnoreCase)
                          .Replace("Receiver", "", StringComparison.OrdinalIgnoreCase).Trim();
        return s.Length > 0 ? s : Loc.T("name_compx");
    }
}

// Corsair Void headsets (VID 0x1B1C). The USB receiver exposes a collection on usage page 0xFFC5:
//   request:  output report C9 64
//   answer:   input report ID 0x64, 5 bytes
//     byte 2: bits 0-6 = percent      byte 3: 177 connected, 16 wired, 38 connecting, other = off
//     byte 4: 0 no data, 1 normal, 2 low, 3 critical, 4 full, 5 charging
// Same protocol as HeadsetControl and Linux's hid-corsair-void driver. iCUE is not needed.
public sealed class CorsairVoidSource : IBatterySource
{
    public const ushort Vid = 0x1B1C;
    static readonly Dictionary<ushort, string> Models = new() { [0x0A51] = "Corsair Void Elite" };

    public string Name => "Corsair Void headsets";
    public string Platforms => "Windows, Linux, macOS";
    public bool IsSupported => true;
    public TimeSpan Interval => TimeSpan.FromSeconds(10);

    public IReadOnlyList<DeviceReading> Poll(HidInventory hid, StringBuilder? log)
    {
        var result = new List<DeviceReading>();
        var candidates = hid.Vendor(Vid).Where(c => c.HasPage(0xFFC5) && c.MaxOut >= 2 && (c.HasInput(0x64) || c.MaxIn >= 5));
        foreach (var group in candidates.GroupBy(c => c.Pid))
        {
            BatteryReading reading = BatteryReading.Fail(Loc.T("no_receiver_reply"));
            foreach (HidCollection c in group)
            {
                reading = ReadOne(c, log);
                if (reading.HasPercent) break;
            }
            result.Add(new DeviceReading
            {
                Id = $"corsair:{group.Key:x4}",
                Name = ModelName(group.Key, group.First().Product),
                Kind = DeviceKind.Headset,
                Source = Name,
                Battery = reading,
                Vid = Vid,
                Pid = group.Key,
            });
        }
        return result;
    }

    static BatteryReading ReadOne(HidCollection c, StringBuilder? log)
    {
        try
        {
            byte[]? r = HidIo.Exchange(c, new byte[] { 0xC9, 0x64 }, 1500, b => b.Length >= 5 && b[0] == 0x64);
            log?.AppendLine($"  {c} state (C9 64): {HidIo.Hex(r)}");
            if (r == null) return BatteryReading.Fail(Loc.T("no_receiver_reply"));
            int link = r[3], status = r[4];
            if (link != 16 && link != 177)
                return BatteryReading.Fail(Loc.T(link == 38 ? "connecting" : "disconnected_off"));
            if (status == 0 || status > 5) return BatteryReading.Fail(Loc.T("unavailable"));
            return BatteryReading.Level(r[2] & 0x7F, charging: status == 5, full: status == 4);
        }
        catch (Exception ex)
        {
            log?.AppendLine($"  {c} error: {ex.Message}");
            return BatteryReading.Fail(Loc.F("unavailable_err", ex.Message));
        }
    }

    static string ModelName(ushort pid, string product)
    {
        if (Models.TryGetValue(pid, out string? name)) return name;
        string s = product.Replace("Gaming Dongle", "", StringComparison.OrdinalIgnoreCase)
                          .Replace("Dongle", "", StringComparison.OrdinalIgnoreCase).Trim();
        return s.Length > 0 ? s : Loc.T("name_corsair");
    }
}
