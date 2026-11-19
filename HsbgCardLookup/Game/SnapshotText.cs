using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HsbgCardLookup.Host;

namespace HsbgCardLookup.Game
{
    /// <summary>
    /// A card's text as the game would write it for one snapshot of a minion: the client's own text
    /// template (<see cref="IClientMemory.CardText"/>) filled with the numbers the minion carried.
    ///
    /// Follows the client's text builders (decompiled, not copied): SCRIPT_DATA_NUM_1 puts the first
    /// script number in place of every '@'; SCRIPT_DATA_NUM_1_NUM_2 and MULTIPLE_ALT_TEXT_SCRIPT_DATA_NUMS
    /// fill {0}..{5} with script numbers 1..6, and the latter then picks one of the '@'-separated
    /// variants by USE_ALTERNATE_CARD_TEXT. Other builders are left alone.
    /// </summary>
    public static class SnapshotText
    {
        private const int BuilderNum1 = 7, BuilderNum1Num2 = 26, BuilderMultiAlt = 28;

        /// <summary>The text with its &lt;b&gt;/&lt;i&gt; tags and line breaks, or null when this card's text
        /// carries nothing of the snapshot (or its builder is one we don't follow).</summary>
        public static string Build(CardTextRecord record, BoardMinion m, string language)
        {
            if (record == null || m == null) return null;
            string t = record.In(language);
            if (string.IsNullOrEmpty(t)) return null;
            int tilde = t.IndexOf('~');                       // the "assembled" variant follows a '~'
            if (tilde >= 0) t = t.Substring(0, tilde);
            try
            {
                switch (record.Builder)
                {
                    case BuilderNum1:
                        if (t.IndexOf('@') < 0) return null;
                        t = t.Replace("@", m.Script[0].ToString(CultureInfo.InvariantCulture));
                        break;
                    case BuilderNum1Num2:
                        if (t.IndexOf("{0", StringComparison.Ordinal) < 0 && t.IndexOf("{1", StringComparison.Ordinal) < 0) return null;
                        t = string.Format(CultureInfo.InvariantCulture, t, m.Script[0], m.Script[1]);
                        break;
                    case BuilderMultiAlt:
                    {
                        bool numbers = t.IndexOf('{') >= 0, variants = t.IndexOf('@') >= 0;
                        if (!numbers && (!variants || m.AltText == 0)) return null;
                        if (numbers) t = string.Format(CultureInfo.InvariantCulture, t, m.Script[0], m.Script[1], m.Script[2], m.Script[3], m.Script[4], m.Script[5]);
                        var parts = t.Split('@');
                        t = parts[Math.Max(0, Math.Min(m.AltText, parts.Length - 1))];
                        break;
                    }
                    default: return null;
                }
            }
            catch (FormatException) { return null; }

            if (t.StartsWith("[x]", StringComparison.Ordinal)) t = t.Substring(3);
            t = Plural.Replace(t, p => p.Groups[1].Value + p.Groups[2].Value + (p.Groups[1].Value == "1" ? p.Groups[3].Value : p.Groups[4].Value).Trim());
            t = Particle.Replace(t, p => p.Groups[1].Value + (EndsClosed(p.Groups[1].Value[0]) ? p.Groups[2].Value : p.Groups[3].Value).Trim());
            t = Bonus.Replace(t, "$1");                       // "$3" / "#3": spell damage and healing markers
            return t.Replace('_', ' ');
        }

        // "{0} |4(turn, turns)": the word by the number before it.
        private static readonly Regex Plural = new Regex(@"(\d+)([^|\d]*)\|4\(([^,)]*),([^)]*)\)", RegexOptions.Compiled);
        // Korean "|1(을,를)" and its kin: the first form after a syllable that ends in a consonant, the
        // second after an open one. A digit counts as its Korean reading does (영 일 삼 육 칠 팔 end closed).
        private static readonly Regex Particle = new Regex(@"(\S)\|[0-35-9]\(([^,)]*),([^)]*)\)", RegexOptions.Compiled);
        private static bool EndsClosed(char c)
        {
            if (c >= 0xAC00 && c <= 0xD7A3) return (c - 0xAC00) % 28 != 0;
            return "013678".IndexOf(c) >= 0;
        }
        private static readonly Regex Bonus = new Regex(@"[$#](\d)", RegexOptions.Compiled);
        private static readonly Regex Tag = new Regex(@"</?[bi]>|\[[xbd]\]", RegexOptions.Compiled);

        /// <summary>Letters, digits and signs only: two texts that read the same compare equal.</summary>
        public static string Plain(string text)
        {
            if (text == null) return "";
            var sb = new StringBuilder();
            foreach (char c in Tag.Replace(text, "")) if (!char.IsWhiteSpace(c) && c != ' ' && c != '_') sb.Append(c);
            return sb.ToString();
        }
    }
}
