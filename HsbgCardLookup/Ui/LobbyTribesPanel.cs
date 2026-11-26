using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using HsbgCardLookup.Config;
using HsbgCardLookup.Data;
using HsbgCardLookup.Host;
using HsbgCardLookup.Localization;

namespace HsbgCardLookup.Ui
{
    /// <summary>
    /// This lobby's minion types for the whole match: one small box of round type icons with the
    /// type's name under each, in one row, one column, or two or three rows (config
    /// <c>LobbyTribesLayout</c>); icons alone or names alone by <c>LobbyTribesStyle</c>, and with
    /// <c>ShowBannedTribes</c> the season's other types crossed out in a second block. Lives on the overlay canvas; moved by dragging it and scaled by its
    /// top-right grabber, the way <see cref="MmrSidePanel"/> is (same LL-hook gesture, same
    /// canvas-fraction placement, config <c>LobbyTribesHud</c>).
    /// </summary>
    public sealed class LobbyTribesPanel : IDockPanel
    {
        public string DockId => "tribes";
        // A box that is wider than tall (one row, or three and two) fits the width of what it is
        // stacked on; a tall one (one column, or two-two-one) fits the height of what stands beside it.
        // The other way round it joins at the size it has.
        public bool DockStretches(bool stacked) => stacked == (PerRow(_config.LobbyTribesLayout) >= 3);
        public double DockLength(bool stacked) => stacked ? _root.ActualWidth : _root.ActualHeight;
        public Border DockRoot => _root;
        public double DockScale
        {
            get { return _scale; }
            set { _scale = Clamp(value, MinScale, MaxScale); if (_root.Visibility == Visibility.Visible) Layout(); Save(false); }
        }
        public void DockMoveTo(double left, double top, bool save)
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (canvas == null || canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0) return;
            Canvas.SetLeft(_root, left); Canvas.SetTop(_root, top);
            _xf = left / canvas.ActualWidth; _yf = top / canvas.ActualHeight;
            if (save) Save(true);
        }

        private const double RefCellW = 46;       // one type's column at scale 1: the box is always this
                                                  // wide per type, a longer name is set smaller instead
        private const double LabelSize = 13, MinLabelSize = 6;    // a name is this large unless its length asks for less
        private const double MinScale = 0.4, MaxScale = 4.8;   // of the settled (pixel) scale
        private const double DefaultXF = 0.004, DefaultYF = 0.098;   // under the opponent's name

        /// <summary>English tribe name → the label to draw (the card language's vocabulary).</summary>
        public static Func<string, string> TribeLabel;
        /// <summary>The season's minion types (English names): what is not in the lobby is banned.</summary>
        public static Func<IEnumerable<string>> Pool;

        private readonly PluginConfig _config;
        private readonly Dispatcher _ui;
        private readonly Border _root;
        private readonly Border _handle;
        private readonly DispatcherTimer _handleHide;
        private sealed class Cell
        {
            public FrameworkElement Root; public Ellipse Icon; public Path Cross; public TextBlock Label;
            public bool Marked, TextOnly;
        }
        // What the box holds, apart from the box: built and sized the same for the live panel and for
        // Settings' picture of it.
        private sealed class Face
        {
            public readonly StackPanel Rows = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            public readonly List<Cell> Cells = new List<Cell>();
            public readonly List<FrameworkElement> Lines = new List<FrameworkElement>();   // every line but a block's first
            public Border Divider;
        }
        private readonly Face _face = new Face();
        private List<string> _banned;     // null = not worked out for this lobby yet

        private bool _attached;
        // _scale is the box's own scale in pixels terms once settled: at the first showing it takes the
        // game window's height into account (x height/1080), and is kept from then on (config Px).
        private double _xf = DefaultXF, _yf = DefaultYF, _scale = 1;
        private bool _settled;
        private bool _dragging, _resizing, _moved;
        private Point _startCursor;
        private double _startLeft, _startTop, _startScale;

        // Match state: the types are read once a second from the game's start until the list is there.
        private volatile bool _active;
        private List<string> _tribes = new List<string>();
        private DateTime _nextRead = DateTime.MinValue;
        private string _shownKey;
        private static LobbyTribesPanel _current;
        private static bool _hooked;

