using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using HsbgCardLookup.Config;
using HsbgCardLookup.Host;
using HsbgCardLookup.Localization;

namespace HsbgCardLookup.Ui
{
    /// <summary>A floating box that can be assembled with others (see <see cref="PanelDock"/>).</summary>
    public interface IDockPanel
    {
        string DockId { get; }
        Border DockRoot { get; }
        /// <summary>Put the box's top-left corner there (canvas pixels) and take that as its own
        /// placement; write it to the config when asked.</summary>
        void DockMoveTo(double left, double top, bool save);
        /// <summary>The box's size factor, whatever it means to the box; setting it lays the box out
        /// again and saves it.</summary>
        double DockScale { get; set; }
    }

    public enum DockSide { Top, Bottom, Left, Right }

    /// <summary>
    /// Assembles floating boxes into one: drag one against a side of another and it joins there, after
    /// which the group moves and scales as one. A right-click on a joined box takes the group apart.
    ///
    /// A join is "child sits on that side of parent" (config <c>PanelLinks</c>, "child&gt;parent:Side").
    /// A box with no parent is its group's root and keeps its own placement; every other box is put
    /// beside its parent on each poll, so a box whose content changes size keeps the group tight.
    /// Stacked boxes take the wider one's width; the corners on a seam are squared and the two borders
    /// share one line. UI thread only.
    /// </summary>
    public static partial class PanelDock
    {
        private sealed class Link { public string Parent; public DockSide Side; }

        private const double Reach = 26;          // how near two sides must be to offer a join
        private const double Radius = 7;          // the boxes' own corner radius
        private static readonly Color Spark = Color.FromRgb(0x7F, 0xE3, 0xFF);

        private static readonly List<IDockPanel> Panels = new List<IDockPanel>();
        private static readonly Dictionary<string, Link> Links = new Dictionary<string, Link>();
        private static PluginConfig _config;
        private static bool _widthsStale = true;

        // The join on offer while a box is being dragged.
        private static IDockPanel _offerTo;
        private static DockSide _offerSide;
        private static Rectangle _barA, _barB;
        private static Border _ghost;

        public static void Init(PluginConfig config)
        {
            _config = config;
            Links.Clear();
            foreach (string s in config.PanelLinks ?? new List<string>())
            {
                int gt = s.IndexOf('>'), colon = s.IndexOf(':');
                DockSide side;
                if (gt <= 0 || colon <= gt + 1 || !Enum.TryParse(s.Substring(colon + 1), out side)) continue;
                Links[s.Substring(0, gt)] = new Link { Parent = s.Substring(gt + 1, colon - gt - 1), Side = side };
            }
        }

        private static bool Enabled => _config != null && _config.AssemblePanels;

        public static void Register(IDockPanel panel)
        {
            Panels.RemoveAll(p => p.DockId == panel.DockId);
            Panels.Add(panel);
            panel.DockRoot.MouseMove += (s, e) => Hover(panel);
            panel.DockRoot.MouseRightButtonUp += (s, e) =>
            {
                if (!Enabled || GroupOf(panel).Count < 2) return;
                e.Handled = true;
                HudContextMenu.ShowMenu(new[] { new KeyValuePair<string, Action>(Strings.T("dock.takeApart"), () => TakeApart(panel)) });
            };
        }

        /// <summary>A setting or a size changed: stacked widths are worked out afresh.</summary>
        public static void Invalidate() { _widthsStale = true; }

        private static IDockPanel Find(string id) => Panels.FirstOrDefault(p => p.DockId == id);
        private static bool Shown(IDockPanel p) => p != null && p.DockRoot.Visibility == Visibility.Visible && p.DockRoot.ActualHeight > 0;
        private static bool Placed(IDockPanel p) => p != null && !double.IsNaN(Canvas.GetLeft(p.DockRoot)) && !double.IsNaN(Canvas.GetTop(p.DockRoot));

