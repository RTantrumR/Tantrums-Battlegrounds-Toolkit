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
        /// <summary>Whether the box is sized to fit a neighbour: for a stacked join (above / below) its
        /// width, for a side join its height. Both boxes of a join must agree, or each keeps the size
        /// it has. Never for the turn counter (one short line that neither stretches nor scales well);
        /// the lobby types box by its layout. A box that does not fit still moves and scales with its group.</summary>
        bool DockStretches(bool stacked);
        /// <summary>What a fit measures of the box at its own size: its width for a stacked join, its
        /// height for a side join. Not always the size on screen: the sidebar answers with the height
        /// of a full lobby however many rows have come in.</summary>
        double DockLength(bool stacked);
    }

    /// <summary>A box with entries of its own for the right-click menu; they come first, before
    /// "take apart", on a right-click anywhere on the box's group.</summary>
    public interface IDockMenu
    {
        IEnumerable<KeyValuePair<string, Action>> DockMenu();
    }

    public enum DockSide { Top, Bottom, Left, Right }

    /// <summary>
    /// Assembles floating boxes into one: drag one against a side of another and it joins there, after
    /// which the group moves and scales as one. A right-click on a joined box takes the group apart.
    ///
    /// A join is "child sits on that side of parent" (config <c>PanelLinks</c>, "child&gt;parent:Side").
    /// A box with no parent is its group's root and keeps its own placement; every other box is put
    /// beside its parent on each poll, so a box whose content changes size keeps the group tight.
    /// Stacked boxes take the wider one's width and boxes side by side the taller one's height, where
    /// both agree to (<see cref="IDockPanel.DockStretches"/>); the corners on a seam are squared and the
    /// two borders share one line. UI thread only.
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
        private static IDockPanel _offerFrom;     // the box of the dragged group that would meet it
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
            // Our boxes lie over the game's own things and over what we pin to them (the icons by the
            // portraits), whatever order they were first shown in.
            Panel.SetZIndex(panel.DockRoot, 500);
            panel.DockRoot.MouseMove += (s, e) => Hover(panel);
            panel.DockRoot.MouseRightButtonUp += (s, e) =>
            {
                var group = GroupOf(panel);
                var items = new List<KeyValuePair<string, Action>>();
                foreach (var q in group) if (Shown(q) && q is IDockMenu) items.AddRange(((IDockMenu)q).DockMenu());
                if (Enabled && group.Count >= 2) items.Add(new KeyValuePair<string, Action>(Strings.T("dock.takeApart"), () => TakeApart(panel)));
                if (items.Count == 0) return;
                e.Handled = true;
                HudContextMenu.ShowMenu(items);
            };
        }

        /// <summary>A setting or a size changed: stacked widths are worked out afresh, and joined boxes
        /// that no longer fit each other are sized to fit again.</summary>
        public static void Invalidate() { _widthsStale = true; _fitRounds = 3; }

        // Fitting is a standing rule, not only something a drop does: a join saved earlier can meet
        // boxes whose sizes have since gone their own ways (a setting that changes a box's content, an
        // imported layout, a box that was sized while its neighbour was not on screen). So whenever
        // the widths are worked out afresh, or another set of boxes is on screen, two joined boxes
        // more than 3% apart get one sized to the other (see _fitAnchor for which). At most three
        // rounds each time: a box's width does not follow its size exactly. All of it in one poll, so
        // no box is ever drawn at a size on the way to its own.
        private static int _fitRounds = 3;
        private static string _shownSet = "";

        // Which box a fit sizes the other one to. The box whose content changed keeps its size and its
        // partner follows it, up OR down: turning the sidebar's MMR on widens it and the types box above
        // grows to match; turning it off shrinks the types box back. A box "changed" when its natural
        // size moved while its scale did not. With no such change (start, an imported layout) the
        // parent leads. Sizing only the smaller box up used to ratchet: a box that was briefly narrow
        // grew, and nothing ever brought it back.
        private static readonly Dictionary<string, double[]> _natural = new Dictionary<string, double[]>();   // id → scale, width, height
        private static string _fitAnchor;

        private static bool ContentChanged(IDockPanel p, bool stacked)
        {
            double[] last;
            if (!_natural.TryGetValue(p.DockId, out last)) return false;
            if (Math.Abs(last[0] - p.DockScale) > 1e-6) return false;            // resized, not changed
            double was = stacked ? last[1] : last[2], now = p.DockLength(stacked);
            return was > 0 && Math.Abs(now - was) / was > 0.01;
        }

        private static void RememberNatural(IDockPanel p) =>
            _natural[p.DockId] = new[] { p.DockScale, p.DockLength(true), p.DockLength(false) };

        // A join's shared dimension: the width of two stacked boxes, the height of two side by side.
        private static bool Stacked(DockSide s) => s == DockSide.Top || s == DockSide.Bottom;
        // Two joined boxes that agree to share a width (stacked) or a height are brought to it by
        // stretching the smaller one (its text unchanged) and, under "Scale" only, by sizing it up first.
        private static bool Fits(IDockPanel a, IDockPanel b, bool stacked) =>
            _config != null && _config.AssembleFit != "None" && a.DockStretches(stacked) && b.DockStretches(stacked);
        private static bool Scales => _config != null && _config.AssembleFit == "Scale";
        private static double Across(FrameworkElement r, bool stacked) => stacked ? r.ActualWidth : r.ActualHeight;
        private static double MinAcross(FrameworkElement r, bool stacked) => stacked ? r.MinWidth : r.MinHeight;
        private static void SetMinAcross(FrameworkElement r, bool stacked, double v) { if (stacked) r.MinWidth = v; else r.MinHeight = v; }
        private static void Unstretch(FrameworkElement r) { if (r.MinWidth != 0) r.MinWidth = 0; if (r.MinHeight != 0) r.MinHeight = 0; }

        private static IDockPanel Find(string id) => Panels.FirstOrDefault(p => p.DockId == id);
        private static bool Shown(IDockPanel p) => p != null && p.DockRoot.Visibility == Visibility.Visible && p.DockRoot.ActualHeight > 0;
        private static bool Placed(IDockPanel p) => p != null && !double.IsNaN(Canvas.GetLeft(p.DockRoot)) && !double.IsNaN(Canvas.GetTop(p.DockRoot));

        // The one way the dock moves a box. A box that has not been on screen in this run (the others of
        // its group can be: the session box shows on the Battlegrounds menu, the rest only in a match)
        // has no place on the canvas yet, and arithmetic on that gives NaN: such a move is dropped, or
        // the NaN would be saved as the box's place and the box could never be dragged again.
        private static void MoveTo(IDockPanel p, double left, double top, bool save)
        {
            if (p == null || double.IsNaN(left) || double.IsNaN(top) || double.IsInfinity(left) || double.IsInfinity(top)) return;
            p.DockMoveTo(left, top, save);
        }

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
                if (_sizing != null)
                {
                    // Being resized as one picture: hands off. Unless the button's release never reached
                    // us (the game lost focus mid-drag): then the resize ends here.
                    if ((GetAsyncKeyState(0x01) & 0x8000) != 0) return;
                    EndSize(true);
                }
                var canvas = HostServices.Overlay?.Canvas;
                if (canvas == null) return;
                canvas.UpdateLayout();                        // a box shown or rebuilt a moment ago has its size now
                string shownSet = string.Join(",", Panels.Where(Shown).Select(p => p.DockId));
                if (shownSet != _shownSet) { _shownSet = shownSet; Invalidate(); }
                if (_widthsStale)
                {
                    _widthsStale = false;
                    foreach (var p in Panels) Unstretch(p.DockRoot);
                    canvas.UpdateLayout();
                    while (Fit())
                    {
                        canvas.UpdateLayout();                // measured again at the new size
                        if (--_fitRounds <= 0) break;
                    }
                }
                for (int pass = 0; pass < 2; pass++)          // a child of a child follows on the second pass
                {
                    if (EvenOut()) canvas.UpdateLayout();
                    foreach (var p in Panels)
                    {
                        Link l;
                        if (!Links.TryGetValue(p.DockId, out l)) continue;
                        var parent = Find(l.Parent);
                        if (!Placed(parent) || !Shown(p)) continue;
                        var at = Beside(RectOf(parent), new Size(p.DockRoot.ActualWidth, p.DockRoot.ActualHeight), l.Side);
                        // "Not within 0.4 of it", so that a box never placed before (NaN) is placed too: the
                        // plain "further than 0.4" is false for NaN, and such a box sat at the screen's corner.
                        if (!(Math.Abs(Canvas.GetLeft(p.DockRoot) - at.X) <= 0.4)) Canvas.SetLeft(p.DockRoot, at.X);
                        if (!(Math.Abs(Canvas.GetTop(p.DockRoot) - at.Y) <= 0.4)) Canvas.SetTop(p.DockRoot, at.Y);
                    }
                }
                KeepInside();
                Decorate(true);
            }
            catch (Exception ex)
            {
                if (_failed) return;
                _failed = true;             // once: this runs ten times a second
                try { Log?.Invoke("[dock] layout: " + ex); } catch { }
            }
        }
        private static bool _failed;
        public static Action<string> Log;

        // The joins on screen whose two boxes are sized to each other: child, parent, and whether they
        // share a width (stacked) or a height.
        private static void Pairs(Action<IDockPanel, IDockPanel, bool> each)
        {
            foreach (var p in Panels)
            {
                Link l;
                if (!Links.TryGetValue(p.DockId, out l)) continue;
                var parent = Find(l.Parent);
                if (Placed(parent) && Shown(p) && Shown(parent) && Fits(p, parent, Stacked(l.Side))) each(p, parent, Stacked(l.Side));
            }
        }

        // One round, with every box at its own size (nothing stretched): where a pair is clearly apart
        // one box of it is sized. False = every pair fits.
        private static bool Fit()
        {
            if (!Scales) return false;
            bool sized = false;
            Pairs((p, parent, stacked) =>
            {
                if (sized) return;
                double mine = p.DockLength(stacked), its = parent.DockLength(stacked);
                double most = Math.Max(mine, its), least = Math.Min(mine, its);
                bool mineChanged = ContentChanged(p, stacked), itsChanged = ContentChanged(parent, stacked);
                if (mineChanged != itsChanged) _fitAnchor = mineChanged ? p.DockId : parent.DockId;
                else if (least > 0 && most / least <= 1.03) _fitAnchor = null;   // fitted: the change that led is done
                RememberNatural(p); RememberNatural(parent);
                if (least <= 0 || most / least <= 1.03) return;
                var lead = _fitAnchor == p.DockId ? p : parent;
                var follow = lead == p ? parent : p;
                double from = follow.DockLength(stacked), to = lead.DockLength(stacked);
                double was = follow.DockScale, want = was * to / from;
                follow.DockScale = want;
                // A box at the end of its size range can't follow: the other one is sized to it next round.
                if (Math.Abs(follow.DockScale - want) > want * 0.01) _fitAnchor = follow.DockId;
                try
                {
                    Log?.Invoke(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "[dock] fit {0} to {1} ({2}): {3:0} vs {4:0} px, scale {5:0.###} -> {6:0.###}, round {7}",
                        follow.DockId, lead.DockId, stacked ? "width" : "height", from, to, was, follow.DockScale, 4 - _fitRounds));
                }
                catch { }
                _natural.Remove(follow.DockId);   // measured afresh at its new scale, never read as a content change
                sized = true;
            });
            return sized;
        }

        // What little is left between the two boxes of a pair is evened out by stretching the smaller.
        private static bool EvenOut()
        {
            bool any = false;
            Pairs((p, parent, stacked) =>
            {
                double most = Math.Max(Across(p.DockRoot, stacked), Across(parent.DockRoot, stacked));
                foreach (var r in new[] { parent.DockRoot, p.DockRoot })
                    if (Math.Abs(MinAcross(r, stacked) - most) > 0.5 && Across(r, stacked) < most - 0.5) { SetMinAcross(r, stacked, most); any = true; }
            });
            return any;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

        // A box keeps itself on screen, but a group is moved by its root alone: without this, dragging
        // a group by a lower box could push the box above it off the top of the screen for good.
        private static void KeepInside()
        {
            var canvas = HostServices.Overlay?.Canvas;
            // Not against a window that is not really there (minimised, or still coming up).
            if (canvas == null || canvas.ActualWidth < 600 || canvas.ActualHeight < 400) return;
            foreach (var root in Panels)
            {
                if (RootOf(root) != root) continue;
                var group = GroupOf(root);
                if (group.Count < 2) continue;
                Rect b = Rect.Empty;
                foreach (var p in group) if (Shown(p)) b.Union(RectOf(p));
                if (b.IsEmpty) continue;
                double dx = b.Left < 0 ? -b.Left : b.Right > canvas.ActualWidth ? Math.Max(-b.Left, canvas.ActualWidth - b.Right) : 0;
                double dy = b.Top < 0 ? -b.Top : b.Bottom > canvas.ActualHeight ? Math.Max(-b.Top, canvas.ActualHeight - b.Bottom) : 0;
                if (Math.Abs(dx) + Math.Abs(dy) < 0.5) continue;
                foreach (var p in group)
                {
                    if (p == root || !Placed(p)) continue;
                    Canvas.SetLeft(p.DockRoot, Canvas.GetLeft(p.DockRoot) + dx);
                    Canvas.SetTop(p.DockRoot, Canvas.GetTop(p.DockRoot) + dy);
                }
                MoveTo(root, Canvas.GetLeft(root.DockRoot) + dx, Canvas.GetTop(root.DockRoot) + dy, _hook == IntPtr.Zero);
            }
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
                            // Boxes meet left edge to left edge (stacked) or top to top (side by side), so
                            // that corner of the seam is always shared; the far one only when this box is
                            // not the longer of the two along the seam.
                            var other = asChild ? parent : child;
                            bool across = p.DockRoot.ActualWidth <= other.DockRoot.ActualWidth + 1.5;
                            bool down = p.DockRoot.ActualHeight <= other.DockRoot.ActualHeight + 1.5;
                            if (mine == DockSide.Top) { tl = true; tr |= across; }
                            else if (mine == DockSide.Bottom) { bl = true; br |= across; }
                            else if (mine == DockSide.Left) { tl = true; bl |= down; }
                            else { tr = true; br |= down; }
                        }
                    }
                else Unstretch(p.DockRoot);
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
                    // Its group is not on the canvas yet: the box moves alone, by its own placement.
                    if (!Placed(parent) || !Placed(root)) return;
                    var want = Beside(RectOf(parent), new Size(p.DockRoot.ActualWidth, p.DockRoot.ActualHeight), l.Side);
                    double dx = Canvas.GetLeft(p.DockRoot) - want.X, dy = Canvas.GetTop(p.DockRoot) - want.Y;
                    MoveTo(root, Canvas.GetLeft(root.DockRoot) + dx, Canvas.GetTop(root.DockRoot) + dy, false);
                }
                Layout();                                   // the group's other boxes come along
                Offer(p);
            }
            catch { }
        }

        // Is a side of any box of the dragged group near the facing side of a box outside it? The group
        // is one piece: whichever of its boxes is held, the one that reaches the other box joins it.
        private static void Offer(IDockPanel p)
        {
            IDockPanel best = null, bestFrom = null; DockSide bestSide = DockSide.Top; double bestGap = Reach;
            var mine = GroupOf(p);
            foreach (var from in mine)
            foreach (var q in Panels)
            {
                if (mine.Contains(q) || !Shown(q) || !Shown(from)) continue;
                var a = RectOf(from);
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
                    // One box to a side, on both boxes that would meet.
                    if (Taken(q, t.Key) || Taken(from, Opposite(t.Key))) continue;
                    best = q; bestFrom = from; bestSide = t.Key; bestGap = t.Value;
                }
            }
            if (best == _offerTo && bestFrom == _offerFrom && (best == null || bestSide == _offerSide)) { if (best != null) PlaceOffer(bestFrom); return; }
            _offerTo = best; _offerFrom = bestFrom; _offerSide = bestSide;
            if (best == null) HideOffer(); else PlaceOffer(bestFrom);
        }

        // A box already has a neighbour on that side: a child joined there, or its own parent (a box
        // that sits below its parent has the parent on its top side).
        private static bool Taken(IDockPanel box, DockSide side)
        {
            if (Links.Any(kv => kv.Value.Parent == box.DockId && kv.Value.Side == side && Shown(Find(kv.Key)))) return true;
            Link l;
            return Links.TryGetValue(box.DockId, out l) && Opposite(l.Side) == side && Shown(Find(l.Parent));
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
            bool stacked = Stacked(_offerSide), fits = Fits(p, _offerTo, stacked);
            var land = Beside(b, new Size(a.Width, a.Height), _offerSide);
            Put(_ghost, land.X, land.Y, fits && stacked ? Math.Max(a.Width, b.Width) : a.Width, fits && !stacked ? Math.Max(a.Height, b.Height) : a.Height);
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
                var to = _offerTo; var side = _offerSide; var joiner = _offerFrom;
                _offerTo = null; _offerFrom = null; HideOffer();
                var root = RootOf(p);
                if (to != null && joiner != null)
                {
                    var from = RectOf(joiner);
                    Reroot(joiner);
                    Join(joiner, to, side);
                    Joined(joiner, to, side, from);
                    return;
                }
                if (root != p) MoveTo(root, Canvas.GetLeft(root.DockRoot), Canvas.GetTop(root.DockRoot), true);
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
            Join(c, p, s);
            Joined(c, p, s, from);
        }

        // A box has one parent, so the box of a group that joins another group must be its group's
        // root: the joins on the way up from it are turned around (child and parent change places, the
        // side becomes its opposite). Nothing moves: the same boxes meet on the same sides.
        private static void Reroot(IDockPanel p)
        {
            var up = new List<KeyValuePair<string, Link>>();
            string id = p.DockId;
            Link l;
            for (int guard = 0; guard < 8 && Links.TryGetValue(id, out l); guard++) { up.Add(new KeyValuePair<string, Link>(id, l)); id = l.Parent; }
            foreach (var kv in up) Links.Remove(kv.Key);
            foreach (var kv in up) Links[kv.Value.Parent] = new Link { Parent = kv.Key, Side = Opposite(kv.Value.Side) };
        }

        // Link the two, and where both fit each other bring the smaller side up to the larger one's
        // width (stacked) or height (side by side) by SIZE (its text and icons grow with it), so the
        // join reads as one panel; what little is left over (a box's width does not follow its size
        // exactly) is evened out by centring.
        private static void Join(IDockPanel child, IDockPanel parent, DockSide side)
        {
            var childSide = GroupOf(child); var parentSide = GroupOf(parent);
            Links[child.DockId] = new Link { Parent = parent.DockId, Side = side };
            Save();
            var canvas = HostServices.Overlay?.Canvas;
            bool stacked = Stacked(side);
            if (canvas != null && Shown(child) && Shown(parent)
                && !parentSide.Contains(child)           // two groups meeting, not a group re-linked within itself
                && Scales && Fits(child, parent, stacked))
            {
                foreach (var q in childSide.Concat(parentSide)) Unstretch(q.DockRoot);
                for (int pass = 0; pass < 3; pass++)
                {
                    canvas.UpdateLayout();
                    double wc = child.DockLength(stacked), wp = parent.DockLength(stacked);
                    if (wc <= 0 || wp <= 0) break;
                    double ratio = wc < wp ? wp / wc : wc / wp;
                    if (ratio < 1.01) break;
                    foreach (var q in wc < wp ? childSide : parentSide) q.DockScale = q.DockScale * ratio;
                }
                canvas.UpdateLayout();
            }
            _widthsStale = false;
            Layout();
        }

        /// <summary>One box of a group was scaled by <paramref name="ratio"/>: the others follow, and one
        /// that is not there yet (the sidebar before its first players) is fitted to this box when it comes,
        /// instead of undoing the size just given by hand.</summary>
        public static void Scaled(IDockPanel p, double ratio)
        {
            try
            {
                if (!Enabled || ratio <= 0 || Math.Abs(ratio - 1) < 0.0005) return;
                foreach (var q in GroupOf(p)) if (q != p) q.DockScale = q.DockScale * ratio;
                _fitAnchor = p.DockId;
                Invalidate();
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
                    if (!Placed(q)) continue;               // never on screen in this run: its saved place stands
                    double x = Canvas.GetLeft(q.DockRoot), y = Canvas.GetTop(q.DockRoot);
                    if (q != root && Placed(root))
                    {
                        var r = RectOf(root);
                        x += Math.Sign(x - r.Left) * 10; y += Math.Sign(y - r.Top) * 10;
                    }
                    Unstretch(q.DockRoot);
                    MoveTo(q, x, y, true);
                    Nudge(q.DockRoot, 0.94);
                }
                Decorate(false);
            }
            catch (Exception ex) { try { Log?.Invoke("[dock] take apart: " + ex); } catch { } }
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
            Tip(canvas, Bounds(GroupOf(child)));

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

        // How to undo it, said once by the new panel: under it (above when there is no room), gone in a
        // few seconds. One line at a time: a second join replaces the first one's.
        private static Border _tip;

        private static void Tip(Canvas canvas, Rect group)
        {
            if (group.IsEmpty) return;
            if (_tip != null) canvas.Children.Remove(_tip);
            var tip = _tip = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x0A, 0x0D, 0x14)),
                BorderBrush = new SolidColorBrush(Spark), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(9, 4, 9, 5), IsHitTestVisible = false, Opacity = 0,
                Child = new TextBlock { Text = Strings.T("dock.tip"), Foreground = UiKit.TextPrimary, FontSize = 12.5 }
            };
            Panel.SetZIndex(tip, 9003);
            canvas.Children.Add(tip);
            tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = tip.DesiredSize.Width, h = tip.DesiredSize.Height;
            double x = Math.Max(4, Math.Min(group.Left + (group.Width - w) / 2, canvas.ActualWidth - w - 4));
            double y = group.Bottom + 8 + h <= canvas.ActualHeight ? group.Bottom + 8 : group.Top - 8 - h;
            Canvas.SetLeft(tip, x); Canvas.SetTop(tip, y);
            var show = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(5.5) };
            show.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5))));
            show.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.8))));
            show.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.9))));
            show.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(5.5))));
            show.Completed += (s, e) => { canvas.Children.Remove(tip); if (_tip == tip) _tip = null; };
            tip.BeginAnimation(UIElement.OpacityProperty, show);
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
