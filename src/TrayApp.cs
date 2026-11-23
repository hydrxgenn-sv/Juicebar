using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MonitBat
{
    class DeviceState
    {
        const string Reading = "Leyendo...";
        public readonly string Name;
        public readonly string Kind;      // "Ratón", "Auriculares"
        public readonly char Glyph;       // 'm', 'h' (color e icono)
        public BatteryReading Last = BatteryReading.Fail(Reading);
        public int LastPercent = -1;      // último porcentaje real leído en esta sesión
        public DateTime LastPercentTime;
        public bool Warned;

        // Icono propio en la bandeja (en los estilos combinados solo se ve el del ratón)
        public NotifyIcon Tray;
        public IntPtr IconHandle = IntPtr.Zero;
        public string IconKey;

        public DeviceState(string name, string kind, char glyph) { Name = name; Kind = kind; Glyph = glyph; }

        public void Update(BatteryReading r)
        {
            Last = r;
            if (r.HasPercent) { LastPercent = r.Percent; LastPercentTime = DateTime.Now; }
        }

        public string ShortText()
        {
            if (Last.HasPercent) return Last.Percent + "%" + (Last.Charging ? " (cargando)" : "");
            if (Last.Asleep) return LastPercent >= 0 ? "reposo, " + LastPercent + "%" : "en reposo";
            if (Last.Status == Reading) return "leyendo...";
            return Last.Status.StartsWith("Desconectado") ? "desconectado" : "no disponible";
        }

        // Datos para la tarjeta de la ventana de estado.
        public CardData Card()
        {
            var c = new CardData { Kind = Kind, Name = Name, Glyph = Glyph, Percent = IconPercent, Live = Last.HasPercent, Charging = Last.Charging };
            c.Low = c.Percent >= 0 && c.Percent < TrayApp.LowLevel && !c.Charging && c.Live;
            if (Last.HasPercent)
                c.Detail = Last.Charging ? "Cargando" : Last.Full ? "Cargada" : c.Low ? "Batería baja" : "Conectado";
            else if (Last.Asleep && LastPercent >= 0)
                c.Detail = "En reposo · último dato a las " + LastPercentTime.ToString("HH:mm");
            else
                c.Detail = Last.Status;
            return c;
        }

        // Datos para el icono de la bandeja.
        public IconInput Input()
        {
            return new IconInput { Percent = IconPercent, Live = Last.HasPercent, Charging = Last.HasPercent && Last.Charging, Glyph = Glyph };
        }

        // Valor que dibuja el icono: el actual o, si el dispositivo solo duerme, el último conocido.
        public int IconPercent
        {
            get { return Last.HasPercent ? Last.Percent : Last.Asleep ? LastPercent : -1; }
        }
    }

    class TrayApp : ApplicationContext
    {
        const int PollMs = 10000;
        public const int LowLevel = 20;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "MonitBat";

        readonly Settings settings = Settings.Load();
        readonly History history = new History();
        readonly StatusForm form;
        SettingsForm settingsForm;
        readonly ContextMenuStrip menu;
        readonly ToolStripMenuItem startupItem;
        readonly System.Threading.Timer timer;
        readonly EventWaitHandle showEvent;
        readonly RegisteredWaitHandle showWait;
        readonly DeviceState mouse = new DeviceState(ScyroxV8.Name, "Ratón", 'm');
        readonly DeviceState headset = new DeviceState(CorsairVoid.Name, "Auriculares", 'h');
        readonly DeviceState[] devices;
        int busy;
        bool lightTaskbar = ReadLightTaskbar();

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        public TrayApp(bool showAtStart)
        {
            devices = new[] { mouse, headset };
            history.Load();

            form = new StatusForm();
            form.SetTheme(lightTaskbar);
            form.SetStyle(settings.Window);
            form.SettingsRequested += ShowSettings;
            IntPtr forceHandle = form.Handle; // necesario para BeginInvoke desde el hilo de sondeo

            startupItem = new ToolStripMenuItem("Iniciar con Windows", null, (s, e) => ToggleStartup());
            startupItem.Checked = StartupEnabled();
            menu = new ContextMenuStrip();
            menu.Items.Add("Mostrar baterías", null, (s, e) => ShowForm());
            menu.Items.Add("Configuración...", null, (s, e) => ShowSettings());
            menu.Items.Add(startupItem);
            menu.Items.Add("Diagnóstico...", null, (s, e) => new DiagForm().Show());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Salir", null, (s, e) => Exit());

            // Un icono por periférico, en orden fijo (Windows recuerda la posición y si está fijado por orden de creación).
            foreach (DeviceState d in devices)
            {
                d.Tray = new NotifyIcon { ContextMenuStrip = menu, Text = d.Kind };
                d.Tray.MouseClick += (s, e) =>
                {
                    if (e.Button != MouseButtons.Left) return;
                    if (form.Visible) form.Hide(); else ShowForm();
                };
            }
            Render();

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemEvents.SessionEnded += OnSessionEnded;
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
            showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, OnShowRequested, null, -1, false);
            timer = new System.Threading.Timer(Poll, null, 0, PollMs);
            if (showAtStart) ShowForm();
        }

        // Otra ejecución del .exe (acceso directo o atajo) pide mostrar la ventana.
        void OnShowRequested(object state, bool timedOut)
        {
            try { form.BeginInvoke((Action)ShowForm); }
            catch (Exception ex) { Log.Write("mostrar ventana: " + ex.Message); }
        }

        static void OnSessionEnded(object sender, SessionEndedEventArgs e)
        {
            Log.Write("Windows termina la sesión: " + e.Reason);
        }

        void ShowForm()
        {
            form.ShowNearTray();
            ThreadPool.QueueUserWorkItem(Poll); // dato fresco al abrir
        }

        void ShowSettings()
        {
            if (settingsForm == null || settingsForm.IsDisposed)
            {
                settingsForm = new SettingsForm(settings);
                settingsForm.SetTheme(lightTaskbar);
                settingsForm.Changed += ApplySettings;
            }
            settingsForm.ShowCentered();
        }

        // Se eligió otro diseño en la configuración: se redibuja todo al instante.
        void ApplySettings()
        {
            form.SetStyle(settings.Window);
            foreach (DeviceState d in devices) d.IconKey = null;
            Render();
        }

        void Poll(object state)
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            try
            {
                var hid = Hid.Enumerate(false, ScyroxV8.Vid, CorsairVoid.Vid);
                BatteryReading m = ScyroxV8.Read(hid, null);
                BatteryReading h = CorsairVoid.Read(hid, null);
                form.BeginInvoke((Action)(() => Apply(m, h)));
            }
            catch (Exception ex) { Log.Write("sondeo: " + ex.Message); }
            finally { Interlocked.Exchange(ref busy, 0); }
        }

        void Apply(BatteryReading m, BatteryReading h)
        {
            mouse.Update(m);
            headset.Update(h);
            foreach (DeviceState d in devices)
            {
                if (d.Last.HasPercent) history.Add(d.Glyph, d.Last.Percent, d.Last.Charging);
                CheckLow(d);
            }
            form.SetValues(BuildCard(mouse), BuildCard(headset),
                           "Última actualización: " + DateTime.Now.ToString("HH:mm:ss"));
            Render();
        }

        CardData BuildCard(DeviceState d)
        {
            CardData c = d.Card();
            c.Points = history.Of(d.Glyph);
            c.Eta = EtaText(d);
            return c;
        }

        string EtaText(DeviceState d)
        {
            if (!d.Last.HasPercent) return d.Last.Asleep ? "En reposo" : "Sin conexión";
            if (d.Last.Charging) return "Cargando";
            TimeSpan? r = history.Remaining(d.Glyph);
            if (r == null) return "Estimando tiempo restante...";
            TimeSpan t = r.Value;
            if (t.TotalHours >= 10) return "≈ " + (int)Math.Round(t.TotalHours) + " h restantes";
            if (t.TotalHours >= 1) return "≈ " + (int)t.TotalHours + " h " + t.Minutes + " min restantes";
            return "≈ " + Math.Max(1, (int)t.TotalMinutes) + " min restantes";
        }

        void CheckLow(DeviceState d)
        {
            BatteryReading r = d.Last;
            if (!r.HasPercent) return;
            if (r.Percent < LowLevel && !r.Charging)
            {
                NotifyIcon t = d.Tray.Visible ? d.Tray : mouse.Tray; // en estilos combinados solo se ve el del ratón
                if (!d.Warned)
                    t.ShowBalloonTip(8000, "Batería baja", d.Name + ": " + r.Percent + "%", ToolTipIcon.Warning);
                d.Warned = true;
            }
            else if (r.Charging || r.Percent >= LowLevel + 5)
                d.Warned = false;
        }

        void Render()
        {
            int s = SystemInformation.SmallIconSize.Width;
            if (IconRenderer.PerDevice(settings.Icon))
            {
                foreach (DeviceState d in devices)
                {
                    SetTip(d.Tray, d.Kind + " · " + d.Name + ": " + d.ShortText());
                    IconInput x = d.Input();
                    string key = "n/" + x.Percent + "/" + x.Live + "/" + x.Charging + "/" + lightTaskbar + "/" + s;
                    SetIcon(d, key, () => IconRenderer.Number(x, lightTaskbar, s));
                    d.Tray.Visible = true;
                }
            }
            else
            {
                SetTip(mouse.Tray, "Ratón: " + mouse.ShortText() + "\nAuriculares: " + headset.ShortText());
                IconInput a = mouse.Input(), b = headset.Input();
                string key = settings.Icon + "/" + a.Percent + a.Live + a.Charging + "/" + b.Percent + b.Live + b.Charging + "/" + lightTaskbar + "/" + s;
                SetIcon(mouse, key, () => IconRenderer.Combined(settings.Icon, a, b, lightTaskbar, s));
                mouse.Tray.Visible = true;
                headset.Tray.Visible = false;
            }
        }

        static void SetTip(NotifyIcon t, string tip)
        {
            t.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip; // límite de NotifyIcon
        }

        static void SetIcon(DeviceState d, string key, Func<Bitmap> draw)
        {
            if (key == d.IconKey) return;
            d.IconKey = key;
            IntPtr old = d.IconHandle;
            using (Bitmap bmp = draw()) d.IconHandle = bmp.GetHicon();
            d.Tray.Icon = Icon.FromHandle(d.IconHandle);
            if (old != IntPtr.Zero) DestroyIcon(old);
        }

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // Cambio de tema claro/oscuro de la barra de tareas
            if (e.Category != UserPreferenceCategory.General) return;
            try
            {
                form.BeginInvoke((Action)(() =>
                {
                    lightTaskbar = ReadLightTaskbar();
                    form.SetTheme(lightTaskbar);
                    if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.SetTheme(lightTaskbar);
                    Render();
                }));
            }
            catch (Exception ex) { Log.Write("cambio de tema: " + ex.Message); }
        }

        static bool ReadLightTaskbar()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("SystemUsesLightTheme");
                    return v is int && (int)v == 1;
                }
            }
            catch { return false; }
        }

        static bool StartupEnabled()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(RunValue) != null;
        }

        void ToggleStartup()
        {
            bool enable = !startupItem.Checked;
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enable) k.SetValue(RunValue, "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue(RunValue, false);
            }
            startupItem.Checked = enable;
        }

        void Exit()
        {
            Log.Write("el usuario eligió Salir");
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemEvents.SessionEnded -= OnSessionEnded;
            showWait.Unregister(null);
            showEvent.Dispose();
            timer.Dispose();
            foreach (DeviceState d in devices)
            {
                d.Tray.Visible = false;
                d.Tray.Dispose();
                if (d.IconHandle != IntPtr.Zero) DestroyIcon(d.IconHandle);
            }
            if (settingsForm != null) settingsForm.Dispose();
            form.Dispose();
            ExitThread();
        }
    }
}
