using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Xml;

namespace HsbgCardLookup.Config
{
    // Settings as a file a player can hand to another player, and as named presets kept beside
    // config.xml. One format for both (".tbtconfig", a small XML list of settings), and ONE way in:
    // every file, a preset this program wrote included, is read by Read() below.
    //
    // A file from someone else is hostile until shown otherwise, so Read() is a whitelist, not a
    // deserializer: nothing in the file names a type, a path or a program. The checks, in order:
    // the extension; the size (read from the open file, never trusted from the directory); the bytes
    // (strict UTF-8, no control characters, no DOCTYPE / comment / CDATA / processing instruction);
    // the XML itself (DTDs prohibited, no resolver, so no entity can expand or reach outside); the
    // shape (known root, known setting names only, each at most once, no attributes, no namespaces);
    // and each value against what that one setting may hold (a switch is "true" or "false", a mode is
    // one of its listed words, a key is a named key, a number is finite and within its range).
    // Nothing is applied unless the whole file passes.
    //
    // What a file can never carry, because it is not in Shared: folders (the card art folder), the
    // language, update and cache bookkeeping, the start-with-Windows choice.
    public static class ConfigShare
    {
        public const string Extension = ".tbtconfig";
        public const int MaxBytes = 64 * 1024;
        public const int MaxPresets = 30;
        private const int FormatVersion = 1;
        private const string Root = "ToolkitConfig";

        public enum Problem { None, Type, Size, Bytes, Xml, Newer, Content, Read }

        public sealed class Result
        {
            public Problem Problem;
            public string Detail = "";                                  // the setting a Content problem is about
            internal readonly List<Action<PluginConfig>> Setters = new List<Action<PluginConfig>>();
            public bool Ok => Problem == Problem.None;
            public int Count => Setters.Count;
            /// <summary>Put the file's settings onto the live config. Only for a result that is Ok.</summary>
            public void ApplyTo(PluginConfig config) { if (Ok) foreach (var set in Setters) set(config); }
        }

        // The settings a file may hold. Everything else in PluginConfig stays this PC's own.
        private static readonly string[] Shared =
        {
            "BrowserKey", "GoldenKey", "FocusKey", "ShowDuos", "ShowSearchButton", "HideDraggedWithApp",
            "DragFromDetail", "DragFromGrid", "FloatingCardWidth",
            "ShowTrinkets", "ShowAnomaly", "LesserTrinketHud", "GreaterTrinketHud", "Trinket3Hud", "Trinket4Hud", "AnomalyHud",
            "ShowOpponentMmr", "OpponentMmrMode", "OpponentNameMode", "OpponentNameType", "ShowMmrDeltas", "TavernTierMode",
            "ShowLastOpponent", "DimDeadPlayers", "OpponentTribeMode", "PortraitTextSize", "PortraitIconSize", "MmrPanelHud",
            "ShowLobbyTribes", "LobbyTribesLayout", "LobbyTribesHud", "AssemblePanels", "PanelLinks", "ShowTurnCounter", "TurnCounterHud",
            "ShowOpponentBoards", "BoardPinOnClick", "OpponentBoardsHud",
            "ShowDarkGifts", "DarkGiftMode", "DarkGiftPoolCount",
        };

        private static readonly string[] Places = { "Off", "Portraits", "Panel", "Both" };
        private static readonly HashSet<string> KeyNames = new HashSet<string>(Enum.GetNames(typeof(Key)), StringComparer.Ordinal);
        private static readonly string[] NoKeys = { "LeftAlt", "RightAlt", "System", "LWin", "RWin" };

        // What each text setting may say. A text setting with no entry here is refused outright.
        private static readonly Dictionary<string, Func<string, bool>> Words = new Dictionary<string, Func<string, bool>>
        {
            { "BrowserKey", IsKey }, { "GoldenKey", IsKey }, { "FocusKey", IsKey },
            { "OpponentMmrMode", v => Places.Contains(v) }, { "OpponentNameMode", v => Places.Contains(v) },
            { "TavernTierMode", v => Places.Contains(v) }, { "OpponentTribeMode", v => Places.Contains(v) },
            { "OpponentNameType", v => v == "Battletags" || v == "Heroes" },
            { "DarkGiftMode", v => v == "Both" || v == "Gifts" || v == "Minions" },
            { "LobbyTribesLayout", v => v == "1x5" || v == "5x1" || v == "2x3" || v == "3x2" },
        };

