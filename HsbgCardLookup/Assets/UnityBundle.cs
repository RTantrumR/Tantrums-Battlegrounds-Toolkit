using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HsbgCardLookup.Assets
{
    // A reader for exactly what Hearthstone ships its card art in, measured 2026-11-19 on client 37.x
    // (Unity 6000.3): UnityFS v8 archives with LZ4 blocks, holding a version-22 serialized file with its
    // type trees embedded, textures in DXT1/DXT5 with their pixels in a ".resS" entry of the same archive.
    // Nothing else is handled; anything unexpected throws and the caller skips that file.
    //
    // Built for a weak PC: a bundle is never loaded whole. The archive's uncompressed stream is addressed
    // by offset and only the 128 KB blocks a read touches are decompressed.

    /// <summary>One UnityFS archive, read by offset into its uncompressed stream.</summary>
    internal sealed class UnityBundle : IDisposable
    {
        internal struct Entry { public long Offset, Size; public string Path; }

        private struct Block { public long UOffset, COffset; public int USize, CSize, Flags; }

        private readonly FileStream _fs;
        private Block[] _blocks;
        private byte[] _cache, _packed = new byte[0];
        private int _cached = -1;

        public Entry[] Entries { get; private set; }

        public UnityBundle(string path)
        {
            _fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.RandomAccess);
            try { ReadHeader(); } catch { _fs.Dispose(); throw; }
        }

        public void Dispose() { _fs.Dispose(); }

        private void ReadHeader()
        {
            var head = new byte[256];
            int got = _fs.Read(head, 0, head.Length);
            int p = 0;
            if (CString(head, ref p) != "UnityFS") throw new InvalidDataException("not UnityFS");
            uint version = BE32(head, ref p);
            CString(head, ref p); CString(head, ref p);          // player version, engine revision
            p += 8;                                               // total size
            int packedInfo = (int)BE32(head, ref p), rawInfo = (int)BE32(head, ref p);
            uint flags = BE32(head, ref p);
            if (version >= 7) p = (p + 15) & ~15;
            if (p > got) throw new InvalidDataException("short header");

            long infoAt = (flags & 0x80) != 0 ? _fs.Length - packedInfo : p;
            var packed = new byte[packedInfo];
            _fs.Position = infoAt;
            ReadFully(_fs, packed, packedInfo);
            long dataStart = (flags & 0x80) != 0 ? p : infoAt + packedInfo;
            if ((flags & 0x200) != 0) dataStart = (dataStart + 15) & ~15L;

            byte[] info = Unpack(packed, packedInfo, rawInfo, (int)(flags & 0x3F));
            int q = 16;                                           // hash
            int blockCount = (int)BE32(info, ref q);
            _blocks = new Block[blockCount];
            long u = 0, c = dataStart;
            for (int i = 0; i < blockCount; i++)
            {
                int us = (int)BE32(info, ref q), cs = (int)BE32(info, ref q);
                int f = (info[q] << 8) | info[q + 1]; q += 2;
                _blocks[i] = new Block { UOffset = u, COffset = c, USize = us, CSize = cs, Flags = f & 0x3F };
                u += us; c += cs;
            }
            int entryCount = (int)BE32(info, ref q);
            Entries = new Entry[entryCount];
            for (int i = 0; i < entryCount; i++)
            {
                long off = BE64(info, ref q), size = BE64(info, ref q);
                q += 4;
                Entries[i] = new Entry { Offset = off, Size = size, Path = CString(info, ref q) };
            }
        }

        /// <summary>Bytes of the uncompressed stream.</summary>
        public void Read(long offset, byte[] buffer, int at, int count)
        {
            int b = FindBlock(offset);
            while (count > 0)
            {
                if (b >= _blocks.Length) throw new EndOfStreamException();
                Load(b);
                int inBlock = (int)(offset - _blocks[b].UOffset);
                int n = Math.Min(count, _blocks[b].USize - inBlock);
                Buffer.BlockCopy(_cache, inBlock, buffer, at, n);
                offset += n; at += n; count -= n; b++;
            }
        }

        public byte[] Read(long offset, int count) { var d = new byte[count]; Read(offset, d, 0, count); return d; }

        private int FindBlock(long offset)
        {
            int lo = 0, hi = _blocks.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (_blocks[mid].UOffset <= offset) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        private void Load(int b)
        {
            if (_cached == b) return;
            var k = _blocks[b];
            if (_packed.Length < k.CSize) _packed = new byte[k.CSize];
            _fs.Position = k.COffset;
            ReadFully(_fs, _packed, k.CSize);
            _cache = Unpack(_packed, k.CSize, k.USize, k.Flags);
            _cached = b;
        }

        private static byte[] Unpack(byte[] src, int srcLen, int rawLen, int method)
        {
            var dst = new byte[rawLen];
            if (method == 0) Buffer.BlockCopy(src, 0, dst, 0, rawLen);
            else if (method == 2 || method == 3) Lz4(src, srcLen, dst);
            else throw new NotSupportedException("compression " + method);
            return dst;
        }

        // LZ4 block format: token (literal length | match length), literals, 2-byte offset, match.
        private static void Lz4(byte[] src, int srcLen, byte[] dst)
        {
            int s = 0, d = 0;
            while (s < srcLen)
            {
                int token = src[s++];
                int lit = token >> 4;
                if (lit == 15) { int x; do { x = src[s++]; lit += x; } while (x == 255); }
                Buffer.BlockCopy(src, s, dst, d, lit);
                s += lit; d += lit;
                if (s >= srcLen) break;
                int back = src[s] | (src[s + 1] << 8); s += 2;
                int len = token & 15;
                if (len == 15) { int x; do { x = src[s++]; len += x; } while (x == 255); }
                len += 4;
                int m = d - back;
                if (back >= len) { Buffer.BlockCopy(dst, m, dst, d, len); d += len; }
                else while (len-- > 0) dst[d++] = dst[m++];       // overlapping: byte by byte
            }
            if (d != dst.Length) throw new InvalidDataException("LZ4 length");
        }

        private static void ReadFully(Stream s, byte[] b, int count)
        {
            int at = 0;
            while (at < count)
            {
                int n = s.Read(b, at, count - at);
                if (n <= 0) throw new EndOfStreamException();
                at += n;
            }
        }

        private static uint BE32(byte[] b, ref int p) { uint v = (uint)(b[p] << 24 | b[p + 1] << 16 | b[p + 2] << 8 | b[p + 3]); p += 4; return v; }
        private static long BE64(byte[] b, ref int p) { long hi = BE32(b, ref p); return (hi << 32) | BE32(b, ref p); }
        internal static string CString(byte[] b, ref int p)
        {
            int e = p;
            while (b[e] != 0) e++;
            string s = Encoding.UTF8.GetString(b, p, e - p);
            p = e + 1;
            return s;
        }
    }

    /// <summary>The object table and type trees of a serialized file inside a bundle.</summary>
    internal sealed class SerializedAssets
    {
        internal struct Node { public int Level, ByteSize, Meta; public bool IsArray; public string Name; }
        internal struct Obj { public long PathId, Start; public int Size, Type; }

        public const int ClassGameObject = 1, ClassTexture2D = 28, ClassMonoBehaviour = 114;

        // The few names Unity keeps in its built-in string table that we look for (offset → name).
        private static readonly Dictionary<int, string> Common = new Dictionary<int, string>
        {
            { 49, "Array" }, { 106, "data" }, { 374, "m_GameObject" }, { 427, "m_Name" }, { 795, "size" },
        };

        private readonly UnityBundle _bundle;
        private readonly long _base;
        public int[] ClassIds;
        public Node[][] Trees;
        public Obj[] Objects;

        public SerializedAssets(UnityBundle bundle, UnityBundle.Entry entry)
        {
            _bundle = bundle; _base = entry.Offset;
            var h = bundle.Read(_base, 48);
            int p = 8;
            int version = h[p] << 24 | h[p + 1] << 16 | h[p + 2] << 8 | h[p + 3];
            if (version != 22) throw new NotSupportedException("serialized file version " + version);
            if (h[16] != 0) throw new NotSupportedException("big-endian file");
            int metaSize = h[20] << 24 | h[21] << 16 | h[22] << 8 | h[23];
            long dataOffset = 0;
            for (int i = 32; i < 40; i++) dataOffset = (dataOffset << 8) | h[i];

            var m = bundle.Read(_base + 48, metaSize);
            int q = 0;
            UnityBundle.CString(m, ref q);                        // unity version
            q += 4;                                               // target platform
            bool trees = m[q++] != 0;
            if (!trees) throw new NotSupportedException("no type trees");
            int typeCount = I32(m, ref q);
            ClassIds = new int[typeCount]; Trees = new Node[typeCount][];
            for (int t = 0; t < typeCount; t++)
            {
                int classId = I32(m, ref q);
                q += 1 + 2;                                       // stripped, script type index
                if (classId == ClassMonoBehaviour) q += 16;       // script id
                q += 16;                                          // type hash
                int nodeCount = I32(m, ref q), strSize = I32(m, ref q);
                int nodesAt = q, strAt = q + nodeCount * 32;
                var nodes = new Node[nodeCount];
                for (int n = 0; n < nodeCount; n++)
                {
                    int o = nodesAt + n * 32;
                    int nameOff = BitConverter.ToInt32(m, o + 8);
                    nodes[n] = new Node
                    {
                        Level = m[o + 2], IsArray = (m[o + 3] & 1) != 0,
                        ByteSize = BitConverter.ToInt32(m, o + 12), Meta = BitConverter.ToInt32(m, o + 20),
                        Name = Str(m, strAt, nameOff)
                    };
                }
                q = strAt + strSize;
                int deps = I32(m, ref q);
                q += deps * 4;
                ClassIds[t] = classId; Trees[t] = nodes;
            }
            int objCount = I32(m, ref q);
            Objects = new Obj[objCount];
            for (int i = 0; i < objCount; i++)
            {
                q = (q + 3) & ~3;
                long pathId = BitConverter.ToInt64(m, q), start = BitConverter.ToInt64(m, q + 8);
                Objects[i] = new Obj { PathId = pathId, Start = dataOffset + start, Size = BitConverter.ToInt32(m, q + 16), Type = BitConverter.ToInt32(m, q + 20) };
                q += 24;
            }
        }

        public byte[] Data(Obj o) => _bundle.Read(_base + o.Start, o.Size);

        public bool HasField(int type, string name)
        {
            foreach (var n in Trees[type]) if (n.Level == 1 && n.Name == name) return true;
            return false;
        }

        private static string Str(byte[] m, int strAt, int off)
        {
            if (off < 0) { string s; return Common.TryGetValue(off & 0x7FFFFFFF, out s) ? s : ""; }
            int p = strAt + off;
            return UnityBundle.CString(m, ref p);
        }

        private static int I32(byte[] b, ref int p) { int v = BitConverter.ToInt32(b, p); p += 4; return v; }

        /// <summary>
        /// Walks an object by its type tree and returns the wanted fields: numbers as long, strings and
        /// byte arrays as byte[]. Keys are field paths ("m_Name", "m_StreamData.offset"). Stops as soon as
        /// every wanted field is read, so fields late in a big object are never parsed.
        /// </summary>
        public static Dictionary<string, object> Fields(Node[] tree, byte[] data, HashSet<string> want)
        {
            var found = new Dictionary<string, object>();
            int pos = 0;
            Walk(tree, 0, data, ref pos, "", want, found);
            return found;
        }

        private static int Walk(Node[] t, int i, byte[] d, ref int pos, string path, HashSet<string> want, Dictionary<string, object> found)
        {
            var n = t[i];
            int end = i + 1;
            while (end < t.Length && t[end].Level > n.Level) end++;
            if (found.Count == want.Count) return end;

            if (end == i + 1)                                     // a leaf: a fixed-size value
            {
                if (want.Contains(path))
                    found[path] = n.ByteSize == 8 ? BitConverter.ToInt64(d, pos) : n.ByteSize == 4 ? BitConverter.ToInt32(d, pos)
                                : n.ByteSize == 2 ? BitConverter.ToInt16(d, pos) : (long)d[pos];
                pos += n.ByteSize;
            }
            else if (n.IsArray)                                   // size, then that many of the element
            {
                int count = BitConverter.ToInt32(d, pos); pos += 4;
                int element = i + 2;
                bool leaf = element + 1 >= end;
                if (leaf)
                {
                    int bytes = count * t[element].ByteSize;
                    if (want.Contains(path)) { var v = new byte[bytes]; Buffer.BlockCopy(d, pos, v, 0, bytes); found[path] = v; }
                    pos += bytes;
                }
                else
                    for (int k = 0; k < count && found.Count < want.Count; k++) Walk(t, element, d, ref pos, path, want, found);
            }
            else                                                  // a struct: its children in order
            {
                for (int c = i + 1; c < end;)
                {
                    string name = t[c].Name;
                    string child = t[c].IsArray ? path : path.Length == 0 ? name : path + "." + name;   // an array is anonymous
                    c = Walk(t, c, d, ref pos, child, want, found);
                }
            }
            if ((n.Meta & 0x4000) != 0) pos = (pos + 3) & ~3;
            return end;
        }
    }

    /// <summary>Where one texture's pixels are, as its bundle describes it.</summary>
    internal sealed class TextureInfo
    {
        public string Name;
        public int Width, Height, Format, MipCount;
        public long StreamOffset;       // into the archive's uncompressed stream
        public int StreamSize;

        public const int Dxt1 = 10, Dxt5 = 12;

        /// <summary>Every DXT texture of a bundle whose pixels sit in its ".resS" entry.</summary>
        public static List<TextureInfo> List(UnityBundle bundle)
        {
            var list = new List<TextureInfo>();
            var want = new HashSet<string> { "m_Name", "m_Width", "m_Height", "m_TextureFormat", "m_MipCount", "m_StreamData.offset", "m_StreamData.size", "m_StreamData.path" };
            foreach (var entry in bundle.Entries)
            {
                if (entry.Path.EndsWith(".resS", StringComparison.OrdinalIgnoreCase) || entry.Path.EndsWith(".resource", StringComparison.OrdinalIgnoreCase)) continue;
                var file = new SerializedAssets(bundle, entry);
                foreach (var o in file.Objects)
                {
                    if (file.ClassIds[o.Type] != SerializedAssets.ClassTexture2D) continue;
                    var f = SerializedAssets.Fields(file.Trees[o.Type], file.Data(o), want);
                    object name, path, w, h, fmt, mips, off, size;
                    if (!f.TryGetValue("m_Name", out name) || !f.TryGetValue("m_StreamData.path", out path) || !f.TryGetValue("m_StreamData.size", out size)) continue;
                    if (!f.TryGetValue("m_Width", out w) || !f.TryGetValue("m_Height", out h) || !f.TryGetValue("m_TextureFormat", out fmt)
                        || !f.TryGetValue("m_MipCount", out mips) || !f.TryGetValue("m_StreamData.offset", out off)) continue;
                    int format = (int)(long)Convert.ToInt64(fmt);
                    if ((format != Dxt1 && format != Dxt5) || Convert.ToInt64(size) == 0) continue;
                    string res = Encoding.UTF8.GetString((byte[])path);
                    res = res.Substring(res.LastIndexOf('/') + 1);
                    long resAt = -1;
                    foreach (var e in bundle.Entries) if (e.Path == res) resAt = e.Offset;
                    if (resAt < 0) continue;
                    list.Add(new TextureInfo
                    {
                        Name = Encoding.UTF8.GetString((byte[])name),
                        Width = (int)Convert.ToInt64(w), Height = (int)Convert.ToInt64(h), Format = format, MipCount = (int)Convert.ToInt64(mips),
                        StreamOffset = resAt + Convert.ToInt64(off), StreamSize = (int)Convert.ToInt64(size)
                    });
                }
            }
            return list;
        }

        /// <summary>
        /// The mip level whose width is closest to (not below) <paramref name="width"/>, as top-down
        /// BGRA. The game stores every size already, so nothing is ever scaled here.
        /// </summary>
        public byte[] Decode(UnityBundle bundle, int width, out int w, out int h)
        {
            int level = 0;
            while (level + 1 < MipCount && (Width >> (level + 1)) >= width) level++;
            int blockBytes = Format == Dxt1 ? 8 : 16;
            long at = StreamOffset;
            for (int l = 0; l < level; l++) at += Math.Max(1, (Width >> l) / 4) * (long)Math.Max(1, (Height >> l) / 4) * blockBytes;
            w = Math.Max(1, Width >> level); h = Math.Max(1, Height >> level);
            int bw = Math.Max(1, w / 4), bh = Math.Max(1, h / 4);
            var src = bundle.Read(at, bw * bh * blockBytes);
            var dst = new byte[w * h * 4];
            var c = new uint[4];
            var a = new byte[8];
            int s = 0;
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    ulong alphaBits = 0;
                    if (Format == Dxt5)
                    {
                        a[0] = src[s]; a[1] = src[s + 1];
                        for (int i = 2; i < 8; i++)
                            a[i] = a[0] > a[1] ? (byte)(((8 - i) * a[0] + (i - 1) * a[1]) / 7)
                                 : i < 6 ? (byte)(((6 - i) * a[0] + (i - 1) * a[1]) / 5) : (byte)(i == 6 ? 0 : 255);
                        for (int i = 7; i >= 2; i--) alphaBits = (alphaBits << 8) | src[s + i];
                        s += 8;
                    }
                    int c0 = src[s] | src[s + 1] << 8, c1 = src[s + 2] | src[s + 3] << 8;
                    uint bits = (uint)(src[s + 4] | src[s + 5] << 8 | src[s + 6] << 16 | src[s + 7] << 24);
                    s += 8;
                    int r0 = (c0 >> 11) * 255 / 31, g0 = ((c0 >> 5) & 63) * 255 / 63, b0 = (c0 & 31) * 255 / 31;
                    int r1 = (c1 >> 11) * 255 / 31, g1 = ((c1 >> 5) & 63) * 255 / 63, b1 = (c1 & 31) * 255 / 31;
                    c[0] = Bgra(r0, g0, b0, 255); c[1] = Bgra(r1, g1, b1, 255);
                    if (c0 > c1 || Format == Dxt5)
                    {
                        c[2] = Bgra((2 * r0 + r1) / 3, (2 * g0 + g1) / 3, (2 * b0 + b1) / 3, 255);
                        c[3] = Bgra((r0 + 2 * r1) / 3, (g0 + 2 * g1) / 3, (b0 + 2 * b1) / 3, 255);
                    }
                    else { c[2] = Bgra((r0 + r1) / 2, (g0 + g1) / 2, (b0 + b1) / 2, 255); c[3] = 0; }
                    for (int py = 0; py < 4; py++)
                    {
                        int row = h - 1 - (by * 4 + py);            // stored bottom-up
                        if (row < 0) continue;
                        for (int px = 0; px < 4; px++)
                        {
                            int x = bx * 4 + px;
                            if (x >= w) continue;
                            uint v = c[(bits >> (2 * (py * 4 + px))) & 3];
                            int o = (row * w + x) * 4;
                            dst[o] = (byte)v; dst[o + 1] = (byte)(v >> 8); dst[o + 2] = (byte)(v >> 16);
                            dst[o + 3] = Format == Dxt5 ? a[(alphaBits >> (3 * (py * 4 + px))) & 7] : (byte)(v >> 24);
                        }
                    }
                }
            return dst;
        }

        private static uint Bgra(int r, int g, int b, int a) => (uint)(b | g << 8 | r << 16 | a << 24);
    }

    /// <summary>A font file the game carries (its number face is "Belwe").</summary>
    internal static class GameFont
    {
        private const int ClassFont = 128;

        /// <summary>The font's file bytes (TTF/OTF), or null when this bundle has no font of that name.</summary>
        public static byte[] Read(UnityBundle bundle, string name)
        {
            var want = new HashSet<string> { "m_Name", "m_FontData" };
            foreach (var entry in bundle.Entries)
            {
                if (entry.Path.EndsWith(".resS", StringComparison.OrdinalIgnoreCase) || entry.Path.EndsWith(".resource", StringComparison.OrdinalIgnoreCase)) continue;
                var file = new SerializedAssets(bundle, entry);
                foreach (var o in file.Objects)
                {
                    if (file.ClassIds[o.Type] != ClassFont) continue;
                    var f = SerializedAssets.Fields(file.Trees[o.Type], file.Data(o), want);
                    object n, data;
                    if (f.TryGetValue("m_Name", out n) && Encoding.UTF8.GetString((byte[])n) == name
                        && f.TryGetValue("m_FontData", out data) && ((byte[])data).Length > 1000) return (byte[])data;
                }
            }
            return null;
        }
    }

    /// <summary>Card id → the art its definition names, from the client's CardDef prefabs.</summary>
    internal static class CardDefs
    {
        // The field was renamed once already (36.2 renamed the golden ones); list every known name.
        private static readonly string[] PortraitFields = { "m_PortraitTexturePath" };

        /// <summary>Adds every card of one carddef bundle. Returns how many definitions it held.</summary>
        public static int Read(UnityBundle bundle, Dictionary<string, string> cardToArt)
        {
            int defs = 0;
            var wantName = new HashSet<string> { "m_Name" };
            foreach (var entry in bundle.Entries)
            {
                if (entry.Path.EndsWith(".resS", StringComparison.OrdinalIgnoreCase) || entry.Path.EndsWith(".resource", StringComparison.OrdinalIgnoreCase)) continue;
                var file = new SerializedAssets(bundle, entry);
                Dictionary<long, int> byPath = null;
                for (int i = 0; i < file.Objects.Length; i++)
                {
                    var o = file.Objects[i];
                    if (file.ClassIds[o.Type] != SerializedAssets.ClassMonoBehaviour) continue;
                    string field = null;
                    foreach (string f in PortraitFields) if (file.HasField(o.Type, f)) { field = f; break; }
                    if (field == null) continue;
                    var got = SerializedAssets.Fields(file.Trees[o.Type], file.Data(o), new HashSet<string> { "m_GameObject.m_PathID", field });
                    object owner, art;
                    if (!got.TryGetValue("m_GameObject.m_PathID", out owner) || !got.TryGetValue(field, out art)) continue;
                    defs++;
                    string artId = ArtId(Encoding.UTF8.GetString((byte[])art));
                    if (artId.Length == 0) continue;
                    if (byPath == null)
                    {
                        byPath = new Dictionary<long, int>();
                        for (int k = 0; k < file.Objects.Length; k++) byPath[file.Objects[k].PathId] = k;
                    }
                    int go;
                    if (!byPath.TryGetValue(Convert.ToInt64(owner), out go)) continue;
                    var g = file.Objects[go];
                    object name;
                    if (SerializedAssets.Fields(file.Trees[g.Type], file.Data(g), wantName).TryGetValue("m_Name", out name))
                        cardToArt[Encoding.UTF8.GetString((byte[])name)] = artId;
                }
            }
            return defs;
        }

        // "HS26-058_M.tif:02537bd0…" → "HS26-058_M" (the texture's name); .psd and .png occur too.
        internal static string ArtId(string path)
        {
            int colon = path.IndexOf(':');
            if (colon >= 0) path = path.Substring(0, colon);
            foreach (string ext in new[] { ".tif", ".psd", ".png", ".tga" })
                if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return path.Substring(0, path.Length - ext.Length);
            return path;
        }
    }
}
