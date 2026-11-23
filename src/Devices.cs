using System;
using System.Collections.Generic;
using System.Text;

namespace MonitBat
{
    class BatteryReading
    {
        public int Percent = -1;      // -1 = no disponible
        public bool Charging;
        public bool Full;
        public bool Asleep;           // el dispositivo existe pero no responde (reposo)
        public string Status;         // motivo cuando no hay porcentaje

        public bool HasPercent { get { return Percent >= 0; } }

        public static BatteryReading Fail(string status)
        {
            return new BatteryReading { Status = status };
        }
    }

    // Scyrox V8: receptor 2,4 GHz con chip Compx (VID 0x3554). Protocolo propietario por HID:
    // informe de salida/entrada de 17 bytes, ID 0x08, en la colección de página de uso 0xFF02.
    //   petición:  08 <cmd> 00 00 00 <len> <datos...> <checksum>
    //   checksum:  0x55 - suma(bytes 0..15)
    //   cmd 0x03:  estado del enlace con el ratón (byte 6: 1 = conectado)
    //   cmd 0x04:  batería -> byte 6 = porcentaje, byte 7 = 0 descarga / 1 cargando / 2 llena
    // Con cable USB el ratón aparece con otro PID del mismo fabricante y habla el mismo protocolo.
    static class ScyroxV8
    {
        public const string Name = "Scyrox V8";
        public const ushort Vid = 0x3554;

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

        static Func<byte[], bool> Reply(byte cmd)
        {
            return r => r.Length == 17 && r[0] == 0x08 && r[1] == cmd && r[16] == Checksum(r);
        }

        public static List<HidInfo> Candidates(List<HidInfo> hid)
        {
            return hid.FindAll(d => d.Vid == Vid && d.UsagePage == 0xFF02 && d.OutLen == 17 && d.InLen == 17);
        }

        public static BatteryReading Read(List<HidInfo> hid, StringBuilder log)
        {
            var candidates = Candidates(hid);
            if (candidates.Count == 0)
                return BatteryReading.Fail("Desconectado (receptor no encontrado)");

            BatteryReading result = null;
            foreach (HidInfo dev in candidates)
            {
                try
                {
                    byte[] bat = Hid.Exchange(dev.Path, Packet(0x04), 17, 1500, Reply(0x04));
                    Log(log, dev, "batería (cmd 04)", bat);
                    if (bat != null && bat[2] == 0 && bat[6] <= 100)
                        return new BatteryReading { Percent = bat[6], Charging = bat[7] == 1, Full = bat[7] == 2 };

                    // Sin batería: el estado del enlace (lo responde el propio receptor) explica por qué.
                    byte[] link = Hid.Exchange(dev.Path, Packet(0x03), 17, 600, Reply(0x03));
                    Log(log, dev, "estado del enlace (cmd 03)", link);
                    if (link == null)
                        result = BatteryReading.Fail("Sin respuesta del receptor");
                    else
                    {
                        result = BatteryReading.Fail("En reposo o apagado (mueve el ratón)");
                        result.Asleep = true;
                    }
                }
                catch (Exception ex)
                {
                    if (log != null) log.AppendLine("  error: " + ex.Message);
                    result = BatteryReading.Fail("Batería no disponible (" + ex.Message + ")");
                }
            }
            return result;
        }

        static void Log(StringBuilder log, HidInfo dev, string what, byte[] reply)
        {
            if (log == null) return;
            log.AppendLine(string.Format("  {0:X4}:{1:X4} {2}: {3}", dev.Vid, dev.Pid, what, Hid.Hex(reply)));
        }
    }

    // Corsair Void Elite Wireless (VID 0x1B1C). El receptor USB expone una colección de página 0xFFC5:
    //   petición:  informe de salida C9 64 (relleno hasta el tamaño del informe)
    //   respuesta: informe de entrada de 5 bytes con ID 0x64
    //     byte 2: bits 0-6 = porcentaje, bit 7 = micrófono levantado
    //     byte 3: 177 = auricular conectado, 16 = cable, 38 = conectando, otro (51, 52...) = apagado/buscando
    //     byte 4: 0 = sin datos, 1 = normal, 2 = baja, 3 = crítica, 4 = cargada, 5 = cargando
    // Mismo protocolo que usan HeadsetControl y el driver hid-corsair-void de Linux. No requiere iCUE.
    static class CorsairVoid
    {
        public const string Name = "Corsair Void Elite";
        public const ushort Vid = 0x1B1C;

        public static List<HidInfo> Candidates(List<HidInfo> hid)
        {
            return hid.FindAll(d => d.Vid == Vid && d.UsagePage == 0xFFC5 && d.OutLen >= 2 && d.InLen >= 5);
        }

        public static BatteryReading Read(List<HidInfo> hid, StringBuilder log)
        {
            var candidates = Candidates(hid);
            if (candidates.Count == 0)
                return BatteryReading.Fail("Desconectado (receptor no encontrado)");

            BatteryReading result = null;
            foreach (HidInfo dev in candidates)
            {
                try
                {
                    var req = new byte[dev.OutLen];
                    req[0] = 0xC9;
                    req[1] = 0x64;
                    byte[] r = Hid.Exchange(dev.Path, req, dev.InLen, 1500, b => b[0] == 0x64);
                    if (log != null)
                        log.AppendLine(string.Format("  {0:X4}:{1:X4} estado (C9 64): {2}", dev.Vid, dev.Pid, Hid.Hex(r)));
                    if (r == null)
                    {
                        result = BatteryReading.Fail("Sin respuesta del receptor");
                        continue;
                    }

                    int link = r[3], status = r[4];
                    bool wired = link == 16;
                    if (!wired && link != 177)
                    {
                        result = BatteryReading.Fail(link == 38 ? "Conectando..." : "Desconectado (apagado o fuera de alcance)");
                        continue;
                    }
                    if (status == 0 || status > 5)
                    {
                        result = BatteryReading.Fail("Batería no disponible");
                        continue;
                    }
                    return new BatteryReading { Percent = r[2] & 0x7F, Charging = status == 5, Full = status == 4 };
                }
                catch (Exception ex)
                {
                    if (log != null) log.AppendLine("  error: " + ex.Message);
                    result = BatteryReading.Fail("Batería no disponible (" + ex.Message + ")");
                }
            }
            return result;
        }
    }
}
