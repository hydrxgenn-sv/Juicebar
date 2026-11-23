using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MonitBat
{
    // Configuración: elegir el diseño del icono de la bandeja y el de la ventana de detalles.
    // Cada opción muestra una vista previa con datos de ejemplo; el cambio se aplica y se guarda al instante.
    class SettingsForm : FlyoutForm
    {
        const int W = 640, H = 516;
        static readonly IconStyle[] IconStyles = { IconStyle.Numero, IconStyle.Pilas, IconStyle.Anillos, IconStyle.Glifos };
        static readonly string[] IconNames = { "Número por periférico", "Pilas", "Anillos dobles", "Glifos" };
        static readonly WindowStyle[] WinStyles = { WindowStyle.Cristal, WindowStyle.Anillos, WindowStyle.Segmentos, WindowStyle.Historial };
        static readonly string[] WinNames = { "Cristal", "Anillos", "Segmentos", "Historial" };

        readonly Settings settings;
        readonly RectangleF[] iconTiles = new RectangleF[4], winTiles = new RectangleF[4];
        readonly Bitmap[] thumbs = new Bitmap[4];
        RectangleF closeRect;
        int hoverIcon = -1, hoverWin = -1;
        bool hoverClose, light;

        public event Action Changed;

        public SettingsForm(Settings s)
        {
            settings = s;
            Text = "MonitBat - Configuración";
            ApplySize();
        }

        protected override Size LogicalSize { get { return new Size(W, H); } }

        public void SetTheme(bool lightTheme)
        {
            light = lightTheme;
            DisposeThumbs();
            Invalidate();
        }

        public void ShowCentered()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + (wa.Height - Height) / 2);
            Show();
            Activate();
        }

        // ---- datos de ejemplo para las vistas previas ----

        static CardData[] Sample()
        {
            return new[]
            {
                new CardData { Kind = "Ratón", Name = ScyroxV8.Name, Glyph = 'm', Percent = 82, Live = true,
                               Detail = "Conectado", Eta = "≈ 38 h restantes", Points = Curve(100, 82) },
                new CardData { Kind = "Auriculares", Name = CorsairVoid.Name, Glyph = 'h', Percent = 14, Live = true, Low = true,
                               Detail = "Batería baja", Eta = "≈ 1 h 40 min restantes", Points = Curve(90, 14) }
            };
        }

        static List<HistoryPoint> Curve(int from, int to)
        {
            var l = new List<HistoryPoint>();
            DateTime start = DateTime.Now.AddHours(-23);
            for (int i = 0; i <= 23; i++)
            {
                double t = i / 23.0;
                l.Add(new HistoryPoint { Time = start.AddHours(i), Percent = (int)Math.Round(from + (to - from) * t) });
            }
            return l;
        }

        Bitmap Thumb(int i)
        {
            if (thumbs[i] == null)
            {
                CardData[] s = Sample();
                thumbs[i] = WindowPainter.Render(WinStyles[i], light, s[0], s[1], "Última actualización: 23:20:05");
            }
            return thumbs[i];
        }

        void DisposeThumbs()
        {
            for (int i = 0; i < thumbs.Length; i++)
                if (thumbs[i] != null) { thumbs[i].Dispose(); thumbs[i] = null; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) DisposeThumbs();
            base.Dispose(disposing);
        }

        // ---- dibujo ----

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float k = K;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            Color bg = light ? Color.FromArgb(243, 243, 243) : Color.FromArgb(32, 32, 32);
            Color border = light ? Color.FromArgb(214, 214, 214) : Color.FromArgb(64, 64, 64);
            Color text = light ? Color.FromArgb(26, 26, 26) : Color.White;
            Color sub = light ? Color.FromArgb(104, 104, 104) : Color.FromArgb(166, 166, 166);

            g.Clear(bg);
            using (var pen = new Pen(border))
            using (GraphicsPath p = Gfx.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 8 * k))
                g.DrawPath(pen, p);

            using (var f = new Font("Segoe UI Semibold", 13f))
                WindowPainter.DrawText(g, "Configuración", f, text, new RectangleF(20 * k, 12 * k, 300 * k, 30 * k), StringAlignment.Near, StringAlignment.Near);

            closeRect = new RectangleF((W - 42) * k, 12 * k, 28 * k, 28 * k);
            if (hoverClose)
                using (var b = new SolidBrush(light ? Color.FromArgb(30, 0, 0, 0) : Color.FromArgb(40, 255, 255, 255)))
                using (GraphicsPath p = Gfx.Round(closeRect, 5 * k))
                    g.FillPath(b, p);
            using (var pen = new Pen(sub, 1.4f * k))
            {
                float i = 10 * k;
                g.DrawLine(pen, closeRect.Left + i, closeRect.Top + i, closeRect.Right - i, closeRect.Bottom - i);
                g.DrawLine(pen, closeRect.Right - i, closeRect.Top + i, closeRect.Left + i, closeRect.Bottom - i);
            }

            float tw = (W - 40 - 30) / 4f;
            using (var f = new Font("Segoe UI Semibold", 10f))
                WindowPainter.DrawText(g, "Icono de la bandeja", f, text, new RectangleF(20 * k, 58 * k, 400 * k, 22 * k), StringAlignment.Near, StringAlignment.Near);
            for (int i = 0; i < 4; i++)
            {
                iconTiles[i] = new RectangleF((20 + i * (tw + 10)) * k, 86 * k, tw * k, 108 * k);
                IconTile(g, i, iconTiles[i], text, k);
            }

            using (var f = new Font("Segoe UI Semibold", 10f))
                WindowPainter.DrawText(g, "Ventana de detalles", f, text, new RectangleF(20 * k, 208 * k, 400 * k, 22 * k), StringAlignment.Near, StringAlignment.Near);
            for (int i = 0; i < 4; i++)
            {
                winTiles[i] = new RectangleF((20 + i * (tw + 10)) * k, 236 * k, tw * k, 236 * k);
                WinTile(g, i, winTiles[i], text, k);
            }

            using (var f = new Font("Segoe UI", 8.5f))
                WindowPainter.DrawText(g, "Los cambios se aplican al instante y se recuerdan la próxima vez que abras MonitBat.", f, sub,
                    new RectangleF(20 * k, 484 * k, (W - 40) * k, 18 * k), StringAlignment.Near, StringAlignment.Near);
        }

        void Tile(Graphics g, RectangleF r, bool selected, bool hover, float k)
        {
            Color cardBg = light ? Color.White : Color.FromArgb(44, 44, 44);
            Color line = light ? Color.FromArgb(226, 226, 226) : Color.FromArgb(60, 60, 60);
            Color accent = Palette.Accent(light);
            using (var b = new SolidBrush(cardBg))
            using (GraphicsPath p = Gfx.Round(r, 8 * k))
            {
                g.FillPath(b, p);
                Color edge = selected ? accent : hover ? (light ? Color.FromArgb(160, 160, 160) : Color.FromArgb(120, 120, 120)) : line;
                using (var pen = new Pen(edge, selected ? 2 * k : 1)) g.DrawPath(pen, p);
            }
        }

        // Marca ✓ de la opción elegida; se dibuja al final para quedar encima de la vista previa.
        void Badge(Graphics g, RectangleF r, float k)
        {
            Color accent = Palette.Accent(light);
            var badge = new RectangleF(r.Right - 24 * k, r.Y + 8 * k, 16 * k, 16 * k);
            using (var b = new SolidBrush(accent)) g.FillEllipse(b, badge);
            using (var pen = new Pen(light ? Color.White : Color.FromArgb(20, 20, 20), 1.6f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[]
                {
                    new PointF(badge.X + 4.5f * k, badge.Y + 8.2f * k),
                    new PointF(badge.X + 7f * k, badge.Y + 10.6f * k),
                    new PointF(badge.X + 11.5f * k, badge.Y + 5.6f * k)
                });
        }

        void IconTile(Graphics g, int i, RectangleF r, Color text, float k)
        {
            IconStyle st = IconStyles[i];
            Tile(g, r, settings.Icon == st, hoverIcon == i, k);
            var strip = new RectangleF(r.X + 10 * k, r.Y + 10 * k, r.Width - 20 * k, 58 * k);
            using (var b = new SolidBrush(light ? Color.FromArgb(232, 232, 232) : Color.FromArgb(28, 28, 28)))
            using (GraphicsPath p = Gfx.Round(strip, 5 * k))
                g.FillPath(b, p);

            int s = (int)Math.Round(32 * k);
            var m = new IconInput { Percent = 82, Live = true, Glyph = 'm' };
            var h = new IconInput { Percent = 64, Live = true, Glyph = 'h' };
            var icons = new List<Bitmap>();
            if (IconRenderer.PerDevice(st)) { icons.Add(IconRenderer.Number(m, light, s)); icons.Add(IconRenderer.Number(h, light, s)); }
            else icons.Add(IconRenderer.Combined(st, m, h, light, s));
            float gap = 10 * k, total = icons.Count * s + (icons.Count - 1) * gap;
            float x = strip.X + (strip.Width - total) / 2, y = strip.Y + (strip.Height - s) / 2;
            InterpolationMode old = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            foreach (Bitmap b in icons)
            {
                g.DrawImage(b, (int)Math.Round(x), (int)Math.Round(y), s, s);
                x += s + gap;
                b.Dispose();
            }
            g.InterpolationMode = old;

            using (var f = new Font("Segoe UI Semibold", 9f))
                WindowPainter.DrawText(g, IconNames[i], f, text, new RectangleF(r.X + 6 * k, r.Y + 78 * k, r.Width - 12 * k, 20 * k), StringAlignment.Center, StringAlignment.Near);
            if (settings.Icon == st) Badge(g, r, k);
        }

        void WinTile(Graphics g, int i, RectangleF r, Color text, float k)
        {
            Tile(g, r, settings.Window == WinStyles[i], hoverWin == i, k);
            var area = new RectangleF(r.X + 10 * k, r.Y + 10 * k, r.Width - 20 * k, 186 * k);
            Color w1 = light ? Color.FromArgb(185, 210, 246) : Color.FromArgb(22, 38, 74);
            Color w2 = light ? Color.FromArgb(242, 212, 230) : Color.FromArgb(67, 32, 106);
            using (var b = new LinearGradientBrush(area, w1, w2, 45f))
            using (GraphicsPath p = Gfx.Round(area, 6 * k))
                g.FillPath(b, p);

            Bitmap t = Thumb(i);
            float sc = Math.Min((area.Width - 16 * k) / t.Width, (area.Height - 16 * k) / t.Height);
            float tw = t.Width * sc, th = t.Height * sc;
            g.DrawImage(t, area.X + (area.Width - tw) / 2, area.Y + (area.Height - th) / 2, tw, th);

            using (var f = new Font("Segoe UI Semibold", 9f))
                WindowPainter.DrawText(g, WinNames[i], f, text, new RectangleF(r.X + 6 * k, r.Y + 206 * k, r.Width - 12 * k, 20 * k), StringAlignment.Center, StringAlignment.Near);
            if (settings.Window == WinStyles[i]) Badge(g, r, k);
        }

        // ---- interacción ----

        static int Hit(RectangleF[] tiles, Point p)
        {
            for (int i = 0; i < tiles.Length; i++) if (tiles[i].Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int hi = Hit(iconTiles, e.Location), hw = Hit(winTiles, e.Location);
            bool hc = closeRect.Contains(e.Location);
            if (hi != hoverIcon || hw != hoverWin || hc != hoverClose)
            {
                hoverIcon = hi; hoverWin = hw; hoverClose = hc;
                Cursor = hi >= 0 || hw >= 0 || hc ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverIcon = hoverWin = -1;
            hoverClose = false;
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (closeRect.Contains(e.Location)) { Hide(); return; }
                int i = Hit(iconTiles, e.Location), w = Hit(winTiles, e.Location);
                if (i >= 0 && settings.Icon != IconStyles[i]) { settings.Icon = IconStyles[i]; Commit(); }
                if (w >= 0 && settings.Window != WinStyles[w]) { settings.Window = WinStyles[w]; Commit(); }
            }
            base.OnMouseClick(e);
        }

        void Commit()
        {
            settings.Save();
            Invalidate();
            Action h = Changed;
            if (h != null) h();
        }
    }
}
