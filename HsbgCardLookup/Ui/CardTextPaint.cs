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
    /// taken out by letting the plate flow into them, and the new text is set in the face the game uses
    /// for that language, at the size, colour, place and line spacing the old one had.
    ///
    /// Size comes first: the text keeps the old size. It breaks after the same words as the old text
    /// when that fits the plate; when a longer number makes a line too wide, the text is wrapped anew
    /// (onto one more line if need be), and only a text that still doesn't fit is set smaller.
    /// Minion cards of the 512×672 layout only; anything else comes back as it was.
    /// </summary>
    internal static class CardTextPaint
    {
        public static Action<string> Log;
        // The text plate's surroundings in a 512×672 render: where text may be, and a patch that is always plate.
        private const int X0 = 100, X1 = 424, Y0 = 428, Y1 = 566, PlateX0 = 200, PlateX1 = 320, PlateY0 = 426, PlateY1 = 438;
        private const double DefaultSize = 25, MinSize = 13, MaxSize = 30, MidX = 265, MidY = 494;
        // Room for text: full lines this wide; a line that reaches down between the two gems, this wide.
        private const double MaxLine = 288, MaxLineLow = 222, LowFrom = 549, TextTop = 430, TextBottom = 560;

        /// <param name="oldText">What the render shows (tags and line breaks as our card data has them).</param>
        /// <param name="newText">What it should show.</param>
        /// <param name="face">The game's text face for the text's language.</param>
        /// <param name="lineSpace">That language's line spacing against the English face's.</param>
        public static BitmapSource WithText(BitmapSource render, string oldText, string newText, GlyphTypeface face, double lineSpace)
        {
            try
            {
                if (face == null || render == null || string.IsNullOrEmpty(newText)) return render;
                // Renders arrive trimmed to the card: the plate is found on the whole picture they were cut from.
                var crop = Int32Rect.Empty;
                var whole = render;
                var cut = render as CroppedBitmap;
                if (cut != null) { whole = cut.Source; crop = cut.SourceRect; }
                if (whole.PixelWidth != 512 || whole.PixelHeight != 672) return render;
                var newLines = Lines(newText);
                if (newLines.Count == 0 || !Covers(face, newLines)) return render;       // a letter the face lacks
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
                var oldLines = Lines(oldText ?? "");
                double size = DefaultSize, midX = MidX, midY = MidY, pitch = lineSpace == 1 ? 1.04 : 1.2;
                Color ink = lightText ? Color.FromRgb(229, 222, 212) : Color.FromRgb(42, 30, 19);
                if (count > 40)
                {
                    ink = Color.FromRgb((byte)(r / count), (byte)(g / count), (byte)(b / count));
                    // The text's extent without the odd speck that passed for a letter: all but the
                    // outermost half percent of its pixels each way.
                    var xs = new List<int>(count); var ys = new List<int>(count);
                    for (int i = 0; i < glyphs.Length; i++) if (glyphs[i]) { xs.Add(i % zw); ys.Add(i / zw); }
                    xs.Sort(); ys.Sort();
                    int cutAt = count / 200;
                    gx0 = xs[cutAt]; gx1 = xs[count - 1 - cutAt]; gy0 = ys[cutAt]; gy1 = ys[count - 1 - cutAt];
                    midX = X0 + (gx0 + gx1 + 1) / 2.0; midY = Y0 + (gy0 + gy1 + 1) / 2.0;
                    // The old text's rows on the render: a row of pixels belongs to a line of text when it
                    // holds a fair share of what the fullest row holds.
                    var perRow = new int[zh];
                    foreach (int y in ys) perRow[y]++;
                    int fullest = perRow.Max();
                    var bands = new List<KeyValuePair<int, int>>();
                    for (int y = gy0, start = -1; y <= gy1 + 1; y++)
                    {
                        bool on = y <= gy1 && perRow[y] > fullest * 0.12;
                        if (on && start < 0) start = y;
                        if (!on && start >= 0) { if (y - start >= 5) bands.Add(new KeyValuePair<int, int>(start, y - 1)); start = -1; }
                    }
                    double bandStep = bands.Count > 1
                        ? ((bands[bands.Count - 1].Key + bands[bands.Count - 1].Value) - (bands[0].Key + bands[0].Value)) / 2.0 / (bands.Count - 1) : 0;
                    double at100 = oldLines.Count == 0 ? 0 : oldLines.Max(l => Width(face, l, 100));
                    if (at100 > 0 && (oldLines.Count == bands.Count || bands.Count == 0))
                        // Our card data breaks the text as the render does: the old text's width on the
                        // render against its width in the face gives its size.
                        size = Clamp(100.0 * (gx1 - gx0 + 1) / at100, MinSize, MaxSize);
                    else if (bands.Count > 0)
                    {
                        // No such luck (a text kept as one line): all its rows' widths together against
                        // the whole text's width in the face.
                        double inked = 0;
                        foreach (var band in bands)
                        {
                            int bx0 = zw, bx1 = -1;
                            for (int y = band.Key; y <= band.Value; y++)
                                for (int x = gx0; x <= gx1; x++)
                                    if (glyphs[y * zw + x]) { if (x < bx0) bx0 = x; if (x > bx1) bx1 = x; }
                            if (bx1 >= bx0) inked += bx1 - bx0 + 1;
                        }
                        double allText = oldLines.Sum(l => Width(face, l, 100));
                        if (allText > 0 && inked > 0) size = Clamp(100.0 * inked / allText, MinSize, MaxSize);
                    }
                    // Lines of the English face stand 1.04 em apart; another language's as its rows do.
                    if (lineSpace != 1 && bandStep > 0) pitch = Clamp(bandStep / size, 1.0, 1.6);
                    Erase(px, w, glyphs, zw, zh);
                }

                double measured = size;
                // Same breaks as before when they fit; else wrapped anew at the same size; else smaller.
                var tokens = Tokens(newLines);
                var want = oldLines.Select(l => Tokens(new List<List<Run>> { l }).Count).ToList();
                List<List<Run>> set = null;
                if (want.Count > 0 && want.Sum() == tokens.Count)
                {
                    var same = Cut(tokens, want);
                    if (Fits(face, same, size, pitch, midY)) set = same;
                }
                for (double s = size; set == null && s >= MinSize; s *= 0.96)
                {
                    // The text's own breaks (the game's, for a text it breaks by hand) before ours.
                    if (newLines.Count > 1 && Fits(face, newLines, s, pitch, midY)) { set = newLines; size = s; break; }
                    var wrapped = Wrap(face, tokens, s, pitch, midY);
                    if (wrapped != null) { set = wrapped; size = s; }
                }
                if (set == null) return render;
                Log?.Invoke("[boards] card text: measured " + measured.ToString("0.0") + " px, pitch " + pitch.ToString("0.00") + ", " + oldLines.Count + " lines; set at " + size.ToString("0.0") + " px on " + set.Count + " lines");

                var clean = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawImage(clean, new Rect(0, 0, w, h));
                    var brush = new SolidColorBrush(ink); brush.Freeze();
                    double step = size * pitch, top = Top(set.Count, step, midY);
                    for (int i = 0; i < set.Count; i++)
                    {
                        // Latin letters stand on a baseline 0.30 em under the line's middle; full-width ones fill the em.
                        bool wide = set[i].Any(run => run.Text.Any(Wide));
                        DrawLine(dc, face, set[i], size, midX, top + i * step + step / 2 + size * (wide ? 0.36 : 0.30), brush);
                    }
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

        // ── layout ───────────────────────────────────────────────────────────────────────────────

        private static double Top(int lines, double step, double midY)
        {
            double block = lines * step;
            return Clamp(midY - block / 2, TextTop, Math.Max(TextTop, TextBottom - block));
        }

        private static double Limit(int line, int lines, double step, double midY)
        {
            double bottom = Top(lines, step, midY) + (line + 1) * step;
            return bottom > LowFrom ? MaxLineLow : MaxLine;
        }

        private static bool Fits(GlyphTypeface face, List<List<Run>> lines, double size, double pitch, double midY)
        {
            double step = size * pitch;
            if (lines.Count * step > TextBottom - TextTop) return false;
            for (int i = 0; i < lines.Count; i++)
                if (Width(face, lines[i], size) > Limit(i, lines.Count, step, midY)) return false;
            return true;
        }

        // Fewest lines that hold the text at this size, then evened out; null when it doesn't fit the plate.
        private static List<List<Run>> Wrap(GlyphTypeface face, List<Token> tokens, double size, double pitch, double midY)
        {
            double step = size * pitch;
            for (int n = 1; n * step <= TextBottom - TextTop; n++)
            {
                int lines = n;
                var best = Greedy(face, tokens, size, i => Limit(i, lines, step, midY), 1.0);
                if (best == null || best.Count > n) continue;
                // Even the lines out: the narrowest measure that still gives this many lines.
                for (double f = 0.95; f >= 0.5; f -= 0.05)
                {
                    var tighter = Greedy(face, tokens, size, i => Limit(i, lines, step, midY), f);
                    if (tighter == null || tighter.Count > best.Count) break;
                    best = tighter;
                }
                return best;
            }
            return null;
        }

        private static List<List<Run>> Greedy(GlyphTypeface face, List<Token> tokens, double size, Func<int, double> limit, double factor)
        {
            var counts = new List<int>();
            double x = 0; int inLine = 0;
            foreach (var t in tokens)
            {
                double word = t.Chars.Sum(c => Advance(face, c.Key) * size), gap = t.Space && inLine > 0 ? Advance(face, ' ') * size : 0;
                if (inLine > 0 && x + gap + word > limit(counts.Count) * factor) { counts.Add(inLine); inLine = 0; x = 0; gap = 0; }
                if (word > limit(counts.Count)) return null;             // one word wider than the plate
                x += gap + word; inLine++;
            }
            if (inLine > 0) counts.Add(inLine);
            return Cut(tokens, counts);
        }

        // ── text runs and words ──────────────────────────────────────────────────────────────────

        private sealed class Run { public string Text; public bool Bold, Italic; }

        /// <summary>A word (or one full-width character): the smallest piece a line can break around.</summary>
        private sealed class Token
        {
            public readonly List<KeyValuePair<char, Run>> Chars = new List<KeyValuePair<char, Run>>();
            public bool Space;       // a space stands before it
        }

        // Chinese and Japanese are written without spaces and break between any two characters.
        private static bool Wide(char c) => (c >= 0x2E80 && c <= 0x9FFF) || (c >= 0xFF00 && c <= 0xFFEF);
        private const string NoLineStart = "，。、！？）】》：；」』,.!?):;%";

        private static List<Token> Tokens(List<List<Run>> lines)
        {
            var list = new List<Token>();
            foreach (var line in lines)
            {
                Token cur = null;
                bool space = list.Count > 0 && !Wide(list[list.Count - 1].Chars[list[list.Count - 1].Chars.Count - 1].Key);
                foreach (var run in line)
                    foreach (char c in run.Text)
                    {
                        if (c == ' ') { cur = null; space = list.Count > 0; continue; }
                        if (c == '​') { cur = null; continue; }
                        bool breakHere = cur == null
                            || ((Wide(c) || Wide(cur.Chars[cur.Chars.Count - 1].Key)) && NoLineStart.IndexOf(c) < 0);
                        if (breakHere) { cur = new Token { Space = space }; list.Add(cur); space = false; }
                        cur.Chars.Add(new KeyValuePair<char, Run>(c, run));
                    }
            }
            return list;
        }

        // Lines of runs from the words, so many to a line.
        private static List<List<Run>> Cut(List<Token> tokens, List<int> counts)
        {
            var lines = new List<List<Run>>();
            int at = 0;
            foreach (int n in counts)
            {
                var line = new List<Run>();
                for (int k = 0; k < n && at < tokens.Count; k++, at++)
                {
                    var t = tokens[at];
                    if (t.Space && k > 0) Append(line, ' ', t.Chars[0].Value);
                    foreach (var c in t.Chars) Append(line, c.Key, c.Value);
                }
                if (line.Count > 0) lines.Add(line);
            }
            return lines;
        }

        private static void Append(List<Run> line, char c, Run style)
        {
            var last = line.Count > 0 ? line[line.Count - 1] : null;
            if (last != null && last.Bold == style.Bold && last.Italic == style.Italic) last.Text += c;
            else line.Add(new Run { Text = c.ToString(), Bold = style.Bold, Italic = style.Italic });
        }

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
            // "[b]" is the game's mark for a place where a line may break (Thai is written without spaces).
            text = text.Replace("[x]", "").Replace("[b]", "​").Replace("[d]", "").Replace("\r", "");
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
                cur.Append(c == ' ' ? ' ' : c);
            }
            flush();
            lines.RemoveAll(l => l.Count == 0 || l.All(run => run.Text.Trim().Length == 0));
            return lines;
        }

        private static bool Covers(GlyphTypeface face, List<List<Run>> lines)
        {
            foreach (var line in lines)
                foreach (var run in line)
                    foreach (char c in run.Text)
                        if (c != '​' && !face.CharacterToGlyphMap.ContainsKey(c)) return false;
            return true;
        }

        private static double Advance(GlyphTypeface face, char c)
        {
            ushort gi;
            return face.CharacterToGlyphMap.TryGetValue(c, out gi) ? face.AdvanceWidths[gi] : 0;
        }

        private static double Width(GlyphTypeface face, List<Run> line, double size)
        {
            double x = 0;
            foreach (var run in line) foreach (char ch in run.Text) x += Advance(face, ch) * size;
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
                    if (!face.CharacterToGlyphMap.TryGetValue(ch, out gi)) continue;
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
                // Not on the zone's edge (the frame comes in from there) and not the size of the plate
                // itself. Bold full-width characters run together, along a line and from line to line.
                if (y1 - y0 + 1 > 110 || x1 - x0 + 1 > 290 || x0 == 0 || y0 == 0 || x1 == w - 1 || y1 == h - 1) continue;
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
