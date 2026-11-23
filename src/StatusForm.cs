using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MonitBat
{
    // Base de las ventanas flotantes: sin bordes, con sombra y esquinas redondeadas (Windows 11).
    // Esc o cerrar solo la ocultan.
    class FlyoutForm : Form
    {
        bool useRegion;   // Windows 10: sin esquinas redondeadas del sistema

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        protected float K { get { return DeviceDpi / 96f; } }

        // Tamaño a 96 ppp; las subclases lo definen y llaman a ApplySize() al final de su constructor.
        protected virtual Size LogicalSize { get { return new Size(320, 262); } }

        public FlyoutForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 10f);
        }

        protected void ApplySize()
        {
            // Size y no ClientSize: sin bordes son iguales, pero ClientSize antes de crear la ventana
            // suma la altura de un título que esta ventana no tiene.
            Size s = LogicalSize;
            Size = new Size((int)(s.Width * K), (int)(s.Height * K));
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplySize();
            int round = 2; // DWMWCP_ROUND (Windows 11)
            useRegion = DwmSetWindowAttribute(Handle, 33, ref round, 4) != 0;
            ApplyRegion();
        }

        void ApplyRegion()
        {
            if (!useRegion || Width == 0) return;
            using (GraphicsPath p = Gfx.Round(new RectangleF(0, 0, Width, Height), 8 * K))
                Region = new Region(p);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ApplyRegion();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Hide();
            base.OnKeyDown(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnFormClosing(e);
        }
    }

    // Ventana de estado junto a la bandeja. El dibujo depende del estilo elegido (WindowPainter).
    class StatusForm : FlyoutForm
    {
        readonly WindowPainter painter = new WindowPainter();

        public event Action SettingsRequested;   // clic en el engranaje

        public StatusForm()
        {
            Text = "MonitBat";
            ApplySize();
        }

        protected override Size LogicalSize
        {
            get { return new Size(WindowPainter.W, WindowPainter.HeightOf(painter.Style)); }
        }

        public void SetTheme(bool light)
        {
            painter.Light = light;
            Invalidate();
        }

        public void SetStyle(WindowStyle style)
        {
            painter.Style = style;
            ApplySize();
            if (Visible) PlaceNearTray();
            Invalidate();
        }

        public void SetValues(CardData m, CardData h, string time)
        {
            painter.Mouse = m;
            painter.Headset = h;
            painter.Time = time;
            Invalidate();
        }

        public void ShowNearTray()
        {
            Show();
            PlaceNearTray();
            Activate();
        }

        void PlaceNearTray()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 12, wa.Bottom - Height - 12);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Visible) PlaceNearTray();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            painter.Paint(e.Graphics, ClientSize.Width, ClientSize.Height, K);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool overClose = painter.CloseRect.Contains(e.Location), overGear = painter.GearRect.Contains(e.Location);
            if (overClose != painter.CloseHover || overGear != painter.GearHover)
            {
                painter.CloseHover = overClose;
                painter.GearHover = overGear;
                Cursor = overClose || overGear ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (painter.CloseHover || painter.GearHover)
            {
                painter.CloseHover = painter.GearHover = false;
                Cursor = Cursors.Default;
                Invalidate();
            }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (painter.CloseRect.Contains(e.Location)) Hide();
                else if (painter.GearRect.Contains(e.Location))
                {
                    Action h = SettingsRequested;
                    if (h != null) h();
                }
            }
            base.OnMouseClick(e);
        }
    }
}
