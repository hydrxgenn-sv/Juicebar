using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MonitBat
{
    // Una colección HID (Windows crea una por cada "top-level collection" del dispositivo).
    class HidInfo
    {
        public string Path;
        public ushort Vid, Pid, UsagePage, Usage;
        public int InLen, OutLen, FeatLen;
        public string Product;
        public bool CanOpenRW;
    }

    // Acceso mínimo a HID mediante hid.dll y setupapi.dll (sin dependencias externas).
    static class Hid
    {
        const uint GENERIC_RW = 0xC0000000;
        const uint SHARE_RW = 3;
        const uint OPEN_EXISTING = 3;
        const uint FILE_FLAG_OVERLAPPED = 0x40000000;

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid ClassGuid; public int Flags; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort Version; }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 27)] public ushort[] Reserved;
        }

        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, out HIDP_CAPS c);
        [DllImport("hid.dll")] static extern bool HidD_GetProductString(SafeFileHandle h, byte[] buf, int len);
        [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, int index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr devInfo);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        static List<string> InterfacePaths()
        {
            var list = new List<string>();
            Guid g;
            HidD_GetHidGuid(out g);
            IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12); // DIGCF_PRESENT | DIGCF_DEVICEINTERFACE
            if (set == new IntPtr(-1)) return list;
            try
            {
                var data = new SP_DEVICE_INTERFACE_DATA();
                data.cbSize = Marshal.SizeOf(data);
                for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref data); i++)
                {
                    int required;
                    SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out required, IntPtr.Zero);
                    if (required < 8) continue; // dispositivo retirado durante la enumeración
                    IntPtr buf = Marshal.AllocHGlobal(required);
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6); // cbSize de SP_DEVICE_INTERFACE_DETAIL_DATA_W
                        if (SetupDiGetDeviceInterfaceDetail(set, ref data, buf, required, out required, IntPtr.Zero))
                            list.Add(Marshal.PtrToStringUni(buf + 4));
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return list;
        }

        // Lista las colecciones HID presentes. Si 'vids' no está vacío, solo las de esos fabricantes.
        public static List<HidInfo> Enumerate(bool testReadWrite, params ushort[] vids)
        {
            var result = new List<HidInfo>();
            foreach (string path in InterfacePaths())
            {
                // Acceso 0: permite leer atributos incluso de teclados y ratones que Windows tiene en exclusiva.
                using (SafeFileHandle h = CreateFile(path, 0, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid) continue;
                    var attr = new HIDD_ATTRIBUTES();
                    attr.Size = Marshal.SizeOf(attr);
                    if (!HidD_GetAttributes(h, ref attr)) continue;
                    if (vids.Length > 0 && Array.IndexOf(vids, attr.VendorID) < 0) continue;

                    var info = new HidInfo { Path = path, Vid = attr.VendorID, Pid = attr.ProductID };
                    IntPtr pre;
                    if (HidD_GetPreparsedData(h, out pre))
                    {
                        HIDP_CAPS caps;
                        HidP_GetCaps(pre, out caps);
                        HidD_FreePreparsedData(pre);
                        info.UsagePage = caps.UsagePage;
                        info.Usage = caps.Usage;
                        info.InLen = caps.InputReportByteLength;
                        info.OutLen = caps.OutputReportByteLength;
                        info.FeatLen = caps.FeatureReportByteLength;
                    }
                    var name = new byte[256];
                    if (HidD_GetProductString(h, name, name.Length))
                        info.Product = Encoding.Unicode.GetString(name).TrimEnd('\0');

                    if (testReadWrite)
                        using (SafeFileHandle rw = CreateFile(path, GENERIC_RW, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
                            info.CanOpenRW = !rw.IsInvalid;
                    result.Add(info);
                }
            }
            return result;
        }

        // Envía un informe de salida y espera un informe de entrada que cumpla 'match'.
        // Devuelve null si no llega respuesta dentro del tiempo indicado.
        public static byte[] Exchange(string path, byte[] output, int inLen, int timeoutMs, Func<byte[], bool> match)
        {
            using (SafeFileHandle h = CreateFile(path, GENERIC_RW, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero))
            {
                if (h.IsInvalid)
                    throw new IOException("no se puede abrir el dispositivo (error de Windows " + Marshal.GetLastWin32Error() + ")");
                using (var fs = new FileStream(h, FileAccess.ReadWrite, 1, true))
                {
                    fs.Write(output, 0, output.Length);
                    int deadline = Environment.TickCount + timeoutMs;
                    while (true)
                    {
                        int left = deadline - Environment.TickCount;
                        if (left <= 0) return null;
                        var buf = new byte[inLen];
                        IAsyncResult ar = fs.BeginRead(buf, 0, inLen, null, null);
                        if (!ar.AsyncWaitHandle.WaitOne(left)) return null; // al cerrar el handle se cancela la lectura pendiente
                        if (fs.EndRead(ar) > 0 && match(buf)) return buf;
                    }
                }
            }
        }

        public static string Hex(byte[] b)
        {
            return b == null ? "(sin respuesta)" : BitConverter.ToString(b).Replace('-', ' ');
        }
    }
}
