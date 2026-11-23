using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MonitBat
{
    // Estado de un periférico tal como lo dibuja un icono de la bandeja.
    struct IconInput
    {
        public int Percent;     // -1 = sin dato
        public bool Live;       // lectura actual (false = último valor conocido, dispositivo en reposo)
        public bool Charging;
        public char Glyph;      // 'm' ratón, 'h' auriculares
    }

    static class Gfx
    {
        public static GraphicsPath Round(RectangleF r, float rad)
        {
            float d = Math.Max(0.01f, Math.Min(rad * 2, Math.Min(r.Width, r.Height)));
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // Iconos de la bandeja. Número: un icono por periférico. Pilas, Anillos y Glifos: un icono con los dos.
    // Rojo = menos del 20 %, verde = cargando, atenuado = dato viejo o sin dato.
    static class IconRenderer
    {
        public static bool PerDevice(IconStyle st) { return st == IconStyle.Numero; }

        static Color Fg(bool light) { return light ? Color.FromArgb(26, 26, 26) : Color.White; }
        static Color Dim(bool light) { return Color.FromArgb(140, Fg(light)); }
        static Color Track(bool light) { return Color.FromArgb(light ? 55 : 70, Fg(light)); }
        static bool Low(IconInput d) { return d.Percent >= 0 && d.Live && !d.Charging && d.Percent < TrayApp.LowLevel; }

        static Color StateColor(IconInput d, bool light, bool deviceColor)
        {
            if (d.Percent < 0 || !d.Live) return Dim(light);
            if (d.Charging) return Palette.Charge(light);
            if (Low(d)) return Palette.Low(light);
            return deviceColor ? Palette.Device(d.Glyph, light) : Fg(light);
        }

        static Bitmap NewBitmap(int s, out Graphics g)
        {
            var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            return bmp;
        }

        // ---- Número: porcentaje grande y barra con el color del dispositivo ----
        public static Bitmap Number(IconInput d, bool light, int s)
        {
            float k = s / 16f;
            bool known = d.Percent >= 0;
            Color numColor = !known || !d.Live ? Dim(light) : Low(d) ? Palette.Low(light) : Fg(light);
            Graphics g;
            Bitmap bmp = NewBitmap(s, out g);
            using (g)
            {
                // El número se dibuja como trazado y se centra por su caja real (igual con 1, 2 o 3 cifras).
                g.SmoothingMode = SmoothingMode.AntiAlias;
                string txt = known ? d.Percent.ToString() : "--";
                using (var path = new GraphicsPath())
                using (var family = new FontFamily("Segoe UI"))
                {
                    path.AddString(txt, family, (int)FontStyle.Bold, 13f * k, PointF.Empty, StringFormat.GenericTypographic);
                    RectangleF bb = path.GetBounds();
                    float sc = Math.Min(1f, Math.Min(15f * k / bb.Width, known ? 10f * k / bb.Height : 1f));
                    using (var m = new Matrix())
                    {
                        m.Translate(s / 2f, (known ? 5.5f : 6f) * k);
                        m.Scale(sc, sc);
                        m.Translate(-(bb.X + bb.Width / 2), -(bb.Y + bb.Height / 2));
                        path.Transform(m);
                    }
                    using (var b = new SolidBrush(numColor)) g.FillPath(b, path);
                }

                // Barra: filas 13-14, en píxeles enteros para que quede nítida.
                g.SmoothingMode = SmoothingMode.None;
                int bx = (int)Math.Round(1 * k), by = (int)Math.Round(13 * k), bw = s - 2 * bx, bh = Math.Max(2, (int)Math.Round(2 * k));
                using (var b = new SolidBrush(Track(light))) g.FillRectangle(b, bx, by, bw, bh);
                if (known && d.Percent > 0)
                {
                    int fw = Math.Max(bh, (int)Math.Round(bw * Math.Min(d.Percent, 100) / 100.0));
                    using (var b = new SolidBrush(StateColor(d, light, true))) g.FillRectangle(b, bx, by, fw, bh);
                }
            }
            return bmp;
        }

        // ---- Pilas, Anillos o Glifos: los dos periféricos en un icono ----
        public static Bitmap Combined(IconStyle st, IconInput m, IconInput h, bool light, int s)
        {
            Graphics g;
            Bitmap bmp = NewBitmap(s, out g);
            using (g)
            {
                if (st == IconStyle.Pilas)
                {
                    int k = Math.Max(1, s / 16), o = (s - 16 * k) / 2;
                    Cell(g, k, o + 1 * k, o, m, light);
                    Cell(g, k, o + 9 * k, o, h, light);
                }
                else if (st == IconStyle.Anillos)
                {
                    float k = s / 16f;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    Ring(g, s, 6.6f * k, 1.8f * k, m, light);   // exterior: ratón
                    Ring(g, s, 3.3f * k, 1.8f * k, h, light);   // interior: auriculares
                }
                else
                {
                    float k = s / 16f;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    Glyph(g, 'm', m, k, light);
                    Glyph(g, 'h', h, k, light);
                }
            }
            return bmp;
        }

        // Pila de 6x13 px con borne de 2 px; relleno de 4x11 px desde abajo (rejilla de 16x16 escalada en enteros).
        static void Cell(Graphics g, int k, int x, int y, IconInput d, bool light)
        {
            bool known = d.Percent >= 0;
            Color line = !known || !d.Live ? Dim(light) : Low(d) ? Palette.Low(light) : Fg(light);
            Color fill = StateColor(d, light, false);
            Action<Brush, int, int, int, int> px = (b, x0, y0, w, hh) => g.FillRectangle(b, x + x0 * k, y + y0 * k, w * k, hh * k);
            using (var b = new SolidBrush(line))
            {
                px(b, 2, 1, 2, 1);   // borne
                px(b, 1, 2, 4, 1);   // borde superior
                px(b, 1, 14, 4, 1);  // borde inferior
                px(b, 0, 3, 1, 11);  // lateral izquierdo
                px(b, 5, 3, 1, 11);  // lateral derecho
            }
            if (!known) return;
            int h11 = (int)Math.Round(11 * Math.Min(d.Percent, 100) / 100.0);
            if (d.Percent > 0 && h11 == 0) h11 = 1;
            using (var b = new SolidBrush(fill)) px(b, 1, 3 + 11 - h11, 4, h11);
        }

        static void Ring(Graphics g, int s, float r, float w, IconInput d, bool light)
        {
            float c = s / 2f;
            var rect = new RectangleF(c - r, c - r, 2 * r, 2 * r);
            using (var pen = new Pen(Track(light), w)) g.DrawEllipse(pen, rect);
            if (d.Percent > 0)
                using (var pen = new Pen(StateColor(d, light, true), w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, rect, -90, 360f * Math.Min(d.Percent, 100) / 100f);
        }

        // Silueta del ratón (izquierda) o de los auriculares (derecha) en la rejilla de 16x16.
        static void GlyphShape(Graphics g, char glyph, float k, Color c)
        {
            using (var b = new SolidBrush(c))
            using (var pen = new Pen(c, 1.3f * k))
            {
                if (glyph == 'm')
                {
                    using (GraphicsPath p = Gfx.Round(new RectangleF(1.4f * k, 1.5f * k, 5.6f * k, 13f * k), 2.8f * k)) g.FillPath(b, p);
                }
                else
                {
                    float r = 3.1f * k;
                    g.DrawArc(pen, 12f * k - r, 8.8f * k - r, 2 * r, 2 * r, 180, 180);
                    g.DrawLine(pen, 8.9f * k, 8.8f * k, 8.9f * k, 10f * k);
                    g.DrawLine(pen, 15.1f * k, 8.8f * k, 15.1f * k, 10f * k);
                    using (GraphicsPath p = Gfx.Round(new RectangleF(8.1f * k, 9.6f * k, 2.2f * k, 4.8f * k), 1f * k)) g.FillPath(b, p);
                    using (GraphicsPath p = Gfx.Round(new RectangleF(13.7f * k, 9.6f * k, 2.2f * k, 4.8f * k), 1f * k)) g.FillPath(b, p);
                }
            }
        }

        // Silueta tenue y encima la parte llena, de abajo hacia arriba según el porcentaje.
        static void Glyph(Graphics g, char glyph, IconInput d, float k, bool light)
        {
            GlyphShape(g, glyph, k, Color.FromArgb(light ? 70 : 80, Fg(light)));
            if (d.Percent <= 0) return;
            float p = Math.Min(d.Percent, 100) / 100f;
            float y0 = (glyph == 'm' ? 1.5f : 5.7f) * k, y1 = 14.5f * k;
            float top = y1 - (y1 - y0) * p;
            GraphicsState st = g.Save();
            g.SetClip(new RectangleF(0, top, 16 * k, 16 * k - top));
            GlyphShape(g, glyph, k, StateColor(d, light, true));
            g.Restore(st);
        }
    }
}