        private static IDockPanel ParentOf(IDockPanel p)
        {
            Link l;
            return Links.TryGetValue(p.DockId, out l) ? Find(l.Parent) : null;
        }

        /// <summary>The box is put in place by its group, not by its own saved placement.</summary>
        public static bool Holds(IDockPanel p) => Enabled && Placed(ParentOf(p));

        private static IDockPanel RootOf(IDockPanel p)
        {
            for (int guard = 0; guard < 8; guard++)
            {
                var up = Enabled ? ParentOf(p) : null;
                if (up == null) return p;
                p = up;
            }
            return p;
        }

        private static List<IDockPanel> GroupOf(IDockPanel p)
        {
            var root = RootOf(p);
            return Panels.Where(x => RootOf(x) == root).ToList();
        }

        private static Rect RectOf(IDockPanel p)
        {
            var r = p.DockRoot;
            bool shown = Shown(p);
            return new Rect(Canvas.GetLeft(r), Canvas.GetTop(r), shown ? r.ActualWidth : 0, shown ? r.ActualHeight : 0);
        }

        // Where a child goes beside its parent: flush, the two borders on one line.
        private static Point Beside(Rect parent, Size child, DockSide side)
        {
            switch (side)
            {
                case DockSide.Top: return new Point(parent.Left, parent.Top - child.Height + (parent.Height > 0 ? 1 : 0));
                case DockSide.Bottom: return new Point(parent.Left, parent.Bottom - (parent.Height > 0 ? 1 : 0));
                case DockSide.Left: return new Point(parent.Left - child.Width + (parent.Width > 0 ? 1 : 0), parent.Top);
                default: return new Point(parent.Right - (parent.Width > 0 ? 1 : 0), parent.Top);
            }
        }

        /// <summary>About every 100 ms, after the boxes' own polls: children beside their parents,
        /// seams squared, stacked widths matched.</summary>
        public static void Layout()
        {
            try
            {
                if (Panels.Count == 0) return;
                if (!Enabled) { Decorate(false); return; }
                if (_sizing != null) return;                 // being resized as one picture: hands off
                // One pass at natural width, so the next can take the wider of the two; the boxes are
                // still put in place on this one.
                bool natural = _widthsStale;
                if (natural)
                {
                    _widthsStale = false;
                    foreach (var p in Panels) if (p.DockRoot.MinWidth != 0) p.DockRoot.MinWidth = 0;
                }
                for (int pass = 0; pass < 2; pass++)          // a child of a child follows on the second pass
                    foreach (var p in Panels)
                    {
                        Link l;
                        if (!Links.TryGetValue(p.DockId, out l)) continue;
                        var parent = Find(l.Parent);
                        if (!Placed(parent) || !Shown(p)) continue;
                        if (!natural && Shown(parent) && (l.Side == DockSide.Top || l.Side == DockSide.Bottom))
                        {
                            double wide = Math.Max(parent.DockRoot.ActualWidth, p.DockRoot.ActualWidth);
                            if (Math.Abs(parent.DockRoot.MinWidth - wide) > 0.5 && parent.DockRoot.ActualWidth < wide - 0.5) parent.DockRoot.MinWidth = wide;
                            if (Math.Abs(p.DockRoot.MinWidth - wide) > 0.5 && p.DockRoot.ActualWidth < wide - 0.5) p.DockRoot.MinWidth = wide;
                        }
                        var at = Beside(RectOf(parent), new Size(p.DockRoot.ActualWidth, p.DockRoot.ActualHeight), l.Side);
                        if (Math.Abs(Canvas.GetLeft(p.DockRoot) - at.X) > 0.4) Canvas.SetLeft(p.DockRoot, at.X);
                        if (Math.Abs(Canvas.GetTop(p.DockRoot) - at.Y) > 0.4) Canvas.SetTop(p.DockRoot, at.Y);
                    }
                Decorate(true);
            }
            catch { }
        }