        private static readonly Brush PanelBg = Frozen(Color.FromArgb(0xDC, 0x0A, 0x0D, 0x14));
        private static readonly Brush InRing = Frozen(Color.FromRgb(0x4C, 0xC8, 0x5A));
        private static readonly Brush OutRing = Frozen(Color.FromRgb(0xB8, 0x3A, 0x3A));
        private static readonly Brush CrossBrush = Frozen(Color.FromRgb(0xE5, 0x2B, 0x2B));

        public LobbyTribesPanel(PluginConfig config, Dispatcher ui)
        {
            _config = config; _ui = ui;

            var arrow = new Path
            {
                Data = Geometry.Parse("M3 9 L9 3 M9 3 L9 6 M9 3 L6 3"),
                Stroke = Brushes.White, StrokeThickness = 1.6, Stretch = Stretch.Uniform, Margin = new Thickness(4)
            };
            _handle = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x00, 0x00, 0x00)),
                BorderBrush = UiKit.AccentBrush, BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 2, 0), Visibility = Visibility.Collapsed,
                Cursor = Cursors.SizeNESW, Child = arrow
            };
            var grid = new Grid();
            grid.Children.Add(_face.Rows);
            grid.Children.Add(_handle);
            _root = new Border
            {
                Background = PanelBg, BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7), Visibility = Visibility.Collapsed,
                Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.75 },
                Child = grid
            };
            _root.SizeChanged += (s, e) => { if (!_dragging && !_resizing) ClampPosition(); };

            _handleHide = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            _handleHide.Tick += (s, e) => { _handleHide.Stop(); if (!_resizing) _handle.Visibility = Visibility.Collapsed; };
            _handle.MouseLeftButtonDown += (s, e) => { e.Handled = true; BeginGesture(true, e); };
            _root.MouseLeftButtonDown += (s, e) => { e.Handled = true; BeginGesture(false, e); };
            _root.MouseMove += (s, e) =>
            {
                if (PanelDock.Grouped(this)) { _handle.Visibility = Visibility.Collapsed; return; }     // an assembled group has one arrow of its own
                _handle.Visibility = Visibility.Visible; _handleHide.Stop(); _handleHide.Start();
            };
            // A right-click on the box by itself offers its layouts; in an assembled group the same
            // click is the group's ("take the panels apart", PanelDock).
            _root.MouseRightButtonUp += (s, e) =>
            {
                if (PanelDock.Grouped(this)) return;
                e.Handled = true;
                LayoutMenu();
            };
            try { HostServices.Overlay?.SetHitTestVisible(_root); } catch { }

            LoadPlacement();

            PanelDock.Register(this);
            _current = this;
            _active = true;          // started mid-match: the first read decides
            if (!_hooked)
            {
                _hooked = true;
                try { HostServices.Game.GameStarted += () => _current?.OnGame(true); } catch { }
                try { HostServices.Game.GameEnded += () => _current?.OnGame(false); } catch { }
            }
        }

        private void OnGame(bool started)
        {
            _active = started;
            _tribes = new List<string>();
            _banned = null;
            _nextRead = DateTime.MinValue;
        }

        /// <summary>About every 100 ms, on the UI thread.</summary>
        public void Poll()
        {
            try
            {
                if (!_config.ShowLobbyTribes || ArrangeSession.IsActive || !_active) { Hide(); return; }
                if (_tribes.Count < 5 && DateTime.UtcNow >= _nextRead)
                {
                    _nextRead = DateTime.UtcNow.AddSeconds(1);
                    var races = HostServices.Memory?.LobbyTribes();
                    var names = new List<string>();
                    if (races != null)
                        foreach (int r in races) { string n = TribeTally.RaceName(r); if (n != null) names.Add(n); }
                    if (names.Count > 0) { _tribes = names; _banned = null; }
                }
                if (_tribes.Count == 0) { Hide(); return; }
                Show();
            }
            catch { }
        }

        public void OnSettingsChanged() { _shownKey = null; _banned = null; }

        private void LoadPlacement()
        {
            _xf = DefaultXF; _yf = DefaultYF; _scale = 1; _settled = false;
            var p = _config.LobbyTribesHud;
            if (p != null && p.WF > 0) { _xf = p.XF; _yf = p.YF; _scale = p.WF; }
            if (p != null && p.Px > 0) { _scale = Clamp(p.Px, MinScale, MaxScale); _settled = true; }
        }

        /// <summary>The saved place and size were replaced wholesale (a config import, a preset).</summary>
        public void ReloadPlacement() { LoadPlacement(); _shownKey = null; if (_root.Visibility == Visibility.Visible) Layout(); }
        public void OnLanguageChanged() { _shownKey = null; _banned = null; }
        public void CloseAll() { _ui.Invoke(new Action(() => { Hide(); RemoveHook(); })); }

        private void Hide()
        {
            if (_root.Visibility != Visibility.Collapsed) _root.Visibility = Visibility.Collapsed;
        }

        private void Show()
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (canvas == null) return;
            if (!_attached)
            {
                canvas.Children.Add(_root);
                canvas.SizeChanged += (s, e) => { if (_root.Visibility == Visibility.Visible) Layout(); };
                _attached = true;
            }
            int perRow = PerRow(_config.LobbyTribesLayout);
            string style = Style(_config.LobbyTribesStyle);
            var banned = Banned();
            string key = string.Join(",", _tribes) + "|" + perRow + "|" + style + "|" + string.Join(",", banned);
            if (key != _shownKey)
            {
                _shownKey = key;
                Build(_face, ByLabel(_tribes), banned, perRow, style);
                Layout();
            }
            if (_root.Visibility != Visibility.Visible) { _root.Visibility = Visibility.Visible; Layout(); }
        }

        private static string Style(string style) => style == "Icons" || style == "Text" ? style : "IconsText";

        private static string LabelOf(string tribe)
        {
            try { return TribeLabel?.Invoke(tribe) ?? tribe; } catch { return tribe; }
        }

        private static List<string> ByLabel(IEnumerable<string> tribes)
        {
            var list = new List<string>(tribes);
            list.Sort((x, y) => string.Compare(LabelOf(x), LabelOf(y), StringComparison.CurrentCultureIgnoreCase));
            return list;
        }

        // The season's types that this lobby does not have; none while the setting is off, the lobby
        // is not read in full yet, or the card data names no pool.
        private List<string> Banned()
        {
            var banned = new List<string>();
            if (!_config.ShowBannedTribes || _tribes.Count < 5) return banned;
            // Asked on every poll: the pool is worked out once per lobby, language and settings change.
            if (_banned != null) return _banned;
            try
            {
                var pool = Pool?.Invoke();
                if (pool != null)
                    foreach (string t in pool)
                        if (!_tribes.Contains(t) && !banned.Contains(t) && TribeIcons.BrushFor(t) != null) banned.Add(t);
            }
            catch { }
            return _banned = ByLabel(banned);
        }

        private static void Build(Face f, List<string> present, List<string> banned, int perRow, string style)
        {
            f.Rows.Children.Clear(); f.Cells.Clear(); f.Lines.Clear(); f.Divider = null;
            // One column: the banned types stand beside it as a second column. Otherwise below.
            f.Rows.Orientation = perRow == 1 ? Orientation.Horizontal : Orientation.Vertical;
            bool marked = banned.Count > 0;
            f.Rows.Children.Add(Block(f, present, perRow, style, false, marked));
            if (!marked) return;
            f.Rows.Children.Add(f.Divider = new Border { Background = UiKit.StrokeBrush });
            // In one row the banned types take one row too, however many they are.
            f.Rows.Children.Add(Block(f, banned, perRow == 5 ? banned.Count : perRow, style, true, true));
        }

        /// <summary>Settings' picture of the box: five sample types drawn by the current layout, look
        /// and banned-types settings. <paramref name="refresh"/> redraws it after a change.</summary>
        public static FrameworkElement Preview(PluginConfig config, out Action refresh)
        {
            var face = new Face();
            var box = new Border
            {
                Background = PanelBg, BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7), Padding = new Thickness(5, 5, 5, 4), Child = face.Rows,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            Action paint = () =>
            {
                try
                {
                    var sample = new List<string> { "Beast", "Dragon", "Mech", "Murloc", "Undead" };
                    var banned = new List<string>();
                    if (config.ShowBannedTribes)
                    {
                        IEnumerable<string> pool = null;
                        try { pool = Pool?.Invoke(); } catch { }
                        foreach (string t in pool ?? new[] { "Aberration", "Demon", "Elemental", "Pirate", "Quilboar" })
                            if (!sample.Contains(t) && !banned.Contains(t) && TribeIcons.BrushFor(t) != null) banned.Add(t);
                    }
                    Build(face, ByLabel(sample), ByLabel(banned), PerRow(config.LobbyTribesLayout), Style(config.LobbyTribesStyle));
                    Size(face, 1);
                    box.Opacity = config.ShowLobbyTribes ? 1 : 0.4;
                }
                catch { }
            };
            paint();
            refresh = paint;
            return box;
        }

        private static FrameworkElement Block(Face f, List<string> tribes, int perRow, string style, bool banned, bool marked)
        {
            var block = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
            StackPanel line = null;
            foreach (string tribe in tribes)
            {
                if (line == null || line.Children.Count >= perRow)
                {
                    bool first = line == null;
                    block.Children.Add(line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center });
                    if (!first) f.Lines.Add(line);
                }
                var c = new Cell { Marked = marked, TextOnly = style == "Text" };
                var cell = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                if (style != "Text")
                {
                    c.Icon = new Ellipse
                    {
                        Fill = TribeIcons.BrushFor(tribe), Stroke = !marked ? TribeIcons.Ring : (banned ? OutRing : InRing),
                        HorizontalAlignment = HorizontalAlignment.Center, Opacity = banned ? 0.5 : 1
                    };
                    var art = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
                    art.Children.Add(c.Icon);
                    if (banned)
                    {
                        c.Cross = new Path
                        {
                            Data = Geometry.Parse("M0 0 L10 10 M10 0 L0 10"), Stroke = CrossBrush, Stretch = Stretch.Uniform,
                            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                        };
                        art.Children.Add(c.Cross);
                    }
                    cell.Children.Add(art);
                }
                if (style != "Icons")
                {
                    c.Label = new TextBlock
                    {
                        Text = LabelOf(tribe), FontWeight = FontWeights.SemiBold,
                        Foreground = c.TextOnly ? TribeIcons.TextBrush(tribe) : UiKit.TextPrimary,
                        TextAlignment = TextAlignment.Center, Opacity = banned ? 0.55 : 1,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    };
                    if (c.TextOnly && banned) c.Label.TextDecorations = TextDecorations.Strikethrough;
                    if (!c.TextOnly) c.Label.TextTrimming = TextTrimming.CharacterEllipsis;
                    cell.Children.Add(new Grid { Children = { c.Label } });   // a row of one height, the name centred in it
                }
                c.Root = cell;
                line.Children.Add(cell);
                f.Cells.Add(c);
            }
            return block;
        }

        // The four layouts as a menu under the cursor, the one in use ticked (rows x columns, written
        // the same in every language, as in Settings); under them what a type is drawn as, and the
        // banned types.
        private void LayoutMenu()
        {
            string now = PerRow(_config.LobbyTribesLayout) == 5 ? "1x5" : _config.LobbyTribesLayout;
            string style = Style(_config.LobbyTribesStyle);
            var items = new List<KeyValuePair<string, Action>>();
            Action<bool, string, Action> add = (ticked, text, pick) =>
                items.Add(new KeyValuePair<string, Action>((ticked ? "✓  " : "     ") + text, () =>
                {
                    pick();
                    try { _config.Save(); } catch { }
                    _shownKey = null;
                    PanelDock.Invalidate();
                }));
            foreach (string layout in new[] { "1x5", "5x1", "2x3", "3x2" })
            {
                string pick = layout;
                add(pick == now, pick.Replace("x", " × "), () => _config.LobbyTribesLayout = pick);
            }
            add(style == "IconsText", Strings.T("settings.lobbyTribes.styleIconsText"), () => _config.LobbyTribesStyle = "IconsText");
            add(style == "Icons", Strings.T("settings.lobbyTribes.styleIcons"), () => _config.LobbyTribesStyle = "Icons");
            add(style == "Text", Strings.T("settings.lobbyTribes.styleText"), () => _config.LobbyTribesStyle = "Text");
            add(_config.ShowBannedTribes, Strings.T("settings.lobbyTribes.banned"), () => _config.ShowBannedTribes = !_config.ShowBannedTribes);
            HudContextMenu.ShowMenu(items, 0);
        }

        /// <summary>Test aid: the layout menu, as a right-click on the box opens it.</summary>
        public static void TestMenu() { _current?.LayoutMenu(); }

        // Types to a row: rows x columns, the last row holding what is left of the five.
        private static int PerRow(string layout)
        {
            switch (layout)
            {
                case "5x1": return 1;
                case "2x3": return 3;
                case "3x2": return 2;
                default: return 5;
            }
        }

        private void Layout()
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (canvas == null || canvas.ActualHeight <= 0) return;
            if (!_settled)
            {
                _scale = Clamp(_scale * Math.Max(0.6, Math.Min(canvas.ActualHeight / 1080.0, 2.0)), MinScale, MaxScale);
                _settled = true;
                Save(false);
            }
            double s = _scale;
            _root.Padding = new Thickness(5 * s, 5 * s, 5 * s, 4 * s);
            // The arrow sits in the box's own top-right corner whatever the padding has grown to.
            _handle.Margin = new Thickness(0, 2 - 5 * s, 2 - 5 * s, 0);
            Size(_face, s);
            ClampPosition();
        }

        private static void Size(Face f, double s)
        {
            foreach (var line in f.Lines) line.Margin = new Thickness(0, 3 * s, 0, 0);
            if (f.Divider != null)
            {
                bool beside = f.Rows.Orientation == Orientation.Horizontal;
                double thick = Math.Max(1, s);
                f.Divider.Width = beside ? thick : double.NaN;
                f.Divider.Height = beside ? double.NaN : thick;
                f.Divider.Margin = beside ? new Thickness(5 * s, 0, 5 * s, 0) : new Thickness(0, 4 * s, 0, 4 * s);
            }
            foreach (var c in f.Cells)
            {
                if (c.Icon != null)
                {
                    c.Icon.Width = c.Icon.Height = 40 * s;
                    c.Icon.StrokeThickness = (c.Marked ? 2.0 : 1.4) * s;
                    if (c.Cross != null) { c.Cross.Width = c.Cross.Height = 24 * s; c.Cross.StrokeThickness = 3.4 * s; }
                    // The icon takes the width its cell always had (40 of a bare icon's, 46 under a name):
                    // larger icons in a box no wider (owner, 2026-11-26).
                    c.Root.Margin = new Thickness(0);
                }
                var label = c.Label;
                if (label == null) continue;
                if (c.TextOnly)
                {
                    label.FontSize = 12.5 * s;
                    c.Root.Margin = new Thickness(6 * s, 1 * s, 6 * s, 1 * s);
                    continue;
                }
                // A name too long for its column is set smaller until it fits, never cut.
                double room = (RefCellW - 3) * s;
                label.Width = double.NaN;
                label.FontSize = LabelSize * s;
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double wide = label.DesiredSize.Width;
                if (wide > room) label.FontSize = Math.Max(MinLabelSize * s, LabelSize * s * room / wide);
                label.Width = RefCellW * s;
                var row = (Grid)label.Parent;
                row.Height = 18 * s;
                row.Margin = new Thickness(0, 1 * s, 0, 0);
            }
        }

        private void ClampPosition()
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (canvas == null) return;
            double cw = canvas.ActualWidth, ch = canvas.ActualHeight;
            if (cw <= 0 || ch <= 0 || PanelDock.Holds(this)) return;
            Canvas.SetLeft(_root, Clamp(_xf * cw, 0, Math.Max(0, cw - _root.ActualWidth)));
            Canvas.SetTop(_root, Clamp(_yf * ch, 0, Math.Max(0, ch - _root.ActualHeight)));
        }

        // ── Move / resize gestures (LL mouse hook, installed only for the gesture) ───────────────

        private void BeginGesture(bool resize, MouseButtonEventArgs e)
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (!_attached || _dragging || _resizing || canvas == null) return;
            try { _startCursor = e.GetPosition(canvas); } catch { return; }
            _startLeft = Canvas.GetLeft(_root); _startTop = Canvas.GetTop(_root); _startScale = _scale;
            if (double.IsNaN(_startLeft) || double.IsNaN(_startTop)) return;
            _moved = false; _dragging = !resize; _resizing = resize;
            InstallHook();
        }

        private void OnGestureMove()
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (canvas == null) { EndGesture(false); return; }
            double cw = canvas.ActualWidth, ch = canvas.ActualHeight;
            if (cw <= 0 || ch <= 0) return;
            Point cur;
            try { GetCursorPos(out POINT p); cur = canvas.PointFromScreen(new Point(p.X, p.Y)); }
            catch { return; }
            double dx = cur.X - _startCursor.X, dy = cur.Y - _startCursor.Y;
            if (Math.Abs(dx) + Math.Abs(dy) > 1) _moved = true;
            if (_dragging)
            {
                Canvas.SetLeft(_root, Clamp(_startLeft + dx, 0, Math.Max(0, cw - _root.ActualWidth)));
                Canvas.SetTop(_root, Clamp(_startTop + dy, 0, Math.Max(0, ch - _root.ActualHeight)));
                PanelDock.Dragging(this);
            }
            else if (_resizing)
            {
                // Drag right to scale up, by the width the box had when the drag began.
                double w0 = Math.Max(40, _root.ActualWidth / (_scale / _startScale));
                double before = _scale;
                _scale = Clamp(_startScale * (w0 + dx) / w0, MinScale, MaxScale);
                Layout();
                PanelDock.Scaled(this, _scale / before);
            }
        }

        private void EndGesture(bool persist = true)
        {
            if (!_dragging && !_resizing) return;
            bool dragged = _dragging;
            _dragging = _resizing = false;
            RemoveHook();
            var canvas = HostServices.Overlay?.Canvas;
            if (!persist || !_moved || canvas == null || canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0) return;
            if (dragged) PanelDock.Dropped(this);
            _xf = Canvas.GetLeft(_root) / canvas.ActualWidth;
            _yf = Canvas.GetTop(_root) / canvas.ActualHeight;
            Save(true);
        }

        private void Save(bool placed)
        {
            try
            {
                var p = _config.LobbyTribesHud ?? (_config.LobbyTribesHud = new HudPlacement());
                if (placed) { p.Set = true; p.XF = _xf; p.YF = _yf; p.WF = _scale; }
                p.Px = _scale;
                _config.Save();
            }
            catch { }
        }

        private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

        // ── Gesture hook plumbing (canvas thread; never swallows) ────────────────────────────────

        private IntPtr _hook = IntPtr.Zero;
        private LowLevelMouseProc _proc;

        private void InstallHook()
        {
            if (_hook != IntPtr.Zero) return;
            try { _proc = HookProc; _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0); }
            catch { _hook = IntPtr.Zero; _proc = null; _dragging = _resizing = false; }
        }

        private void RemoveHook()
        {
            if (_hook == IntPtr.Zero) return;
            try { UnhookWindowsHookEx(_hook); } catch { }
            _hook = IntPtr.Zero; _proc = null;
        }

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                if (wParam == (IntPtr)WM_MOUSEMOVE) { try { OnGestureMove(); } catch { } }
                else if (wParam == (IntPtr)WM_LBUTTONUP) { try { EndGesture(); } catch { } }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private const int WH_MOUSE_LL = 14, WM_MOUSEMOVE = 0x0200, WM_LBUTTONUP = 0x0202;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
