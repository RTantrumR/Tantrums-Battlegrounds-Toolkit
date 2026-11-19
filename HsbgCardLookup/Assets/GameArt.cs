using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HsbgCardLookup.Assets
{
    /// <summary>A card's portrait as the game ships it, and where the painting sits in it.</summary>
    public sealed class ArtPicture
    {
        public BitmapSource Image;
        /// <summary>The painted region (the texture has a white margin around it), as fractions of the image.</summary>
        public Rect Painted;
    }

    /// <summary>
    /// Card art read from the installed game's own files, so it is always the art of the patch the
    /// player has: no download, no store of pictures to go stale.
    ///
    /// One below-normal-priority thread does everything, lazily, once Hearthstone's folder is known:
    ///   1. which art each of our cards uses — from the client's card definitions. That pass is the slow
    ///      one (seconds), so its result is kept on disk per definition file, with that file's size and
    ///      date; only files that changed or are new are read again.
    ///   2. where those textures are — every card-texture bundle is listed afresh each run (a tenth of
    ///      a second for all of them), so a rewritten bundle can never leave a stale address behind.
    ///   3. then it answers requests: one texture's 256-pixel level, about a millisecond each.
    /// Killing the process at any point loses nothing: the cache file is replaced whole, and whatever
    /// was not done is done on the next start. A card it has no art for answers null and the caller
    /// uses its own fallback.
    /// </summary>
    public static class GameArt
    {
        private const int Size = 256;
        private static readonly string[] FallbackGroups = { "portpremium_*texture*.unity3d", "cardasset_*texture*.unity3d", "initial_base_global-*texture*.unity3d" };

        private sealed class Located { public string File; public TextureInfo Info; }

        private static readonly object Gate = new object();
        private static readonly ConcurrentQueue<KeyValuePair<string, Action<ArtPicture>>> Requests = new ConcurrentQueue<KeyValuePair<string, Action<ArtPicture>>>();
        private static readonly AutoResetEvent Wake = new AutoResetEvent(false);
        private static readonly Dictionary<string, ArtPicture> Memory = new Dictionary<string, ArtPicture>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, Located> _art;        // card id → its texture; null until the index is built
        private static Thread _worker;
        private static Dispatcher _ui;
        private static Action<string> _log = s => { };

        /// <summary>
        /// Begins the background work, once. <paramref name="dataDir"/> is the game's Data\Win folder,
        /// <paramref name="cards"/> the card ids worth knowing, <paramref name="cacheFile"/> where the
        /// card → art list is kept between runs.
        /// </summary>
        public static void Start(string dataDir, ICollection<string> cards, string cacheFile, Dispatcher ui, Action<string> log)
        {
            lock (Gate)
            {
                if (_worker != null || string.IsNullOrEmpty(dataDir) || !Directory.Exists(dataDir)) return;
                _ui = ui; _log = log ?? _log;
                var wanted = new HashSet<string>(cards, StringComparer.OrdinalIgnoreCase);
                _worker = new Thread(() => Run(dataDir, wanted, cacheFile)) { IsBackground = true, Name = "game-art", Priority = ThreadPriority.BelowNormal };
                _worker.Start();
            }
        }

        public static bool Started { get { lock (Gate) return _worker != null; } }

        /// <summary>
        /// The card's art, handed to <paramref name="done"/> on the UI thread: at once when it is in
        /// memory, later otherwise; null when the game has no art for it or the reader isn't running.
        /// </summary>
        public static void Request(string cardId, Action<ArtPicture> done)
        {
            if (string.IsNullOrEmpty(cardId) || !Started) { done(null); return; }
            ArtPicture hit;
            lock (Memory) if (Memory.TryGetValue(cardId, out hit)) { done(hit); return; }
            Requests.Enqueue(new KeyValuePair<string, Action<ArtPicture>>(cardId, done));
            Wake.Set();
        }

        private static void Run(string dataDir, HashSet<string> wanted, string cacheFile)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                int reread;
                var cardToArt = CardArtNames(dataDir, wanted, cacheFile, out reread);
                long defsMs = sw.ElapsedMilliseconds;

                sw.Restart();
                var need = new HashSet<string>(cardToArt.Values, StringComparer.OrdinalIgnoreCase);
                var where = new Dictionary<string, Located>(StringComparer.OrdinalIgnoreCase);
                int failed = Locate(dataDir, "cardtexture_base_global-*.unity3d", need, where);
                // A handful of cards keep their art in other bundle groups (thousands of files). Art that
                // wasn't there last time isn't looked for again until the game's file count changes.
                var absent = new HashSet<string>(need.Where(n => !where.ContainsKey(n)), StringComparer.OrdinalIgnoreCase);
                string missFile = cacheFile + ".missing";
                string stamp = Directory.GetFiles(dataDir, "*.unity3d").Length + "\t" + string.Join("\t", absent.OrderBy(x => x, StringComparer.Ordinal));
                bool known = false;
                try { known = File.Exists(missFile) && File.ReadAllText(missFile) == stamp; } catch { }
                if (absent.Count > 0 && !known)
                {
                    foreach (string group in FallbackGroups)
                    {
                        if (where.Count >= need.Count) break;
                        failed += Locate(dataDir, group, need, where);
                    }
                    absent.RemoveWhere(n => where.ContainsKey(n));
                    stamp = Directory.GetFiles(dataDir, "*.unity3d").Length + "\t" + string.Join("\t", absent.OrderBy(x => x, StringComparer.Ordinal));
                    try { File.WriteAllText(missFile, stamp); } catch { }
                }
                var art = new Dictionary<string, Located>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in cardToArt) { Located l; if (where.TryGetValue(kv.Value, out l)) art[kv.Key] = l; }
                _art = art;
                _log("[art] " + art.Count + " of " + wanted.Count + " cards have art in the game's files; definitions " + defsMs + " ms ("
                    + reread + " files read), textures " + sw.ElapsedMilliseconds + " ms" + (failed > 0 ? ", " + failed + " files unreadable" : ""));
            }
            catch (Exception ex)
            {
                _art = new Dictionary<string, Located>();
                _log("[art] index failed: " + ex.GetType().Name + ": " + ex.Message);
            }

            while (true)
            {
                KeyValuePair<string, Action<ArtPicture>> r;
                if (!Requests.TryDequeue(out r)) { Wake.WaitOne(); continue; }
                ArtPicture pic = null;
                try { pic = Load(r.Key); } catch (Exception ex) { _log("[art] " + r.Key + ": " + ex.GetType().Name + ": " + ex.Message); }
                var done = r.Value; var result = pic;
                try { _ui.BeginInvoke(new Action(() => done(result))); } catch { }
            }
        }

        private static ArtPicture Load(string cardId)
        {
            ArtPicture pic;
            lock (Memory) if (Memory.TryGetValue(cardId, out pic)) return pic;
            Located l;
            if (!_art.TryGetValue(cardId, out l)) return null;
            int w, h; byte[] px;
            using (var b = new UnityBundle(l.File)) px = l.Info.Decode(b, Size, out w, out h);
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, px, w * 4);
            bmp.Freeze();
            pic = new ArtPicture { Image = bmp, Painted = PaintedRegion(px, w, h) };
            lock (Memory) Memory[cardId] = pic;
            return pic;
        }

        // The painting sits on a white canvas; its box is everything that isn't near-white.
        private static Rect PaintedRegion(byte[] px, int w, int h)
        {
            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int o = (y * w + x) * 4;
                    if (px[o] > 244 && px[o + 1] > 244 && px[o + 2] > 244) continue;
                    if (x < x0) x0 = x; if (x > x1) x1 = x;
                    if (y < y0) y0 = y; if (y > y1) y1 = y;
                }
            if (x1 < x0 || y1 < y0) return new Rect(0, 0, 1, 1);
            return new Rect(x0 / (double)w, y0 / (double)h, (x1 - x0 + 1) / (double)w, (y1 - y0 + 1) / (double)h);
        }

        private static int Locate(string dataDir, string pattern, HashSet<string> need, Dictionary<string, Located> where)
        {
            int failed = 0;
            foreach (string f in Directory.GetFiles(dataDir, pattern))
            {
                try
                {
                    using (var b = new UnityBundle(f))
                        foreach (var t in TextureInfo.List(b))
                            if (need.Contains(t.Name) && !where.ContainsKey(t.Name)) where[t.Name] = new Located { File = f, Info = t };
                }
                catch { failed++; }                                   // mid-update or a format we don't read: skip the file
            }
            return failed;
        }

        // ── card → art name, cached per definition file ─────────────────────────────────────────
        //
        // Cache file, one record per line, tab-separated:
        //   F <file name> <size> <last write ticks>      a definition file as it was when read
        //   C <card id> <art name>                       a card it defines (wanted cards only)
        private static Dictionary<string, string> CardArtNames(string dataDir, HashSet<string> wanted, string cacheFile, out int reread)
        {
            var cached = new Dictionary<string, KeyValuePair<string, List<KeyValuePair<string, string>>>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(cacheFile))
                {
                    List<KeyValuePair<string, string>> cur = null;
                    foreach (string line in File.ReadAllLines(cacheFile))
                    {
                        var p = line.Split('\t');
                        if (p[0] == "F" && p.Length == 4) cached[p[1]] = new KeyValuePair<string, List<KeyValuePair<string, string>>>(p[2] + "\t" + p[3], cur = new List<KeyValuePair<string, string>>());
                        else if (p[0] == "C" && p.Length == 3 && cur != null) cur.Add(new KeyValuePair<string, string>(p[1], p[2]));
                    }
                }
            }
            catch { cached.Clear(); }

            reread = 0;
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var lines = new List<string>();
            foreach (string f in Directory.GetFiles(dataDir, "carddef_*.unity3d"))
            {
                var info = new FileInfo(f);
                string stamp = info.Length + "\t" + info.LastWriteTimeUtc.Ticks, name = info.Name;
                List<KeyValuePair<string, string>> cards;
                KeyValuePair<string, List<KeyValuePair<string, string>>> had;
                if (cached.TryGetValue(name, out had) && had.Key == stamp) cards = had.Value;
                else
                {
                    var all = new Dictionary<string, string>();
                    try { using (var b = new UnityBundle(f)) CardDefs.Read(b, all); }
                    catch { continue; }                               // not recorded: tried again next start
                    reread++;
                    cards = all.Where(kv => wanted.Contains(kv.Key)).ToList();
                }
                lines.Add("F\t" + name + "\t" + stamp);
                foreach (var kv in cards) { result[kv.Key] = kv.Value; lines.Add("C\t" + kv.Key + "\t" + kv.Value); }
            }
            if (reread > 0)
                try
                {
                    string tmp = cacheFile + ".tmp";
                    File.WriteAllLines(tmp, lines);
                    if (File.Exists(cacheFile)) File.Replace(tmp, cacheFile, null); else File.Move(tmp, cacheFile);
                }
                catch { }
            return result;
        }
    }
}
