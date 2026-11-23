using System;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MonitBat
{
    // Informe de texto: qué ve Windows y qué responde cada dispositivo.
    class DiagForm : Form
    {
        readonly TextBox box;

        public DiagForm()
        {
            Text = "MonitBat - Diagnóstico";
            Size = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            box = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9.5f),
                Text = "Analizando dispositivos..."
            };
            Controls.Add(box);
            var refresh = new Button { Text = "Repetir (F5)", Dock = DockStyle.Bottom, Height = 30 };
            refresh.Click += (s, e) => Run();
            Controls.Add(refresh);
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.F5) Run(); };
            Load += (s, e) => Run();
        }

        void Run()
        {
            box.Text = "Analizando dispositivos...";
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string report;
                try { report = Diagnostics.Report(); }
                catch (Exception ex) { report = "Error: " + ex; }
                try { BeginInvoke((Action)(() => { box.Text = report; box.Select(0, 0); })); }
                catch (InvalidOperationException) { } // la ventana se cerró durante el análisis
            });
        }
    }

    static class Diagnostics
    {
        public static string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine("MonitBat - diagnóstico   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Windows " + Environment.OSVersion.Version + (Environment.Is64BitOperatingSystem ? " (64 bits)" : ""));
            sb.AppendLine();

            sb.AppendLine("Dispositivos HID presentes (VID:PID  nombre):");
            foreach (var g in Hid.Enumerate(false).GroupBy(d => d.Vid * 65536 + d.Pid).OrderBy(g => g.Key))
            {
                HidInfo d = g.First();
                string tag = d.Vid == ScyroxV8.Vid ? "   <- Compx (Scyrox)" : d.Vid == CorsairVoid.Vid ? "   <- Corsair" : "";
                sb.AppendLine(string.Format("  {0:X4}:{1:X4}  {2}{3}", d.Vid, d.Pid, d.Product ?? "(sin nombre)", tag));
            }
            sb.AppendLine();

            var ours = Hid.Enumerate(true, ScyroxV8.Vid, CorsairVoid.Vid);
            sb.AppendLine("Colecciones HID de VID 3554 (Compx/Scyrox) y VID 1B1C (Corsair):");
            if (ours.Count == 0) sb.AppendLine("  ninguna");
            foreach (HidInfo d in ours)
            {
                sb.AppendLine(string.Format("  {0:X4}:{1:X4}  página {2:X4} uso {3:X4}  entrada {4,2}  salida {5,2}  feature {6,2}  lectura/escritura: {7}",
                    d.Vid, d.Pid, d.UsagePage, d.Usage, d.InLen, d.OutLen, d.FeatLen, d.CanOpenRW ? "sí" : "no"));
                sb.AppendLine("      " + d.Path);
            }
            sb.AppendLine();

            sb.AppendLine("== " + ScyroxV8.Name + " ==");
            sb.AppendLine("Método: protocolo HID del chip Compx (informe 0x08 de 17 bytes, página 0xFF02): cmd 04 = batería, cmd 03 = enlace.");
            sb.AppendLine("Colecciones candidatas: " + ScyroxV8.Candidates(ours).Count);
            var log = new StringBuilder();
            BatteryReading m = ScyroxV8.Read(ours, log);
            sb.Append(log);
            sb.AppendLine("Resultado: " + Describe(m));
            if (ScyroxV8.Candidates(ours).Count == 0)
                sb.AppendLine("  -> Conecta el receptor 2,4 GHz o el cable. Si aparece con otro VID, el V8 usa otro chip y este método no aplica.");
            else if (m.Asleep)
                sb.AppendLine("  -> El receptor responde pero el ratón no: está en reposo o apagado. Muévelo y pulsa F5.");
            sb.AppendLine();

            sb.AppendLine("== " + CorsairVoid.Name + " ==");
            sb.AppendLine("Método: protocolo HID Corsair Void (salida C9 64 -> entrada 0x64, página 0xFFC5). No necesita iCUE.");
            sb.AppendLine("Colecciones candidatas: " + CorsairVoid.Candidates(ours).Count);
            log = new StringBuilder();
            BatteryReading h = CorsairVoid.Read(ours, log);
            sb.Append(log);
            sb.AppendLine("Resultado: " + Describe(h));
            if (CorsairVoid.Candidates(ours).Count == 0)
                sb.AppendLine("  -> Conecta el receptor USB del auricular.");
            else if (!h.HasPercent)
                sb.AppendLine("  -> Byte 3 de la respuesta: 177 conectado, 16 cable, 38 conectando, otro valor (51, 52...) = apagado o buscando. Enciende el auricular y pulsa F5.");
            sb.AppendLine();
            sb.AppendLine("Si aparece «error de Windows 5» o «32», otro programa tiene el dispositivo en exclusiva (cierra iCUE o el software del ratón y repite).");
            return sb.ToString();
        }

        static string Describe(BatteryReading r)
        {
            if (r.HasPercent)
                return r.Percent + "%" + (r.Charging ? " (cargando)" : r.Full ? " (cargada)" : "");
            return r.Status;
        }
    }
}