        // Square the corners that meet on a seam; everything else keeps the box's own rounding.
        private static void Decorate(bool joined)
        {
            foreach (var p in Panels)
            {
                bool tl = false, tr = false, bl = false, br = false;
                if (joined)
                    foreach (var q in Panels)
                    {
                        Link l;
                        // p as the child of a join, then p as the parent of one: the same seam from both ends.
                        foreach (bool asChild in new[] { true, false })
                        {
                            var child = asChild ? p : q;
                            if (!Links.TryGetValue(child.DockId, out l)) continue;
                            var parent = Find(l.Parent);
                            if (asChild ? parent != q : parent != p) continue;
                            if (!Shown(child) || !Shown(parent)) continue;
                            DockSide mine = asChild ? Opposite(l.Side) : l.Side;      // the side of p on the seam
                            if (mine == DockSide.Top) tl = tr = true;
                            else if (mine == DockSide.Bottom) bl = br = true;
                            else if (mine == DockSide.Left) tl = true;                // tops are level; heights differ
                            else tr = true;
                        }
                    }
                else if (p.DockRoot.MinWidth != 0) p.DockRoot.MinWidth = 0;
                var want = new CornerRadius(tl ? 0 : Radius, tr ? 0 : Radius, br ? 0 : Radius, bl ? 0 : Radius);
                if (p.DockRoot.CornerRadius != want) p.DockRoot.CornerRadius = want;
            }
        }

        private static DockSide Opposite(DockSide s) =>
            s == DockSide.Top ? DockSide.Bottom : s == DockSide.Bottom ? DockSide.Top : s == DockSide.Left ? DockSide.Right : DockSide.Left;

        // ── While a box is dragged ──────────────────────────────────────────────────────────────

        /// <summary>The box has just moved itself under the cursor.</summary>
        public static void Dragging(IDockPanel p)
        {
            try
            {
                if (!Enabled) return;
                var root = RootOf(p);
                if (root != p)
                {
                    // Dragging any box of a group drags the group: the root goes by as much as this box
                    // is off the place its group gives it.
                    Link l = Links[p.DockId];
                    var parent = Find(l.Parent);
                    var want = Beside(RectOf(parent), new Size(p.DockRoot.ActualWidth, p.DockRoot.ActualHeight), l.Side);
                    double dx = Canvas.GetLeft(p.DockRoot) - want.X, dy = Canvas.GetTop(p.DockRoot) - want.Y;
                    root.DockMoveTo(Canvas.GetLeft(root.DockRoot) + dx, Canvas.GetTop(root.DockRoot) + dy, false);
                    Layout();
                    return;
                }
                Layout();                                   // the group's other boxes come along
                Offer(p);
            }
            catch { }
        }

