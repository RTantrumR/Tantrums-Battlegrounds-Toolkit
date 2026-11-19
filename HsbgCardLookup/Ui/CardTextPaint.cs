using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HsbgCardLookup.Ui
{
    /// <summary>
    /// A card render with another text on its text plate, and nothing else changed.
    ///
    /// The render's own text is found (the pixels on the plate that stand off from the plate's colour),
    /// taken out by letting the plate flow into them, and the new text is set in the game's text face
    /// at the size, colour and place the old one had. The old text's line breaks are kept: the new text
    /// breaks after the same words, so only the numbers move. Minion cards of the 512×672 layout only;
    /// anything else comes back as it was.
    /// </summary>
    internal static class CardTextPaint
    {
        public static Action<string> Log;
        // The text plate's surroundings in a 512×672 render: where text may be, and a patch that is always plate.
        private const int X0 = 100, X1 = 424, Y0 = 428, Y1 = 566, PlateX0 = 200, PlateX1 = 320, PlateY0 = 426, PlateY1 = 438;
        private const double DefaultSize = 25, MinSize = 13, MaxSize = 27, MaxLine = 288, LinePitch = 1.04, MidX = 265, MidY = 494;

        /// <param name="oldText">What the render shows (tags and line breaks as our card data has them).</param>
        /// <param name="newText">What it should show.</param>
        public static BitmapSource WithText(BitmapSource render, string oldText, string newText)
        {
            try
            {
                var face = Assets.GameArt.TextFace;
                if (face == null || render == null || string.IsNullOrEmpty(newText)) return render;
                // Renders arrive trimmed to the card: the plate is found on the whole picture they were cut from.
                var crop = Int32Rect.Empty;
                var whole = render;
                var cut = render as CroppedBitmap;
                if (cut != null) { whole = cut.Source; crop = cut.SourceRect; }
                if (whole.PixelWidth != 512 || whole.PixelHeight != 672) return render;
                var src = new FormatConvertedBitmap(whole, PixelFormats.Bgra32, null, 0);
                int w = 512, h = 672;
                var px = new byte[w * h * 4];
                src.CopyPixels(px, w * 4, 0);

                // The plate's colour, and from it what counts as text: far lighter on a dark plate
                // (golden cards), far darker on a light one.
                var plate = Median(px, w, PlateX0, PlateY0, PlateX1, PlateY1);
                double plateLum = (plate[0] + plate[1] + plate[2]) / 3.0;
                bool lightText = plateLum < 110;
                int zw = X1 - X0, zh = Y1 - Y0;
                var isPlate = new bool[zw * zh];
                var text = new bool[zw * zh];
                for (int y = 0; y < zh; y++)
                {
                    int first = -1, last = -1;
                    for (int x = 0; x < zw; x++)
                    {
                        int o = ((Y0 + y) * w + X0 + x) * 4;
                        bool p = Math.Abs(px[o] - plate[0]) < 40 && Math.Abs(px[o + 1] - plate[1]) < 40 && Math.Abs(px[o + 2] - plate[2]) < 40;
                        isPlate[y * zw + x] = p;
                        if (p) { if (first < 0) first = x; last = x; }
                    }
                    if (last - first < 60) continue;
                    for (int x = first + 4; x < last - 3; x++)
                    {
                        if (isPlate[y * zw + x]) continue;
                        int o = ((Y0 + y) * w + X0 + x) * 4;
                        double lum = (px[o] + px[o + 1] + px[o + 2]) / 3.0;
                        if (lightText ? lum > plateLum + 70 : lum < plateLum - 70) text[y * zw + x] = true;
                    }
                }
                // Letters are small shapes; whatever is larger is the frame, a gem or the type plate.
                var glyphs = Glyphs(text, zw, zh);
                int gx0 = zw, gy0 = zh, gx1 = -1, gy1 = -1, count = 0;
                long r = 0, g = 0, b = 0;
                for (int i = 0; i < glyphs.Length; i++)
                {
                    if (!glyphs[i]) continue;
                    int x = i % zw, y = i / zw;
                    if (x < gx0) gx0 = x; if (x > gx1) gx1 = x; if (y < gy0) gy0 = y; if (y > gy1) gy1 = y;
                    int o = ((Y0 + y) * w + X0 + x) * 4;
                    b += px[o]; g += px[o + 1]; r += px[o + 2]; count++;
                }
                var newLines = Lines(newText);
                var oldLines = Lines(oldText ?? "");
                double size = DefaultSize, midX = MidX, midY = MidY;
                Color ink = lightText ? Color.FromRgb(229, 222, 212) : Color.FromRgb(42, 30, 19);
                if (count > 40)
                {
                    ink = Color.FromRgb((byte)(r / count), (byte)(g / count), (byte)(b / count));
                    midX = X0 + (gx0 + gx1 + 1) / 2.0; midY = Y0 + (gy0 + gy1 + 1) / 2.0;
                    // The old text's width on the render against its width in the face gives its size.
                    double at100 = oldLines.Count == 0 ? 0 : oldLines.Max(l => Width(face, l, 100));
                    if (at100 > 0) size = Clamp(100.0 * (gx1 - gx0 + 1) / at100, MinSize, MaxSize);
                    Erase(px, w, glyphs, zw, zh);
                }
                newLines = Rebreak(newLines, oldLines);
                double widest = newLines.Max(l => Width(face, l, size));
                if (widest > MaxLine) size = Math.Max(MinSize, size * MaxLine / widest);

                var clean = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawImage(clean, new Rect(0, 0, w, h));
                    var brush = new SolidColorBrush(ink); brush.Freeze();
                    double pitch = size * LinePitch, top = midY - newLines.Count * pitch / 2;
                    // Baseline: the face's capitals stand 0.68 em tall; a line's letters are centred in its pitch.
                    for (int i = 0; i < newLines.Count; i++)
                        DrawLine(dc, face, newLines[i], size, midX, top + i * pitch + pitch / 2 + size * 0.30, brush);
                }
                var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                target.Render(visual);
                target.Freeze();
                if (crop.IsEmpty) return target;
                var trimmed = new CroppedBitmap(target, crop);
                trimmed.Freeze();
                return trimmed;
            }
            catch (Exception ex) { Log?.Invoke("[boards] card text: " + ex.GetType().Name + ": " + ex.Message); return render; }
        }

        // ── text runs ────────────────────────────────────────────────────────────────────────────

        private sealed class Run { public string Text; public bool Bold, Italic; }

        /// <summary>Lines of runs, from text with &lt;b&gt;/&lt;i&gt; tags and line breaks.</summary>
        private static List<List<Run>> Lines(string text)
        {
            var lines = new List<List<Run>> { new List<Run>() };
            int bold = 0, italic = 0;
            var cur = new System.Text.StringBuilder();
            Action flush = () =>
            {
                if (cur.Length > 0) lines[lines.Count - 1].Add(new Run { Text = cur.ToString(), Bold = bold > 0, Italic = italic > 0 });
                cur.Clear();
            };
            text = text.Replace("[x]", "").Replace("\r", "");
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '<')
                {
                    int end = text.IndexOf('>', i);
                    string tag = end < 0 ? null : text.Substring(i + 1, end - i - 1).ToLowerInvariant();
                    if (tag == "b" || tag == "/b" || tag == "i" || tag == "/i")
                    {
                        flush();
                        if (tag == "b") bold++; else if (tag == "/b") bold = Math.Max(0, bold - 1);
                        else if (tag == "i") italic++; else italic = Math.Max(0, italic - 1);
                        i = end; continue;
                    }
                }
                if (c == '\n') { flush(); lines.Add(new List<Run>()); continue; }
                cur.Append(c);
            }
            flush();
            lines.RemoveAll(l => l.Count == 0 || l.All(run => run.Text.Trim().Length == 0));
            return lines;
        }

        // The new text broken after the same words as the old one, when both have the same words in
        // number; otherwise as it came.
        private static List<List<Run>> Rebreak(List<List<Run>> fresh, List<List<Run>> old)
        {
            Func<List<Run>, int> words = l => string.Concat(l.Select(r => r.Text)).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
            var want = old.Select(words).ToList();
            if (want.Count == 0 || want.Sum() != fresh.Sum(words) || want.SequenceEqual(fresh.Select(words))) return fresh;

            // Every character with its style, lines joined by a space; then cut again by word count.
            var chars = new List<KeyValuePair<char, Run>>();
            for (int l = 0; l < fresh.Count; l++)
            {
                if (l > 0) chars.Add(new KeyValuePair<char, Run>(' ', fresh[l][0]));
                foreach (var run in fresh[l]) foreach (char c in run.Text) chars.Add(new KeyValuePair<char, Run>(c, run));
            }
            var result = new List<List<Run>> { new List<Run>() };
            int line = 0, inLine = 0;
            bool inWord = false;
            foreach (var ch in chars)
            {
                if (ch.Key == ' ') inWord = false;
                else if (!inWord)
                {
                    inWord = true;
                    if (line < want.Count - 1 && inLine >= want[line]) { result.Add(new List<Run>()); line++; inLine = 0; }
                    inLine++;
                }
                var cur = result[result.Count - 1];
                var last = cur.Count > 0 ? cur[cur.Count - 1] : null;
                if (last != null && last.Bold == ch.Value.Bold && last.Italic == ch.Value.Italic) last.Text += ch.Key;
                else cur.Add(new Run { Text = ch.Key.ToString(), Bold = ch.Value.Bold, Italic = ch.Value.Italic });
            }
            foreach (var l in result)
            {
                if (l.Count == 0) continue;
                l[0].Text = l[0].Text.TrimStart(' '); l[l.Count - 1].Text = l[l.Count - 1].Text.TrimEnd(' ');
            }
            result.RemoveAll(l => l.Count == 0 || l.All(r => r.Text.Length == 0));
            return result.Count == 0 ? fresh : result;
        }

        private static double Width(GlyphTypeface face, List<Run> line, double size)
        {
            double x = 0;
            foreach (var run in line)
                foreach (char ch in run.Text)
                {
                    ushort gi;
                    if (face.CharacterToGlyphMap.TryGetValue(ch == ' ' ? ' ' : ch, out gi)) x += face.AdvanceWidths[gi] * size;
                }
            return x;
        }

        private static void DrawLine(DrawingContext dc, GlyphTypeface face, List<Run> line, double size, double midX, double baseline, Brush ink)
        {
            double x = midX - Width(face, line, size) / 2;
            // The client's bold and italic are made from the one face: a thicker stroke, a slant.
            var boldPen = new Pen(ink, size * 0.045) { LineJoin = PenLineJoin.Round }; boldPen.Freeze();
            foreach (var run in line)
            {
                var group = new GeometryGroup { FillRule = FillRule.Nonzero };
                double start = x;
                foreach (char ch in run.Text)
                {
                    ushort gi;
                    if (!face.CharacterToGlyphMap.TryGetValue(ch == ' ' ? ' ' : ch, out gi)) continue;
                    var outline = face.GetGlyphOutline(gi, size, size).Clone();
                    outline.Transform = new TranslateTransform(x - start, 0);
                    group.Children.Add(outline);
                    x += face.AdvanceWidths[gi] * size;
                }
                var t = new TransformGroup();
                if (run.Italic) t.Children.Add(new SkewTransform(-11, 0));
                t.Children.Add(new TranslateTransform(start, baseline));
                group.Transform = t;
                dc.DrawGeometry(ink, run.Bold ? boldPen : null, group);
            }
        }

        // ── pixels ───────────────────────────────────────────────────────────────────────────────

        private static int[] Median(byte[] px, int stride, int x0, int y0, int x1, int y1)
        {
            var c = new[] { new List<byte>(), new List<byte>(), new List<byte>() };
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                    for (int k = 0; k < 3; k++) c[k].Add(px[(y * stride + x) * 4 + k]);
            foreach (var l in c) l.Sort();
            return new[] { (int)c[0][c[0].Count / 2], (int)c[1][c[1].Count / 2], (int)c[2][c[2].Count / 2] };
        }

        // The connected shapes no larger than a letter.
        private static bool[] Glyphs(bool[] m, int w, int h)
        {
            var keep = new bool[m.Length];
            var seen = new bool[m.Length];
            var stack = new Stack<int>();
            var member = new List<int>();
            for (int start = 0; start < m.Length; start++)
            {
                if (!m[start] || seen[start]) continue;
                member.Clear(); stack.Push(start); seen[start] = true;
                int x0 = w, y0 = h, x1 = -1, y1 = -1;
                while (stack.Count > 0)
                {
                    int p = stack.Pop(); member.Add(p);
                    int x = p % w, y = p / w;
                    if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int q = ny * w + nx;
                            if (m[q] && !seen[q]) { seen[q] = true; stack.Push(q); }
                        }
                }
                // Not on the zone's edge (the frame comes in from there), not taller or wider than a few letters.
                if (y1 - y0 + 1 > 34 || x1 - x0 + 1 > 70 || x0 == 0 || y0 == 0 || x1 == w - 1 || y1 == h - 1) continue;
                foreach (int p in member) keep[p] = true;
            }
            return keep;
        }

        // The letters (and two pixels around them, their soft edges) refilled from the plate around.
        private static void Erase(byte[] px, int stride, bool[] glyphs, int zw, int zh)
        {
            var mask = glyphs;
            for (int s = 0; s < 2; s++)
            {
                var next = (bool[])mask.Clone();
                for (int y = 1; y < zh - 1; y++)
                    for (int x = 1; x < zw - 1; x++)
                    {
                        int i = y * zw + x;
                        if (!mask[i] && (mask[i - 1] || mask[i + 1] || mask[i - zw] || mask[i + zw])) next[i] = true;
                    }
                mask = next;
            }
            var todo = new List<int>();
            var v = new float[zw * zh * 3];
            for (int i = 0; i < zw * zh; i++)
            {
                int x = i % zw, y = i / zw, o = ((Y0 + y) * stride + X0 + x) * 4;
                if (mask[i] && x > 0 && y > 0 && x < zw - 1 && y < zh - 1) todo.Add(i);
                else { mask[i] = false; v[i * 3] = px[o]; v[i * 3 + 1] = px[o + 1]; v[i * 3 + 2] = px[o + 2]; }
            }
            // Seed with the nearest plate values, then smooth.
            var filled = new bool[zw * zh];
            for (int i = 0; i < filled.Length; i++) filled[i] = !mask[i];
            var around = new int[4];
            for (int pass = 0; pass < 60; pass++)
            {
                bool any = false;
                foreach (int i in todo)
                {
                    around[0] = i - 1; around[1] = i + 1; around[2] = i - zw; around[3] = i + zw;
                    int n = 0; float a = 0, b = 0, c = 0;
                    foreach (int k in around) { if (!filled[k]) continue; a += v[k * 3]; b += v[k * 3 + 1]; c += v[k * 3 + 2]; n++; }
                    if (n == 0) { any = true; continue; }
                    v[i * 3] = a / n; v[i * 3 + 1] = b / n; v[i * 3 + 2] = c / n; filled[i] = true;
                }
                if (!any && pass > 24) break;
            }
            foreach (int i in todo)
            {
                int o = ((Y0 + i / zw) * stride + X0 + i % zw) * 4;
                for (int k = 0; k < 3; k++) px[o + k] = (byte)Math.Max(0, Math.Min(255, Math.Round(v[i * 3 + k])));
            }
        }

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