        private static bool IsKey(string v) => KeyNames.Contains(v) && !NoKeys.Contains(v);

        // A placement's numbers: canvas fractions, a scale or a width in pixels. Wide enough for any
        // real layout, narrow enough that nothing lands a thousand screens away.
        private static readonly Dictionary<string, double[]> PlaceRange = new Dictionary<string, double[]>
        {
            { "X", new[] { -20000.0, 20000 } }, { "Y", new[] { -20000.0, 20000 } }, { "W", new[] { 0.0, 8000 } },
            { "XF", new[] { -1.0, 2 } }, { "YF", new[] { -1.0, 2 } }, { "WF", new[] { 0.0, 20 } }, { "Px", new[] { 0.0, 8000 } },
        };

        private static readonly Regex LinkRx = new Regex(@"^(tribes|sidebar|turn)>(tribes|sidebar|turn):(Top|Bottom|Left|Right)$", RegexOptions.CultureInvariant);

        // ── Writing ──────────────────────────────────────────────────────────────────────────

        public static string Write(PluginConfig config)
        {
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true };
            using (var w = XmlWriter.Create(sb, settings))
            {
                w.WriteStartElement(Root);
                w.WriteAttributeString("version", FormatVersion.ToString(CultureInfo.InvariantCulture));
                foreach (string name in Shared)
                {
                    var prop = typeof(PluginConfig).GetProperty(name);
                    object value = prop.GetValue(config);
                    w.WriteStartElement(name);
                    if (value is HudPlacement place)
                    {
                        w.WriteElementString("Set", place.Set ? "true" : "false");
                        foreach (var kv in PlaceRange)
                            w.WriteElementString(kv.Key, Num((double)typeof(HudPlacement).GetProperty(kv.Key).GetValue(place)));
                    }
                    else if (value is List<string> list) { foreach (string s in list) w.WriteElementString("Link", s ?? ""); }
                    else if (value is bool b) w.WriteString(b ? "true" : "false");
                    else if (value is double d) w.WriteString(Num(d));
                    else w.WriteString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }
            return sb.ToString();
        }

        private static string Num(double d) => d.ToString("R", CultureInfo.InvariantCulture);

        public static bool WriteFile(string path, PluginConfig config)
        {
            try { File.WriteAllText(path, Write(config), new UTF8Encoding(false)); return true; }
            catch { return false; }
        }

        // ── Reading ──────────────────────────────────────────────────────────────────────────

