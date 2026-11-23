using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MonitBat
{
    // Datos que dibuja cada tarjeta de la ventana de estado.
    class CardData
    {
        public string Kind = "", Name = "", Detail = "Leyendo...";
        public int Percent = -1;   // -1 = sin dato
        public bool Live;          // true si el porcentaje es una lectura actual (no el último conocido)
        public bool Charging, Low;
        public char Glyph;         // 'm' ratón, 'h' auriculares
        public List<HistoryPoint> Points;  // últimas 24 h (ventana Historial)
        public string Eta = "";            // tiempo restante estimado (ventana Historial)
    }

    // Colores compartidos por los iconos de la bandeja, la ventana y la configuración.
    static class Palette
    {
        public static Color Device(char glyph, bool light)
        {
            if (glyph == 'm') return light ? Color.FromArgb(0, 103, 192) : Color.FromArgb(96, 205, 255);
            return light ? Color.FromArgb(122, 63, 208) : Color.FromArgb(199, 155, 255);
        }
        public static Color Accent(bool light) { return Device('m', light); }
        public static Color Low(bool light) { return light ? Color.FromArgb(214, 43, 43) : Color.FromArgb(255, 90, 90); }
        public static Color Charge(bool light) { return light ? Color.FromArgb(25, 153, 74) : Color.FromArgb(76, 217, 100); }
    }

    // Dibuja la ventana de estado en cualquiera de sus estilos. La usan StatusForm y las miniaturas
    // de la configuración. Las medidas están en píxeles a 96 ppp y se multiplican por k.
    class WindowPainter
    {
        public const int W = 320;
        public WindowStyle Style = WindowStyle.Cristal;
        public bool Light;
        public bool RoundCorners;   // fondo transparente fuera de las esquinas (miniaturas)
        public CardData Mouse = new CardData { Kind = "Ratón", Name = ScyroxV8.Name, Glyph = 'm' };
        public CardData Headset = new CardData { Kind = "Auriculares", Name = CorsairVoid.Name, Glyph = 'h' };
        public string Time = "";
        public bool GearHover, CloseHover;
        public RectangleF GearRect, CloseRect;   // en píxeles, tras pintar

        Color bg, border, text, sub, cardBg, cardLine, track;
        float k = 1f;

        public static int HeightOf(WindowStyle s)
        {
            switch (s)
            {
                case WindowStyle.Anillos: return 266;
                case WindowStyle.Segmentos: return 274;
                case WindowStyle.Historial: return 322;
                default: return 262;
            }
        }

        public static Bitmap Render(WindowStyle style, bool light, CardData m, CardData h, string time)
        {
            var p = new WindowPainter { Style = style, Light = light, Mouse = m, Headset = h, Time = time, RoundCorners = true };
            int ht = HeightOf(style);
            var bmp = new Bitmap(W, ht);
            using (Graphics g = Graphics.FromImage(bmp)) p.Paint(g, W, ht, 1f);
            return bmp;
        }

        public void Paint(Graphics g, int width, int height, float scale)
        {
            k = scale;
            bg = Light ? Color.FromArgb(243, 243, 243) : Color.FromArgb(32, 32, 32);
            border = Light ? Color.FromArgb(214, 214, 214) : Color.FromArgb(64, 64, 64);
            text = Light ? Color.FromArgb(26, 26, 26) : Color.White;
            sub = Light ? Color.FromArgb(104, 104, 104) : Color.FromArgb(166, 166, 166);
            cardBg = Light ? Color.White : Color.FromArgb(44, 44, 44);
            cardLine = Light ? Color.FromArgb(230, 230, 230) : Color.FromArgb(58, 58, 58);
            track = Light ? Color.FromArgb(228, 228, 228) : Color.FromArgb(70, 70, 70);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (GraphicsPath p = Gfx.Round(new RectangleF(0.5f, 0.5f, width - 1, height - 1), 8 * k))
            {
                if (RoundCorners)
                {
                    g.Clear(Color.Transparent);
                    using (var b = new SolidBrush(bg)) g.FillPath(b, p);
                }
                else g.Clear(bg);
                using (var pen = new Pen(border)) g.DrawPath(pen, p);
            }

            using (var title = new Font("Segoe UI Semibold", 11.5f))
                DrawText(g, "Baterías", title, text, new RectangleF(18 * k, 12 * k, 200 * k, 26 * k), StringAlignment.Near, StringAlignment.Near);

            CloseRect = new RectangleF((W - 38) * k, 10 * k, 26 * k, 26 * k);
            GearRect = new RectangleF((W - 68) * k, 10 * k, 26 * k, 26 * k);
            HoverBg(g, CloseRect, CloseHover);
            HoverBg(g, GearRect, GearHover);
            DrawClose(g, CloseRect);
            DrawGear(g, GearRect);

            switch (Style)
            {
                case WindowStyle.Anillos:
                {
                    float cw = (W - 24 - 10) / 2f;
                    CardRings(g, new RectangleF(12 * k, 48 * k, cw * k, 178 * k), Mouse);
                    CardRings(g, new RectangleF((12 + cw + 10) * k, 48 * k, cw * k, 178 * k), Headset);
                    break;
                }
                case WindowStyle.Segmentos:
                    CardSegments(g, new RectangleF(12 * k, 48 * k, (W - 24) * k, 92 * k), Mouse);
                    CardSegments(g, new RectangleF(12 * k, 148 * k, (W - 24) * k, 92 * k), Headset);
                    break;
                case WindowStyle.Historial:
                    CardHistory(g, new RectangleF(12 * k, 48 * k, (W - 24) * k, 114 * k), Mouse);
                    CardHistory(g, new RectangleF(12 * k, 170 * k, (W - 24) * k, 114 * k), Headset);
                    break;
                default:
                    CardCristal(g, new RectangleF(12 * k, 48 * k, (W - 24) * k, 84 * k), Mouse);
                    CardCristal(g, new RectangleF(12 * k, 140 * k, (W - 24) * k, 84 * k), Headset);
                    break;
            }

            using (var small = new Font("Segoe UI", 8.5f))
                DrawText(g, Time, small, sub, new RectangleF(18 * k, (HeightOf(Style) - 30) * k, (W - 36) * k, 20 * k), StringAlignment.Near, StringAlignment.Near);
        }

        // ---- piezas comunes ----

        Color StateColor(CardData c)
        {
            if (c.Percent < 0 || !c.Live) return sub;
            if (c.Low) return Palette.Low(Light);
            if (c.Charging) return Palette.Charge(Light);
            return Palette.Device(c.Glyph, Light);
        }

        Color NumberColor(CardData c)
        {
            if (c.Percent < 0 || !c.Live) return sub;
            return c.Low ? Palette.Low(Light) : text;
        }

        void CardBox(Graphics g, RectangleF r, float rad)
        {
            using (var b = new SolidBrush(cardBg))
            using (var pen = new Pen(cardLine))
            using (GraphicsPath p = Gfx.Round(r, rad))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
        }

        void HoverBg(Graphics g, RectangleF r, bool on)
        {
            if (!on) return;
            using (var b = new SolidBrush(Light ? Color.FromArgb(30, 0, 0, 0) : Color.FromArgb(40, 255, 255, 255)))
            using (GraphicsPath p = Gfx.Round(r, 5 * k))
                g.FillPath(b, p);
        }

        void DrawClose(Graphics g, RectangleF r)
        {
            using (var pen = new Pen(sub, 1.4f * k))
            {
                float i = 9 * k;
                g.DrawLine(pen, r.Left + i, r.Top + i, r.Right - i, r.Bottom - i);
                g.DrawLine(pen, r.Right - i, r.Top + i, r.Left + i, r.Bottom - i);
            }
        }

        void DrawGear(Graphics g, RectangleF r)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            using (var pen = new Pen(sub, 1.4f * k))
            using (var tooth = new Pen(sub, 2.4f * k))
            {
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
                    g.DrawLine(tooth, cx + c * 5.2f * k, cy + s * 5.2f * k, cx + c * 7.4f * k, cy + s * 7.4f * k);
                }
                float ro = 4.9f * k, ri = 1.9f * k;
                g.DrawEllipse(pen, cx - ro, cy - ro, 2 * ro, 2 * ro);
                g.DrawEllipse(pen, cx - ri, cy - ri, 2 * ri, 2 * ri);
            }
        }

        // ---- Cristal: glifo, nombre, porcentaje, barra y estado ----
        void CardCristal(Graphics g, RectangleF r, CardData c)
        {
            Color col = StateColor(c);
            CardBox(g, r, 7 * k);
            float x = r.X + 14 * k, w = r.Width - 28 * k;

            var circle = new RectangleF(x, r.Y + 10 * k, 34 * k, 34 * k);
            using (var b = new SolidBrush(Color.FromArgb(Light ? 34 : 46, col))) g.FillEllipse(b, circle);
            DrawGlyph(g, c.Glyph, circle, col);

            float tx = x + 46 * k, tw = w - 46 * k - 90 * k;
            using (var f = new Font("Segoe UI", 8.5f))
                DrawText(g, c.Kind, f, sub, new RectangleF(tx, r.Y + 9 * k, tw, 16 * k), StringAlignment.Near, StringAlignment.Near);
            using (var f = new Font("Segoe UI Semibold", 10.5f))
                DrawText(g, c.Name, f, text, new RectangleF(tx, r.Y + 24 * k, tw, 22 * k), StringAlignment.Near, StringAlignment.Near);
            using (var f = new Font("Segoe UI Semibold", 20f))
                DrawText(g, c.Percent >= 0 ? c.Percent + "%" : "--", f, NumberColor(c),
                         new RectangleF(x + w - 100 * k, r.Y + 8 * k, 100 * k, 40 * k), StringAlignment.Far, StringAlignment.Near);

            Bar(g, new RectangleF(x, r.Y + 54 * k, w, 8 * k), c.Percent, col);

            using (var f = new Font("Segoe UI", 8.5f))
                DrawText(g, c.Detail, f, c.Low ? Palette.Low(Light) : sub, new RectangleF(x, r.Y + 64 * k, w, 16 * k), StringAlignment.Near, StringAlignment.Near);
        }

        void Bar(Graphics g, RectangleF t, int pct, Color col)
        {
            using (var b = new SolidBrush(track))
            using (GraphicsPath p = Gfx.Round(t, t.Height / 2))
                g.FillPath(b, p);
            if (pct <= 0) return;
            float fw = Math.Max(t.Height, t.Width * Math.Min(pct, 100) / 100f);
            using (var b = new SolidBrush(col))
            using (GraphicsPath p = Gfx.Round(new RectangleF(t.X, t.Y, fw, t.Height), t.Height / 2))
                g.FillPath(b, p);
        }

        // ---- Anillos: anillo grande con el porcentaje al centro ----
        void CardRings(Graphics g, RectangleF r, CardData c)
        {
            Color col = StateColor(c);
            CardBox(g, r, 7 * k);
            float d = 96 * k, rx = r.X + (r.Width - d) / 2, ry = r.Y + 14 * k, pw = 9 * k;
            var ring = new RectangleF(rx + pw / 2, ry + pw / 2, d - pw, d - pw);
            using (var pen = new Pen(track, pw)) g.DrawEllipse(pen, ring);
            if (c.Percent > 0)
                using (var pen = new Pen(col, pw) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, ring, -90, 360f * Math.Min(c.Percent, 100) / 100f);
            using (var f = new Font("Segoe UI Semibold", 17f))
                DrawText(g, c.Percent >= 0 ? c.Percent + "%" : "--", f, NumberColor(c), new RectangleF(rx, ry, d, d), StringAlignment.Center, StringAlignment.Center);

            using (var f = new Font("Segoe UI", 8f))
                DrawText(g, c.Kind, f, sub, new RectangleF(r.X + 8 * k, r.Y + 116 * k, r.Width - 16 * k, 16 * k), StringAlignment.Center, StringAlignment.Near);
            using (var f = new Font("Segoe UI Semibold", 10f))
                DrawText(g, c.Name, f, text, new RectangleF(r.X + 8 * k, r.Y + 131 * k, r.Width - 16 * k, 20 * k), StringAlignment.Center, StringAlignment.Near);

            using (var f = new Font("Segoe UI", 8f))
            {
                string s = c.Detail ?? "";
                SizeF sz = g.MeasureString(s, f);
                float cw = Math.Min(sz.Width + 16 * k, r.Width - 16 * k), ch = 20 * k;
                var chip = new RectangleF(r.X + (r.Width - cw) / 2, r.Y + 152 * k, cw, ch);
                using (var b = new SolidBrush(Color.FromArgb(Light ? 30 : 45, col)))
                using (GraphicsPath p = Gfx.Round(chip, ch / 2))
                    g.FillPath(b, p);
                DrawText(g, s, f, col, RectangleF.Inflate(chip, -6 * k, 0), StringAlignment.Center, StringAlignment.Center);
            }
        }

        // ---- Segmentos: número grande y barra de 12 LED ----
        void CardSegments(Graphics g, RectangleF r, CardData c)
        {
            Color col = StateColor(c);
            CardBox(g, r, 6 * k);
            float x = r.X + 14 * k, w = r.Width - 28 * k;
            using (var f = new Font("Segoe UI Semibold", 7.5f))
            {
                DrawText(g, (c.Kind + " · " + c.Name).ToUpperInvariant(), f, sub, new RectangleF(x, r.Y + 10 * k, w * 0.58f, 14 * k), StringAlignment.Near, StringAlignment.Near);
                DrawText(g, c.Detail, f, c.Low ? Palette.Low(Light) : sub, new RectangleF(x + w * 0.58f, r.Y + 10 * k, w * 0.42f, 14 * k), StringAlignment.Far, StringAlignment.Near);
            }

            string num = c.Percent >= 0 ? c.Percent.ToString() : "--";
            using (var big = new Font("Segoe UI Semibold", 26f))
            using (var small = new Font("Segoe UI", 12f))
            {
                StringFormat sf = StringFormat.GenericTypographic;
                float y = r.Y + 24 * k;
                SizeF sz = g.MeasureString(num, big, PointF.Empty, sf);
                using (var b = new SolidBrush(c.Percent >= 0 && c.Live ? col : sub)) g.DrawString(num, big, b, x, y, sf);
                if (c.Percent >= 0)
                    using (var b = new SolidBrush(sub))
                        g.DrawString("%", small, b, x + sz.Width + 3 * k, y + Ascent(g, big) - Ascent(g, small), sf);
            }

            const int n = 12;
            float gap = 3 * k, sw = (w - (n - 1) * gap) / n, sy = r.Y + 68 * k, sh = 12 * k;
            int lit = c.Percent > 0 ? (int)Math.Round(Math.Min(c.Percent, 100) / 100.0 * n) : 0;
            if (c.Percent > 0 && lit == 0) lit = 1;
            for (int i = 0; i < n; i++)
            {
                var seg = new RectangleF(x + i * (sw + gap), sy, sw, sh);
                if (i < lit && !Light && c.Live)
                    using (var b = new SolidBrush(Color.FromArgb(55, col)))
                    using (GraphicsPath p = Gfx.Round(RectangleF.Inflate(seg, 2 * k, 2 * k), 4 * k))
                        g.FillPath(b, p);
                using (var b = new SolidBrush(i < lit ? col : track))
                using (GraphicsPath p = Gfx.Round(seg, 2 * k))
                    g.FillPath(b, p);
            }
        }

        static float Ascent(Graphics g, Font f)
        {
            return f.GetHeight(g) * f.FontFamily.GetCellAscent(f.Style) / f.FontFamily.GetLineSpacing(f.Style);
        }

        // ---- Historial: curva de 24 h y tiempo restante ----
        void CardHistory(Graphics g, RectangleF r, CardData c)
        {
            Color col = StateColor(c);
            CardBox(g, r, 7 * k);
            float x = r.X + 14 * k, w = r.Width - 28 * k;
            using (var f = new Font("Segoe UI", 8f))
                DrawText(g, c.Kind, f, sub, new RectangleF(x, r.Y + 9 * k, w - 90 * k, 15 * k), StringAlignment.Near, StringAlignment.Near);
            using (var f = new Font("Segoe UI Semibold", 10.5f))
                DrawText(g, c.Name, f, text, new RectangleF(x, r.Y + 22 * k, w - 90 * k, 22 * k), StringAlignment.Near, StringAlignment.Near);
            using (var f = new Font("Segoe UI Semibold", 18f))
                DrawText(g, c.Percent >= 0 ? c.Percent + "%" : "--", f, c.Percent >= 0 && c.Live ? col : sub,
                         new RectangleF(x + w - 100 * k, r.Y + 8 * k, 100 * k, 36 * k), StringAlignment.Far, StringAlignment.Near);

            DrawSpark(g, new RectangleF(x, r.Y + 50 * k, w, 40 * k), c.Points, col);

            using (var f = new Font("Segoe UI", 8f))
            {
                DrawText(g, "últimas 24 h", f, sub, new RectangleF(x, r.Y + 94 * k, w / 2, 15 * k), StringAlignment.Near, StringAlignment.Near);
                DrawText(g, c.Eta, f, c.Low ? Palette.Low(Light) : sub, new RectangleF(x + w * 0.3f, r.Y + 94 * k, w * 0.7f, 15 * k), StringAlignment.Far, StringAlignment.Near);
            }
        }

        void DrawSpark(Graphics g, RectangleF a, List<HistoryPoint> pts, Color col)
        {
            using (var pen = new Pen(Color.FromArgb(60, sub), 1f) { DashPattern = new float[] { 3, 4 } })
                g.DrawLine(pen, a.X, a.Y + a.Height / 2, a.Right, a.Y + a.Height / 2);

            DateTime now = DateTime.Now, from = now - History.Span;
            var line = new List<PointF>();
            if (pts != null)
                foreach (HistoryPoint p in pts)
                {
                    if (p.Time < from) continue;
                    float px = a.X + a.Width * (float)((p.Time - from).TotalSeconds / History.Span.TotalSeconds);
                    float py = a.Bottom - 2 * k - (a.Height - 4 * k) * Math.Min(p.Percent, 100) / 100f;
                    line.Add(new PointF(px, py));
                }
            if (line.Count > 0) line.Add(new PointF(a.Right, line[line.Count - 1].Y)); // el último dato sigue vigente
            if (line.Count < 3)
            {
                using (var f = new Font("Segoe UI", 8f))
                    DrawText(g, "Juntando lecturas para dibujar la curva...", f, sub, a, StringAlignment.Center, StringAlignment.Center);
                return;
            }
            var area = new List<PointF>(line);
            area.Add(new PointF(a.Right, a.Bottom));
            area.Add(new PointF(line[0].X, a.Bottom));
            using (var b = new SolidBrush(Color.FromArgb(Light ? 36 : 44, col))) g.FillPolygon(b, area.ToArray());
            using (var pen = new Pen(col, 2 * k) { LineJoin = LineJoin.Round }) g.DrawLines(pen, line.ToArray());
        }

        // Glifo de 24 unidades (ratón o auriculares) dibujado a 20 px dentro del círculo.
        void DrawGlyph(Graphics g, char glyph, RectangleF box, Color color)
        {
            float s = 20 * k / 24f;
            float ox = box.X + (box.Width - 20 * k) / 2, oy = box.Y + (box.Height - 20 * k) / 2;
            Func<float, float> X = v => ox + v * s, Y = v => oy + v * s;
            using (var pen = new Pen(color, 1.9f * s) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                if (glyph == 'm')
                {
                    using (GraphicsPath p = Gfx.Round(new RectangleF(X(6.5f), Y(2.5f), 11 * s, 19 * s), 5.5f * s)) g.DrawPath(pen, p);
                    g.DrawLine(pen, X(12), Y(2.5f), X(12), Y(9));
                }
                else
                {
                    g.DrawArc(pen, X(4.5f), Y(4.5f), 15 * s, 15 * s, 180, 180);
                    g.DrawLine(pen, X(4.5f), Y(12), X(4.5f), Y(14));
                    g.DrawLine(pen, X(19.5f), Y(12), X(19.5f), Y(14));
                    using (GraphicsPath p = Gfx.Round(new RectangleF(X(3), Y(14), 4 * s, 7 * s), 1.6f * s)) g.DrawPath(pen, p);
                    using (GraphicsPath p = Gfx.Round(new RectangleF(X(17), Y(14), 4 * s, 7 * s), 1.6f * s)) g.DrawPath(pen, p);
                }
            }
        }

        public static void DrawText(Graphics g, string s, Font f, Color color, RectangleF r, StringAlignment h, StringAlignment v)
        {
            using (var sf = new StringFormat(StringFormatFlags.NoWrap) { Alignment = h, LineAlignment = v, Trimming = StringTrimming.EllipsisCharacter })
            using (var b = new SolidBrush(color))
                g.DrawString(s ?? "", f, b, r, sf);
        }
    }
}