        // Is a side of the dragged box near the facing side of a box outside its group?
        private static void Offer(IDockPanel p)
        {
            IDockPanel best = null; DockSide bestSide = DockSide.Top; double bestGap = Reach;
            var mine = GroupOf(p);
            var a = RectOf(p);
            foreach (var q in Panels)
            {
                if (mine.Contains(q) || !Shown(q)) continue;
                var b = RectOf(q);
                double across = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);      // shared width
                double along = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);       // shared height
                var tries = new[]
                {
                    new KeyValuePair<DockSide, double>(DockSide.Bottom, across > 20 ? Math.Abs(a.Top - b.Bottom) : double.MaxValue),
                    new KeyValuePair<DockSide, double>(DockSide.Top, across > 20 ? Math.Abs(a.Bottom - b.Top) : double.MaxValue),
                    new KeyValuePair<DockSide, double>(DockSide.Right, along > 12 ? Math.Abs(a.Left - b.Right) : double.MaxValue),
                    new KeyValuePair<DockSide, double>(DockSide.Left, along > 12 ? Math.Abs(a.Right - b.Left) : double.MaxValue),
                };
                foreach (var t in tries)
                {
                    if (t.Value >= bestGap) continue;
                    // One box to a side.
                    if (Links.Any(kv => kv.Value.Parent == q.DockId && kv.Value.Side == t.Key && Shown(Find(kv.Key)))) continue;
                    best = q; bestSide = t.Key; bestGap = t.Value;
                }
            }
            if (best == _offerTo && (best == null || bestSide == _offerSide)) { if (best != null) PlaceOffer(p); return; }
            _offerTo = best; _offerSide = bestSide;
            if (best == null) HideOffer(); else PlaceOffer(p);
        }

        // The two sides that will meet light up, and an outline shows the place (and the width) the
        // dragged box is about to take.
        private static void PlaceOffer(IDockPanel p)
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (canvas == null || _offerTo == null) return;
            if (_barA == null)
            {
                _barA = Bar(); _barB = Bar();
                _ghost = new Border
                {
                    BorderBrush = new SolidColorBrush(Spark), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(Radius),
                    Background = new SolidColorBrush(Color.FromArgb(0x22, Spark.R, Spark.G, Spark.B)), IsHitTestVisible = false
                };
                foreach (UIElement el in new UIElement[] { _ghost, _barA, _barB }) { Panel.SetZIndex(el, 9000); canvas.Children.Add(el); }
                var pulse = new DoubleAnimation(0.45, 1, TimeSpan.FromMilliseconds(420)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                _barA.BeginAnimation(UIElement.OpacityProperty, pulse);
                _barB.BeginAnimation(UIElement.OpacityProperty, pulse);
            }
            Rect a = RectOf(p), b = RectOf(_offerTo);
            bool stacked = _offerSide == DockSide.Top || _offerSide == DockSide.Bottom;
            double wide = stacked ? Math.Max(a.Width, b.Width) : a.Width;
            var land = Beside(b, new Size(a.Width, a.Height), _offerSide);
            Put(_ghost, land.X, land.Y, wide, a.Height);
            Edge(_barA, a, Opposite(_offerSide));
            Edge(_barB, b, _offerSide);
            foreach (UIElement el in new UIElement[] { _ghost, _barA, _barB }) el.Visibility = Visibility.Visible;
        }

        private static Rectangle Bar() => new Rectangle
        {
            Fill = new SolidColorBrush(Spark), RadiusX = 2, RadiusY = 2, IsHitTestVisible = false,
            Effect = new DropShadowEffect { Color = Spark, BlurRadius = 14, ShadowDepth = 0, Opacity = 1 }
        };

        private static void Edge(Rectangle bar, Rect r, DockSide side)
        {
            const double t = 3;
            if (side == DockSide.Top) Put(bar, r.Left, r.Top - t / 2, r.Width, t);
            else if (side == DockSide.Bottom) Put(bar, r.Left, r.Bottom - t / 2, r.Width, t);
            else if (side == DockSide.Left) Put(bar, r.Left - t / 2, r.Top, t, r.Height);
            else Put(bar, r.Right - t / 2, r.Top, t, r.Height);
        }

        private static void Put(FrameworkElement el, double x, double y, double w, double h)
        {
            Canvas.SetLeft(el, x); Canvas.SetTop(el, y); el.Width = Math.Max(0, w); el.Height = Math.Max(0, h);
        }

        private static void HideOffer()
        {
            if (_barA == null) return;
            foreach (UIElement el in new UIElement[] { _ghost, _barA, _barB }) el.Visibility = Visibility.Collapsed;
        }

        /// <summary>The drag ended: join on the offered side, or keep the group where it was let go.</summary>
        public static void Dropped(IDockPanel p)
        {
            try
            {
                if (!Enabled) return;
                var to = _offerTo; var side = _offerSide;
                _offerTo = null; HideOffer();
                var root = RootOf(p);
                if (to != null && root == p)
                {
                    var from = RectOf(p);
                    Links[p.DockId] = new Link { Parent = to.DockId, Side = side };
                    Save();
                    _widthsStale = false;
                    Layout();
                    Joined(p, to, side, from);
                    return;
                }
                if (root != p) root.DockMoveTo(Canvas.GetLeft(root.DockRoot), Canvas.GetTop(root.DockRoot), true);
            }
            catch { }
        }

        /// <summary>Test aid: join two boxes as a drop would ("apart" as the side takes the group apart).</summary>
        public static void TestJoin(string child, string parent, string side)
        {
            var c = Find(child); var p = Find(parent);
            if (c == null) return;
            double ratio;
            if (parent == "size" && double.TryParse(side, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ratio))
            {
                // As the group's arrow would: the whole group to that many times its size.
                _sizing = GroupOf(c).Where(Shown).ToList(); _sizeBox = Bounds(_sizing); _sizeRatio = ratio;
                EndSize(true);
                return;
            }
            DockSide s;
            if (p == null || !Enum.TryParse(side, out s)) { TakeApart(c); return; }
            var from = RectOf(c);
            Links[c.DockId] = new Link { Parent = p.DockId, Side = s };
            Save();
            _widthsStale = false;
            Layout();
            Joined(c, p, s, from);
        }

        /// <summary>One box of a group was scaled by <paramref name="ratio"/>: the others follow.</summary>
        public static void Scaled(IDockPanel p, double ratio)
        {
            try
            {
                if (!Enabled || ratio <= 0 || Math.Abs(ratio - 1) < 0.0005) return;
                foreach (var q in GroupOf(p)) if (q != p) q.DockScale = q.DockScale * ratio;
                _widthsStale = true;
            }
            catch { }
        }

        private static void TakeApart(IDockPanel p)
        {
            try
            {
                var group = GroupOf(p);
                var root = RootOf(p);
                foreach (var q in group) Links.Remove(q.DockId);
                Save();
                // Each box keeps the spot it has, a step away from the seam so the parting shows.
                foreach (var q in group)
                {
                    double x = Canvas.GetLeft(q.DockRoot), y = Canvas.GetTop(q.DockRoot);
                    if (q != root)
                    {
                        var r = RectOf(root);
                        x += Math.Sign(x - r.Left) * 10; y += Math.Sign(y - r.Top) * 10;
                    }
                    q.DockRoot.MinWidth = 0;
                    q.DockMoveTo(x, y, true);
                    Nudge(q.DockRoot, 0.94);
                }
                Decorate(false);
            }
            catch { }
        }

        private static void Save()
        {
            if (_config == null) return;
            _config.PanelLinks = Links.Select(kv => kv.Key + ">" + kv.Value.Parent + ":" + kv.Value.Side).ToList();
            try { _config.Save(); } catch { }
        }

        // ── The join, shown ────────────────────────────────────────────────────────────────────
        // The dropped box is pulled the last stretch into place, a line of light runs along the seam
        // from its middle to both ends, and sparks jump off it: the game's Magnetic, in our own drawing.

        private static void Joined(IDockPanel child, IDockPanel parent, DockSide side, Rect from)
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (canvas == null) return;
            Rect now = RectOf(child), b = RectOf(parent);

            var pull = new TranslateTransform(from.Left - now.Left, from.Top - now.Top);
            child.DockRoot.RenderTransform = pull;
            var ease = new BackEase { Amplitude = 0.55, EasingMode = EasingMode.EaseOut };
            var back = TimeSpan.FromMilliseconds(260);
            pull.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, back) { EasingFunction = ease });
            pull.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, back) { EasingFunction = ease });
            Nudge(parent.DockRoot, 1.03);

            bool stacked = side == DockSide.Top || side == DockSide.Bottom;
            double x0, y0, length;
            if (stacked)
            {
                x0 = Math.Min(now.Left, b.Left); length = Math.Max(now.Right, b.Right) - x0;
                y0 = side == DockSide.Bottom ? b.Bottom : b.Top;
            }
            else
            {
                y0 = Math.Max(now.Top, b.Top); length = Math.Min(now.Bottom, b.Bottom) - y0;
                x0 = side == DockSide.Right ? b.Right : b.Left;
            }
            if (length <= 0) return;

            var seam = new Rectangle
            {
                Fill = Brushes.White, IsHitTestVisible = false, RadiusX = 2, RadiusY = 2,
                Effect = new DropShadowEffect { Color = Spark, BlurRadius = 22, ShadowDepth = 0, Opacity = 1 },
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            var grow = new ScaleTransform(stacked ? 0 : 1, stacked ? 1 : 0);
            seam.RenderTransform = grow;
            if (stacked) Put(seam, x0, y0 - 2, length, 4); else Put(seam, x0 - 2, y0, 4, length);
            Panel.SetZIndex(seam, 9001);
            canvas.Children.Add(seam);
            var run = new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { BeginTime = TimeSpan.FromMilliseconds(140), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            grow.BeginAnimation(stacked ? ScaleTransform.ScaleXProperty : ScaleTransform.ScaleYProperty, run);
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = TimeSpan.FromMilliseconds(330) };
            fade.Completed += (s, e) => canvas.Children.Remove(seam);
            seam.BeginAnimation(UIElement.OpacityProperty, fade);

            var rnd = new Random();
            for (int i = 0; i < 9; i++)
            {
                double along = (i + 0.5) / 9 * length, size = 3 + rnd.NextDouble() * 3;
                var spark = new Rectangle
                {
                    Width = size, Height = size, Fill = new SolidColorBrush(i % 3 == 0 ? Colors.White : Spark), IsHitTestVisible = false,
                    RenderTransformOrigin = new Point(0.5, 0.5), Opacity = 0,
                    Effect = new DropShadowEffect { Color = Spark, BlurRadius = 8, ShadowDepth = 0, Opacity = 1 }
                };
                var fly = new TranslateTransform();
                spark.RenderTransform = new TransformGroup { Children = { new RotateTransform(45), fly } };
                Canvas.SetLeft(spark, stacked ? x0 + along : x0 - size / 2);
                Canvas.SetTop(spark, stacked ? y0 - size / 2 : y0 + along);
                Panel.SetZIndex(spark, 9002);
                canvas.Children.Add(spark);
                double off = (rnd.NextDouble() < 0.5 ? -1 : 1) * (10 + rnd.NextDouble() * 22), drift = (rnd.NextDouble() - 0.5) * 18;
                var start = TimeSpan.FromMilliseconds(240 + rnd.Next(120));
                var time = TimeSpan.FromMilliseconds(320 + rnd.Next(160));
                fly.BeginAnimation(stacked ? TranslateTransform.YProperty : TranslateTransform.XProperty,
                    new DoubleAnimation(0, off, time) { BeginTime = start, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                fly.BeginAnimation(stacked ? TranslateTransform.XProperty : TranslateTransform.YProperty, new DoubleAnimation(0, drift, time) { BeginTime = start });
                var blink = new DoubleAnimationUsingKeyFrames { BeginTime = start, Duration = time };
                blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(0)));
                blink.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
                var mine = spark;
                blink.Completed += (s, e) => canvas.Children.Remove(mine);
                spark.BeginAnimation(UIElement.OpacityProperty, blink);
            }
        }

        // A quick swell (or dip) and back, about the box's middle.
        private static void Nudge(FrameworkElement el, double to)
        {
            var scale = new ScaleTransform(1, 1);
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = scale;
            var a = new DoubleAnimation(1, to, TimeSpan.FromMilliseconds(110)) { AutoReverse = true, BeginTime = TimeSpan.FromMilliseconds(180) };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
    }
}