        public static Result ReadFile(string path)
        {
            var r = new Result();
            byte[] bytes;
            try
            {
                if (!string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase)) { r.Problem = Problem.Type; return r; }
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (fs.Length < 1 || fs.Length > MaxBytes) { r.Problem = Problem.Size; return r; }
                    bytes = new byte[(int)fs.Length];
                    int got = 0;
                    while (got < bytes.Length)
                    {
                        int n = fs.Read(bytes, got, bytes.Length - got);
                        if (n <= 0) break;
                        got += n;
                    }
                    // Grew or shrank under us: not the file that was measured.
                    if (got != bytes.Length || fs.ReadByte() != -1) { r.Problem = Problem.Size; return r; }
                }
            }
            catch { r.Problem = Problem.Read; return r; }
            return Read(bytes);
        }

        public static Result Read(byte[] bytes)
        {
            var r = new Result();
            if (bytes == null || bytes.Length < 1 || bytes.Length > MaxBytes) { r.Problem = Problem.Size; return r; }

            string text;
            try
            {
                int skip = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                text = new UTF8Encoding(false, true).GetString(bytes, skip, bytes.Length - skip);     // throws on bytes that are not UTF-8
            }
            catch { r.Problem = Problem.Bytes; return r; }
            foreach (char c in text)
                if ((c < 0x20 && c != '\t' && c != '\r' && c != '\n') || c == 0x7F || c == '￾' || c == '￿') { r.Problem = Problem.Bytes; return r; }
            // None of these has any business in a list of settings, and each is a way to make an XML
            // reader do more than read: DOCTYPE (entities), CDATA, comments, processing instructions.
            if (text.IndexOf("<!", StringComparison.Ordinal) >= 0 || text.IndexOf("<?", StringComparison.Ordinal) >= 0) { r.Problem = Problem.Bytes; return r; }
            if (!text.TrimStart().StartsWith("<" + Root, StringComparison.Ordinal)) { r.Problem = Problem.Xml; return r; }

            var doc = new XmlDocument { XmlResolver = null };
            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes,
                    MaxCharactersFromEntities = 1024, IgnoreWhitespace = true, CheckCharacters = true
                };
                using (var reader = XmlReader.Create(new StringReader(text), settings)) doc.Load(reader);
            }
            catch { r.Problem = Problem.Xml; return r; }

            var root = doc.DocumentElement;
            if (root == null || root.Name != Root || root.NamespaceURI.Length > 0 || doc.ChildNodes.Count != 1) { r.Problem = Problem.Xml; return r; }
            if (root.Attributes.Count != 1 || root.Attributes[0].Name != "version") { r.Problem = Problem.Xml; return r; }
            int version;
            if (!int.TryParse(root.GetAttribute("version"), NumberStyles.None, CultureInfo.InvariantCulture, out version) || version < 1) { r.Problem = Problem.Xml; return r; }
            if (version > FormatVersion) { r.Problem = Problem.Newer; return r; }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var keys = new Dictionary<string, string>();
            foreach (XmlNode node in root.ChildNodes)
            {
                var el = node as XmlElement;
                string name = el?.Name ?? "";
                if (el == null || el.HasAttributes || el.NamespaceURI.Length > 0 || !Shared.Contains(name) || !seen.Add(name)
                    || !ReadSetting(el, typeof(PluginConfig).GetProperty(name), r, keys))
                {
                    r.Problem = Problem.Content;
                    r.Detail = name.Length > 40 ? name.Substring(0, 40) : name;
                    r.Setters.Clear();
                    return r;
                }
            }
            // Two actions on one key: only one of them would ever fire.
            var bound = keys.Values.Where(k => k != "None").ToList();
            if (seen.Count == 0 || bound.Count != bound.Distinct().Count())
            {
                r.Problem = Problem.Content; r.Detail = seen.Count == 0 ? "" : "BrowserKey"; r.Setters.Clear();
            }
            return r;
        }

        private static bool ReadSetting(XmlElement el, PropertyInfo prop, Result r, Dictionary<string, string> keys)
        {
            Type t = prop.PropertyType;
            if (t == typeof(HudPlacement)) return ReadPlacement(el, prop, r);
            if (t == typeof(List<string>)) return ReadLinks(el, prop, r);

            string v;
            if (!Leaf(el, out v)) return false;
            if (t == typeof(bool))
            {
                if (v != "true" && v != "false") return false;
                bool b = v == "true";
                r.Setters.Add(c => prop.SetValue(c, b));
                return true;
            }
            if (t == typeof(int))
            {
                int i;
                if (!int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out i) || i > 1000) return false;
                r.Setters.Add(c => prop.SetValue(c, i));
                return true;
            }
            if (t == typeof(double))
            {
                double d;
                if (!Number(v, 0, 8000, out d)) return false;
                r.Setters.Add(c => prop.SetValue(c, d));
                return true;
            }
            if (t == typeof(string))
            {
                Func<string, bool> allowed;
                if (!Words.TryGetValue(prop.Name, out allowed) || !allowed(v)) return false;
                if (prop.Name.EndsWith("Key", StringComparison.Ordinal)) keys[prop.Name] = v;
                r.Setters.Add(c => prop.SetValue(c, v));
                return true;
            }
            return false;
        }

        // An element holding one short run of text and nothing else.
        private static bool Leaf(XmlElement el, out string value)
        {
            value = "";
            if (el.HasAttributes || el.ChildNodes.Count > 1) return false;
            if (el.ChildNodes.Count == 1)
            {
                if (el.FirstChild.NodeType != XmlNodeType.Text) return false;
                value = el.FirstChild.Value ?? "";
            }
            return value.Length <= 64;
        }

        private static bool Number(string v, double min, double max, out double d)
        {
            d = 0;
            if (v.Length == 0 || v.Length > 32) return false;
            if (!double.TryParse(v, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out d)) return false;
            return !double.IsNaN(d) && !double.IsInfinity(d) && d >= min && d <= max;
        }

        private static bool ReadPlacement(XmlElement el, PropertyInfo prop, Result r)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var numbers = new Dictionary<string, double>();
            bool set = false;
            foreach (XmlNode node in el.ChildNodes)
            {
                var part = node as XmlElement;
                string v;
                if (part == null || part.NamespaceURI.Length > 0 || !seen.Add(part.Name) || !Leaf(part, out v)) return false;
                if (part.Name == "Set")
                {
                    if (v != "true" && v != "false") return false;
                    set = v == "true";
                    continue;
                }
                double[] range; double d;
                if (!PlaceRange.TryGetValue(part.Name, out range) || !Number(v, range[0], range[1], out d)) return false;
                numbers[part.Name] = d;
            }
            if (seen.Count != PlaceRange.Count + 1) return false;        // all of a placement, or it is not one
            r.Setters.Add(c =>
            {
                // Onto the placement the boxes already hold, so nothing is left pointing at an old one.
                var place = (HudPlacement)prop.GetValue(c);
                if (place == null) { place = new HudPlacement(); prop.SetValue(c, place); }
                place.Set = set;
                foreach (var kv in numbers) typeof(HudPlacement).GetProperty(kv.Key).SetValue(place, kv.Value);
            });
            return true;
        }

        private static bool ReadLinks(XmlElement el, PropertyInfo prop, Result r)
        {
            var links = new List<string>();
            var children = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlNode node in el.ChildNodes)
            {
                var part = node as XmlElement;
                string v;
                if (part == null || part.Name != "Link" || part.NamespaceURI.Length > 0 || !Leaf(part, out v)) return false;
                var m = LinkRx.Match(v);
                // A box joins one other box, never itself.
                if (!m.Success || m.Groups[1].Value == m.Groups[2].Value || !children.Add(m.Groups[1].Value)) return false;
                links.Add(v);
            }
            if (links.Count > 2) return false;                            // three boxes: two joins at most
            r.Setters.Add(c => prop.SetValue(c, new List<string>(links)));
            return true;
        }

        // ── Presets ──────────────────────────────────────────────────────────────────────────

        public static string PresetDir => Path.Combine(PluginConfig.DataDir, "presets");

        private static readonly Regex Reserved = new Regex(@"^(con|prn|aux|nul|com\d|lpt\d)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>A preset's name is also its file's name, so it is letters, digits, spaces, "-" and
        /// "_" only: nothing that could step out of the presets folder.</summary>
        public static bool NameOk(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 32 || name != name.Trim() || Reserved.IsMatch(name)) return false;
            foreach (char c in name)
                if (!char.IsLetterOrDigit(c) && c != ' ' && c != '-' && c != '_') return false;
            return true;
        }

        private static string PresetPath(string name) => Path.Combine(PresetDir, name + Extension);

        public static List<string> Presets()
        {
            var names = new List<string>();
            try
            {
                if (!Directory.Exists(PresetDir)) return names;
                foreach (string file in Directory.GetFiles(PresetDir, "*" + Extension))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (NameOk(name)) names.Add(name);
                }
            }
            catch { }
            names.Sort(StringComparer.CurrentCultureIgnoreCase);
            return names;
        }

        public static bool SavePreset(string name, PluginConfig config)
        {
            if (!NameOk(name)) return false;
            try
            {
                Directory.CreateDirectory(PresetDir);
                if (!File.Exists(PresetPath(name)) && Presets().Count >= MaxPresets) return false;
                return WriteFile(PresetPath(name), config);
            }
            catch { return false; }
        }

        /// <summary>A preset goes through the same reader as a stranger's file: the folder is the
        /// user's, and anything may have been dropped into it.</summary>
        public static Result ReadPreset(string name)
        {
            if (!NameOk(name)) return new Result { Problem = Problem.Read };
            return ReadFile(PresetPath(name));
        }

        public static bool DeletePreset(string name)
        {
            if (!NameOk(name)) return false;
            try { File.Delete(PresetPath(name)); return true; } catch { return false; }
        }
    }
}
