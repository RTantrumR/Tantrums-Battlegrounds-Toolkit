using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HsbgCardLookup.Assets;

// Dev tool: reads card art straight from the installed game with the toolkit's own bundle reader.
//   ArtProbe <Hearthstone\Data\Win> <out dir> [card ids...]
internal static class Probe
{
    private static int Main(string[] a)
    {
        string dir = a[0], outDir = a[1];
        Directory.CreateDirectory(outDir);
        var sw = Stopwatch.StartNew();
        var cardToArt = new Dictionary<string, string>();
        int defs = 0, defFiles = 0, bad = 0;
        foreach (string f in Directory.GetFiles(dir, "carddef_*.unity3d"))
        {
            try { using (var b = new UnityBundle(f)) defs += CardDefs.Read(b, cardToArt); defFiles++; }
            catch (Exception ex) { if (bad++ < 3) Console.WriteLine("  carddef " + Path.GetFileName(f) + ": " + ex.GetType().Name + " " + ex.Message); }
        }
        Console.WriteLine("carddefs: " + defs + " defs, " + cardToArt.Count + " cards with art, " + defFiles + " files, " + bad + " failed, " + sw.ElapsedMilliseconds + " ms");

        sw.Restart();
        var where = new Dictionary<string, KeyValuePair<string, TextureInfo>>();
        int texFiles = 0; bad = 0;
        foreach (string f in Directory.GetFiles(dir, "cardtexture_base_global-*.unity3d"))
        {
            try
            {
                using (var b = new UnityBundle(f))
                    foreach (var t in TextureInfo.List(b)) where[t.Name] = new KeyValuePair<string, TextureInfo>(f, t);
                texFiles++;
            }
            catch (Exception ex) { if (bad++ < 3) Console.WriteLine("  texture " + Path.GetFileName(f) + ": " + ex.GetType().Name + " " + ex.Message); }
        }
        Console.WriteLine("textures: " + where.Count + " in " + texFiles + " files, " + bad + " failed, " + sw.ElapsedMilliseconds + " ms");

        var ids = a.Skip(2).ToList();
        if (ids.Count > 0 && File.Exists(ids[0])) ids = File.ReadAllLines(ids[0]).Where(l => l.Length > 0).ToList();
        sw.Restart();
        int ok = 0, noDef = 0, noTex = 0;
        var missing = new List<string>();
        foreach (string id in ids)
        {
            string art; KeyValuePair<string, TextureInfo> hit;
            if (!cardToArt.TryGetValue(id, out art)) { noDef++; missing.Add(id + " (no def)"); continue; }
            if (!where.TryGetValue(art, out hit)) { noTex++; missing.Add(id + " -> " + art); continue; }
            int w, h; byte[] px;
            using (var b = new UnityBundle(hit.Key)) px = hit.Value.Decode(b, 256, out w, out h);
            ok++;
            if (ids.Count <= 12)
                using (var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    var d = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
                    System.Runtime.InteropServices.Marshal.Copy(px, 0, d.Scan0, px.Length);
                    bmp.UnlockBits(d);
                    bmp.Save(Path.Combine(outDir, id + ".png"));
                    Console.WriteLine("  " + id + " -> " + art + " " + hit.Value.Width + "x" + hit.Value.Height + " fmt " + hit.Value.Format + " mips " + hit.Value.MipCount + " -> " + w + "x" + h + "  [" + Path.GetFileName(hit.Key) + "]");
                }
        }
        Console.WriteLine("decoded " + ok + " of " + ids.Count + " (no def " + noDef + ", no texture " + noTex + ") in " + sw.ElapsedMilliseconds + " ms; peak " + (Process.GetCurrentProcess().PeakWorkingSet64 >> 20) + " MB");
        foreach (string m in missing.Take(15)) Console.WriteLine("  missing: " + m);
        return 0;
    }
}
