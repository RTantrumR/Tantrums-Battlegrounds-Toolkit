using System.Collections.Generic;
using System.Windows.Media;
using HsbgCardLookup.Data;

namespace HsbgCardLookup.Ui
{
    /// <summary>The one place that turns a <see cref="TribeTally"/> result into an icon, so the
    /// portrait labels and the side panel can't drift. Every state maps to a bundled tribe art:
    /// a real tribe → its own; <see cref="TribeTally.Mixed"/> → "All"; <see cref="TribeTally.None"/>
    /// (no typed minions) → "Neutral" (Brann Bronzebeard — the same art the F3 filters use for
    /// tribeless minions); null (no data) → nothing. The arts are painted as CIRCLES (an
    /// <c>Ellipse</c> filled with an <see cref="ImageBrush"/>): they're drawn for round display, the
    /// way HDT's own tribe row shows them, so a square crop wastes the corners.</summary>
    internal static class TribeIcons
    {
        private static readonly Dictionary<string, ImageBrush> Cache = new Dictionary<string, ImageBrush>();

        /// <summary>Ring drawn around every type circle so it reads on any background.</summary>
        public static readonly Brush Ring = Frozen(Color.FromArgb(0xE0, 0x14, 0x18, 0x22));

        /// <summary>Frozen, cached fill for a type circle. UI thread. Null tribe → null.</summary>
        public static ImageBrush BrushFor(string tribe)
        {
            if (string.IsNullOrEmpty(tribe)) return null;
            string art = tribe == TribeTally.None ? "Neutral" : tribe;
            if (Cache.TryGetValue(art, out var cached)) return cached;
            ImageBrush brush = null;
            try
            {
                var src = ImageCache.Load(CardStore.TribeIconPath(art), 64);
                if (src != null)
                {
                    brush = new ImageBrush(src) { Stretch = Stretch.UniformToFill };
                    brush.Freeze();
                }
            }
            catch { brush = null; }
            Cache[art] = brush;   // a miss is remembered too, so it isn't retried every poll
            return brush;
        }

        // A colour of its own per type, for where the name stands in for the icon.
        private static readonly Dictionary<string, Brush> Text = new Dictionary<string, Brush>
        {
            { "Beast", Frozen(Color.FromRgb(0x8F, 0xD1, 0x4F)) }, { "Demon", Frozen(Color.FromRgb(0xB6, 0x6D, 0xFF)) },
            { "Dragon", Frozen(Color.FromRgb(0xFF, 0x7A, 0x45)) }, { "Elemental", Frozen(Color.FromRgb(0x4F, 0xC3, 0xF7)) },
            { "Mech", Frozen(Color.FromRgb(0xB0, 0xBE, 0xC5)) }, { "Murloc", Frozen(Color.FromRgb(0x3D, 0xDB, 0xB4)) },
            { "Naga", Frozen(Color.FromRgb(0x5C, 0x8D, 0xFF)) }, { "Pirate", Frozen(Color.FromRgb(0xFF, 0xC9, 0x4D)) },
            { "Quilboar", Frozen(Color.FromRgb(0xFF, 0x6F, 0xA3)) }, { "Undead", Frozen(Color.FromRgb(0x9F, 0xA8, 0xDA)) },
            { "Aberration", Frozen(Color.FromRgb(0xE2, 0x6D, 0xD6)) },
        };

        /// <summary>The type's own colour for its written name.</summary>
        public static Brush TextBrush(string tribe) =>
            tribe != null && Text.TryGetValue(tribe, out var b) ? b : UiKit.TextPrimary;

        private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    }
}
