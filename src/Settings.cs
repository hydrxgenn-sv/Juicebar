using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MonitBat
{
    // Estilos elegibles en la configuración.
    enum IconStyle { Numero, Pilas, Anillos, Glifos }
    enum WindowStyle { Cristal, Anillos, Segmentos, Historial }

    static class AppPaths
    {
        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonitBat");
    }

    // Preferencias del usuario en %LOCALAPPDATA%\MonitBat\config.ini (líneas clave=valor).
    class Settings
    {
        public IconStyle Icon = IconStyle.Numero;
        public WindowStyle Window = WindowStyle.Cristal;

        static string FilePath { get { return Path.Combine(AppPaths.Dir, "config.ini"); } }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();
                    IconStyle i;
                    WindowStyle w;
                    if (key == "icono" && Enum.TryParse(val, true, out i) && Enum.IsDefined(typeof(IconStyle), i)) s.Icon = i;
                    if (key == "ventana" && Enum.TryParse(val, true, out w) && Enum.IsDefined(typeof(WindowStyle), w)) s.Window = w;
                }
            }
            catch (Exception ex) { Log.Write("leer config: " + ex.Message); }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Dir);
                File.WriteAllText(FilePath,
                    "icono=" + Icon.ToString().ToLowerInvariant() + "\r\n" +
                    "ventana=" + Window.ToString().ToLowerInvariant() + "\r\n",
                    new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Write("guardar config: " + ex.Message); }
        }
    }

    struct HistoryPoint
    {
        public DateTime Time;
        public int Percent;
        public bool Charging;
    }

    // Lecturas de las últimas 24 h de cada dispositivo, en %LOCALAPPDATA%\MonitBat\historial.csv
    // (una línea por cambio: fecha;dispositivo;porcentaje;cargando). Se guarda siempre, para que la
    // ventana Historial tenga datos aunque se elija más tarde.
    class History
    {
        public static readonly TimeSpan Span = TimeSpan.FromHours(24);
        const string Fmt = "yyyy-MM-ddTHH:mm:ss";
        readonly Dictionary<char, List<HistoryPoint>> data = new Dictionary<char, List<HistoryPoint>>();
        readonly string path = Path.Combine(AppPaths.Dir, "historial.csv");

        public List<HistoryPoint> Of(char device)
        {
            List<HistoryPoint> l;
            if (!data.TryGetValue(device, out l)) { l = new List<HistoryPoint>(); data[device] = l; }
            return l;
        }

        // Carga el archivo y lo reescribe sin las líneas de más de 24 h.
        public void Load()
        {
            try
            {
                if (!File.Exists(path)) return;
                DateTime from = DateTime.Now - Span;
                var keep = new StringBuilder();
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] f = line.Split(';');
                    DateTime t;
                    int p;
                    if (f.Length != 4 || f[1].Length != 1) continue;
                    if (!DateTime.TryParseExact(f[0], Fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) continue;
                    if (!int.TryParse(f[2], out p) || t < from) continue;
                    Of(f[1][0]).Add(new HistoryPoint { Time = t, Percent = p, Charging = f[3] == "1" });
                    keep.AppendLine(line);
                }
                File.WriteAllText(path, keep.ToString());
            }
            catch (Exception ex) { Log.Write("leer historial: " + ex.Message); }
        }

        // Registra la lectura si cambió el porcentaje o la carga, o cada 30 min aunque no cambie.
        public void Add(char device, int percent, bool charging)
        {
            List<HistoryPoint> l = Of(device);
            DateTime now = DateTime.Now;
            if (l.Count > 0)
            {
                HistoryPoint last = l[l.Count - 1];
                if (last.Percent == percent && last.Charging == charging && now - last.Time < TimeSpan.FromMinutes(30)) return;
            }
            l.Add(new HistoryPoint { Time = now, Percent = percent, Charging = charging });
            while (l.Count > 0 && l[0].Time < now - Span) l.RemoveAt(0);
            try
            {
                Directory.CreateDirectory(AppPaths.Dir);
                File.AppendAllText(path, now.ToString(Fmt, CultureInfo.InvariantCulture) + ";" + device + ";" +
                    percent + ";" + (charging ? "1" : "0") + Environment.NewLine);
            }
            catch (Exception ex) { Log.Write("guardar historial: " + ex.Message); }
        }

        // Tiempo restante con el ritmo de descarga desde la última carga (o la última subida de nivel).
        // Null si todavía no hay al menos 30 min y 2 % de descarga medidos.
        public TimeSpan? Remaining(char device)
        {
            List<HistoryPoint> l = Of(device);
            if (l.Count < 2) return null;
            HistoryPoint end = l[l.Count - 1];
            if (end.Charging) return null;
            int i = l.Count - 1;
            while (i > 0 && !l[i - 1].Charging && l[i - 1].Percent >= l[i].Percent) i--;
            HistoryPoint start = l[i];
            double hours = (end.Time - start.Time).TotalHours;
            int drop = start.Percent - end.Percent;
            if (hours < 0.5 || drop < 2) return null;
            double left = end.Percent / (drop / hours);
            if (left > 24 * 14) return null;
            return TimeSpan.FromHours(left);
        }
    }
}
