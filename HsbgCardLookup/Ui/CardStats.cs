using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HsbgCardLookup.Ui
{
    /// <summary>
    /// A card render with other attack and health numbers on it, and nothing else changed.
    ///
    /// The renders have their numbers painted in, and the gems under them sit at slightly different
    /// pixels from card to card (measured: two layouts among normal cards, a third for golden), so no
    /// shared "clean gem" picture fits. Instead each render's own digits are found (the white glyphs in
    /// the two gem corners), removed by letting the surrounding pixels flow into them, and the new number
    /// is set at the removed digits' centre and size. Every pixel outside the old digits is the render's.
    /// </summary>
    internal static class CardStats
    {
        // The gems sit in the bottom corners. Renders arrive trimmed to the card (sizes differ per card),
        // so the corners are measured from the picture's own edges: this wide, this tall.
        private const int ZoneW = 200, ZoneH = 200;
        // Belwe's digits are 0.62 em tall; a longer number is set smaller, as the game does.
        private const double DigitEm = 0.62;
        public static Action<string> Log;
        private static readonly double[] Shrink = { 1.0, 1.0, 0.88, 0.72, 0.6, 0.5 };
        // The owner wants the new numbers clearly larger than the render's own.
        private const double Larger = 1.3;

        /// <summary>The render with these numbers, frozen; the render itself when its digits can't be found.</summary>
        public static BitmapSource WithNumbers(BitmapSource render, string attack, string health, int oldAttackDigits, int oldHealthDigits,
            Brush attackFill, Brush healthFill)
        {
            try
            {
                var src = new FormatConvertedBitmap(render, PixelFormats.Bgra32, null, 0);
                int w = src.PixelWidth, h = src.PixelHeight;
                if (w < 2 * ZoneW || h < 2 * ZoneH) { Log?.Invoke("[boards] card render is " + w + "x" + h + ", numbers left as they are"); return render; }
                var px = new byte[w * h * 4];
                src.CopyPixels(px, w * 4, 0);
                Rect a = RemoveDigits(px, w, new[] { 0, h - ZoneH, ZoneW, h }), b = RemoveDigits(px, w, new[] { w - ZoneW, h - ZoneH, w, h });
                if (a.IsEmpty || b.IsEmpty) Log?.Invoke("[boards] digits not found on a render (attack " + !a.IsEmpty + ", health " + !b.IsEmpty + ")");
                var clean = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);

                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawImage(clean, new Rect(0, 0, w, h));
                    if (!a.IsEmpty) Number(dc, attack, a, oldAttackDigits, attackFill, w);
                    if (!b.IsEmpty) Number(dc, health, b, oldHealthDigits, healthFill, w);
                }
                var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                target.Render(visual);
                target.Freeze();
                return target;
            }
            catch (Exception ex) { Log?.Invoke("[boards] card numbers: " + ex.GetType().Name + ": " + ex.Message); return render; }
        }

        // The new number where the old one was: same centre, and the old digits' height scaled by how
        // many digits each has.
        private static void Number(DrawingContext dc, string text, Rect old, int oldDigits, Brush fill, int width)
        {
            double perDigit = old.Height / DigitEm / Shrink[Math.Min(Math.Max(oldDigits, 1), Shrink.Length - 1)];
            double size = perDigit * Larger * Shrink[Math.Min(text.Length, Shrink.Length - 1)];
            var shape = Assets.GameArt.NumberOutline(text, size).Clone();
            var box = shape.Bounds;
            // Centred on the old digits, but kept whole inside the picture: a wide number moves inward.
            double edge = size * 0.08 + 1;
            double x = old.X + old.Width / 2 - box.Width / 2;
            x = Math.Max(edge, Math.Min(x, width - box.Width - edge));
            shape.Transform = new TranslateTransform(x - box.X, old.Y + old.Height / 2 - (box.Y + box.Height / 2));
            dc.DrawGeometry(null, new Pen(Brushes.Black, size * 0.13) { LineJoin = PenLineJoin.Round }, shape);
            dc.DrawGeometry(fill, null, shape);
        }

        /// <summary>
        /// Takes the digits out of one gem corner, in place. Returns the box of their white fill, or
        /// Rect.Empty when none were found (the corner is then untouched).
        /// </summary>
        private static Rect RemoveDigits(byte[] px, int stride, int[] zone)
        {
            int x0 = zone[0], y0 = zone[1], zw = zone[2] - zone[0], zh = zone[3] - zone[1];
            var white = new bool[zw * zh];
            for (int y = 0; y < zh; y++)
                for (int x = 0; x < zw; x++)
                {
                    int o = ((y0 + y) * stride + x0 + x) * 4;
                    white[y * zw + x] = px[o] > 222 && px[o + 1] > 222 && px[o + 2] > 222 && px[o + 3] > 200;
                }

            // Digits are the white shapes of a digit's size (the tribe plate's letters are far smaller).
            var digit = new bool[zw * zh];
            var seen = new bool[zw * zh];
            var stack = new Stack<int>();
            var member = new List<int>();
            int bx0 = int.MaxValue, by0 = int.MaxValue, bx1 = -1, by1 = -1;
            for (int start = 0; start < white.Length; start++)
            {
                if (!white[start] || seen[start]) continue;
                member.Clear(); stack.Push(start); seen[start] = true;
                int cx0 = zw, cy0 = zh, cx1 = -1, cy1 = -1;
                while (stack.Count > 0)
                {
                    int p = stack.Pop(); member.Add(p);
                    int x = p % zw, y = p / zw;
                    if (x < cx0) cx0 = x; if (x > cx1) cx1 = x; if (y < cy0) cy0 = y; if (y > cy1) cy1 = y;
                    if (x > 0 && white[p - 1] && !seen[p - 1]) { seen[p - 1] = true; stack.Push(p - 1); }
                    if (x < zw - 1 && white[p + 1] && !seen[p + 1]) { seen[p + 1] = true; stack.Push(p + 1); }
                    if (y > 0 && white[p - zw] && !seen[p - zw]) { seen[p - zw] = true; stack.Push(p - zw); }
                    if (y < zh - 1 && white[p + zw] && !seen[p + zw]) { seen[p + zw] = true; stack.Push(p + zw); }
                }
                int ch = cy1 - cy0 + 1, cw = cx1 - cx0 + 1;
                if (ch < 40 || ch > 90 || cw < 14 || cw > 90) continue;
                foreach (int p in member) digit[p] = true;
                if (cx0 < bx0) bx0 = cx0; if (cy0 < by0) by0 = cy0; if (cx1 > bx1) bx1 = cx1; if (cy1 > by1) by1 = cy1;
            }
            if (bx1 < 0) return Rect.Empty;

            // What to repaint: the fill, its black outline (dark pixels within 8 of the fill) and a 3-pixel rim.
            var near = Grow(digit, zw, zh, 8);
            var rim = Grow(digit, zw, zh, 3);
            var mask = new bool[zw * zh];
            var todo = new List<int>();
            for (int i = 0; i < mask.Length; i++)
            {
                int o = ((y0 + i / zw) * stride + x0 + i % zw) * 4;
                bool dark = px[o] < 75 && px[o + 1] < 75 && px[o + 2] < 75;
                int x = i % zw, y = i / zw;
                if ((rim[i] || (near[i] && dark)) && x > 0 && y > 0 && x < zw - 1 && y < zh - 1) { mask[i] = true; todo.Add(i); }
            }

            // Let the neighbours flow in (colour premultiplied by alpha, so a digit hanging over the
            // card's edge fades out instead of leaving a smear).
            var v = new float[zw * zh * 4];
            for (int i = 0; i < zw * zh; i++)
            {
                int o = ((y0 + i / zw) * stride + x0 + i % zw) * 4;
                float al = mask[i] ? 0 : px[o + 3] / 255f;
                v[i * 4] = mask[i] ? 0 : px[o] * al; v[i * 4 + 1] = mask[i] ? 0 : px[o + 1] * al; v[i * 4 + 2] = mask[i] ? 0 : px[o + 2] * al; v[i * 4 + 3] = al;
            }
            // A neighbour that is dark and was not repainted is the gem's rim or a shadow: it must not
            // bleed into the gem, so only repainted and coloured neighbours count.
            var source = new bool[zw * zh];
            for (int i = 0; i < source.Length; i++)
            {
                int o = ((y0 + i / zw) * stride + x0 + i % zw) * 4;
                source[i] = mask[i] || px[o + 3] < 40 || px[o] >= 75 || px[o + 1] >= 75 || px[o + 2] >= 75;
            }
            var around = new int[4];
            for (int pass = 0; pass < 160; pass++)
                foreach (int i in todo)
                {
                    around[0] = i - 1; around[1] = i + 1; around[2] = i - zw; around[3] = i + zw;
                    int n = 0;
                    float b0 = 0, b1 = 0, b2 = 0, b3 = 0;
                    foreach (int k in around)
                    {
                        if (!source[k]) continue;
                        b0 += v[k * 4]; b1 += v[k * 4 + 1]; b2 += v[k * 4 + 2]; b3 += v[k * 4 + 3]; n++;
                    }
                    if (n == 0) continue;
                    v[i * 4] = b0 / n; v[i * 4 + 1] = b1 / n; v[i * 4 + 2] = b2 / n; v[i * 4 + 3] = b3 / n;
                }
            foreach (int i in todo)
            {
                int o = ((y0 + i / zw) * stride + x0 + i % zw) * 4;
                float al = v[i * 4 + 3];
                px[o + 3] = (byte)Math.Round(al * 255);
                for (int c = 0; c < 3; c++) px[o + c] = al < 0.004f ? (byte)0 : (byte)Math.Max(0, Math.Min(255, Math.Round(v[i * 4 + c] / al)));
            }
            return new Rect(x0 + bx0, y0 + by0, bx1 - bx0 + 1, by1 - by0 + 1);
        }

        private static bool[] Grow(bool[] m, int w, int h, int steps)
        {
            var cur = m;
            for (int s = 0; s < steps; s++)
            {
                var next = (bool[])cur.Clone();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (cur[i]) continue;
                        if ((x > 0 && cur[i - 1]) || (x < w - 1 && cur[i + 1]) || (y > 0 && cur[i - w]) || (y < h - 1 && cur[i + w])) next[i] = true;
                    }
                cur = next;
            }
            return cur;
        }
    }
}
