using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using HsbgCardLookup.Config;
using HsbgCardLookup.Game;
using HsbgCardLookup.Host;
using HsbgCardLookup.Localization;
using HsbgCardLookup.Data;
using HsbgCardLookup.Search;

namespace HsbgCardLookup.Ui
{
    /// <summary>
    /// The session in a box: the rating it began at and the rating now, and its latest matches (hero,
    /// place, rating change), for the mode being played. On screen during a match and on the
    /// Battlegrounds menu. What a session is: <see cref="MatchSession"/>. Lives on the overlay canvas,
    /// moved and scaled like <see cref="TurnCounterPanel"/> (config <c>SessionHud</c>), top left by
    /// default. A right-click offers a new session (<see cref="IDockMenu"/>).
    /// </summary>
    public sealed class SessionPanel : IDockPanel, IDockMenu
    {
        public string DockId => "session";
        // Above or below another box it shares that box's width; beside one it keeps its own height,
        // which goes with how many matches the session has.
        public bool DockStretches(bool stacked) => stacked;
        public double DockLength(bool stacked) => stacked ? _root.ActualWidth : _root.ActualHeight;
        public Border DockRoot => _root;
        public double DockScale
        {
            get { return _scale; }
            set { _scale = Clamp(value, MinScale, MaxScale); _shown = null; if (_root.Visibility == Visibility.Visible) { Rebuild(); Layout(); } Save(); }
        }
        public void DockMoveTo(double left, double top, bool save)
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (canvas == null || canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0) return;
            Canvas.SetLeft(_root, left); Canvas.SetTop(_root, top);
            _xf = left / canvas.ActualWidth; _yf = top / canvas.ActualHeight;
            if (save) Save();
        }

        public IEnumerable<KeyValuePair<string, Action>> DockMenu()
        {
            yield return new KeyValuePair<string, Action>(Strings.T("session.new"), NewSession);
        }

        private const double MinScale = 0.4, MaxScale = 4;
        private const double DefaultXF = 0.005, DefaultYF = 0.008;
        private const double BodyWidth = 222, SummaryWidth = 120;   // at scale 1, with the matches and without: the same whatever the numbers
        private const double NameWidth = 118, ChangeWidth = 40, NameSize = 15;
        private const double CompactWidth = 171, CompactName = 128;   // two-line rows: a portrait and a name wide
        public const int MinGames = 1, MaxGames = 10;

        private readonly PluginConfig _config;
        private readonly CardStore _store;
        private readonly MatchSession _session;
        private readonly Dispatcher _ui;
        private readonly Border _root;
        private readonly StackPanel _body;
        private readonly Border _handle;
        private readonly DispatcherTimer _handleHide;

        private bool _attached;
        private double _xf = DefaultXF, _yf = DefaultYF, _scale;   // _scale 0 = not settled yet
        private bool _dragging, _resizing, _moved;
        private Point _startCursor;
        private double _startLeft, _startTop, _startScale;
        private string _shown;                 // what the box was last built from
        private SessionView _view;
        private DateTime? _matchSince;         // when the match on screen was first seen
        private DateTime _lastReload;

        private static readonly Brush PanelBg = Frozen(Color.FromArgb(0xDC, 0x0A, 0x0D, 0x14));
        private static readonly Color Good = Color.FromRgb(0x5B, 0xD6, 0x6E), Middle = Color.FromRgb(0xF0, 0xC8, 0x4A), Bad = Color.FromRgb(0xE8, 0x5D, 0x5D);
        private static readonly Brush Up = Frozen(Good), Down = Frozen(Bad);
        // The portrait inside our render of a hero card.
        private static readonly Rect HeroArt = new Rect(59 / 404.0, 38 / 419.0, 256 / 404.0, 256 / 419.0);

        public SessionPanel(PluginConfig config, CardStore store, MatchSession session, Dispatcher ui)
        {
            _config = config; _store = store; _session = session; _ui = ui;
            _body = new StackPanel();

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
                Visibility = Visibility.Collapsed, Cursor = Cursors.SizeNESW, Child = arrow
            };
            var grid = new Grid();
            grid.Children.Add(_body);
            grid.Children.Add(_handle);
            _root = new Border
            {
                Background = PanelBg, BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7), Visibility = Visibility.Collapsed,
                Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.75 },
                Child = grid
            };
            _root.SizeChanged += (s, e) => { if (!_dragging && !_resizing) ClampPosition(); };
            PanelDock.Register(this);

            _handleHide = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            _handleHide.Tick += (s, e) => { _handleHide.Stop(); if (!_resizing) _handle.Visibility = Visibility.Collapsed; };
            _handle.MouseLeftButtonDown += (s, e) => { e.Handled = true; BeginGesture(true, e); };
            _root.MouseLeftButtonDown += (s, e) => { e.Handled = true; BeginGesture(false, e); };
            _root.MouseMove += (s, e) =>
            {
                if (PanelDock.Grouped(this)) { _handle.Visibility = Visibility.Collapsed; return; }     // an assembled group has one arrow of its own
                _handle.Visibility = Visibility.Visible; _handleHide.Stop(); _handleHide.Start();
            };
            try { HostServices.Overlay?.SetHitTestVisible(_root); } catch { }

            LoadPlacement();
            _session.Reload();
        }

        private void LoadPlacement()
        {
            _xf = DefaultXF; _yf = DefaultYF; _scale = 0;
            var p = _config.SessionHud;
            if (p != null && p.WF > 0) { _xf = p.XF; _yf = p.YF; _scale = Clamp(p.WF, MinScale, MaxScale); }
        }

        /// <summary>The saved place and size were replaced wholesale (a config import, a preset).</summary>
        public void ReloadPlacement() { LoadPlacement(); _shown = null; if (_root.Visibility == Visibility.Visible) { Rebuild(); Layout(); } }

        public void OnSettingsChanged() { _shown = null; }
        public void OnLanguageChanged() { _shown = null; }
        public void CloseAll() { _ui.Invoke(new Action(() => { Hide(); RemoveHook(); })); }

        /// <summary>The recorder wrote a finished match (or the rating that came after it).</summary>
        public void MatchSaved() { _session.Reload(); }

        private void NewSession()
        {
            _config.SessionResetTicks = DateTime.UtcNow.Ticks;
            try { _config.Save(); } catch { }
            _shown = null;
        }

        /// <summary>About every 100 ms, on the UI thread.</summary>
        public void Poll()
        {
            try
            {
                var game = HostServices.Game; var memory = HostServices.Memory;
                bool sample = OpponentBoardPanel.TestBoard;          // the sample board's test aid shows a sample session too
                bool inMatch = game != null && game.IsBattlegroundsMatch;
                bool onMenu = !inMatch && memory != null && memory.OnBattlegroundsMenu;
                if (!inMatch) _matchSince = null; else if (_matchSince == null) _matchSince = DateTime.UtcNow;
                if (!_config.ShowSessionPanel || ArrangeSession.IsActive || !(inMatch || onMenu || sample)) { Hide(); return; }

                // Between matches the files are looked at again now and then: a match recorded by another
                // copy of the toolkit, or files put back by hand. A match of our own says so itself.
                var now = DateTime.UtcNow;
                if (onMenu && (now - _lastReload).TotalSeconds > 60) { _lastReload = now; _session.Reload(); }

                bool duos = inMatch ? game.IsBattlegroundsDuosMatch : _session.LastWasDuos;
                int? client = null;
                try { client = memory?.OwnRating(duos); } catch { }
                var view = sample ? Sample() : _session.View(duos, _matchSince ?? now, new DateTime(Math.Max(0, _config.SessionResetTicks), DateTimeKind.Utc), client);
                if (view.Current == null && view.Games.Count == 0) { Hide(); return; }
                Show(view, duos);
            }
            catch (Exception ex)
            {
                if (_failed) return;
                _failed = true;             // once: this runs ten times a second
                try { PanelDock.Log?.Invoke("[session] " + ex); } catch { }
            }
        }
        private bool _failed;

        private static SessionView Sample()
        {
            var v = new SessionView { Start = 6480, Current = 6612 };
            string[] heroes = { "TB_BaconShop_HERO_56", "BG20_HERO_202", "TB_BaconShop_HERO_08", "BG21_HERO_000", "TB_BaconShop_HERO_34" };
            int[] places = { 1, 5, 3, 8, 2 }, changes = { 98, -24, 41, -77, 94 };
            int rating = 6612;
            for (int i = 0; i < heroes.Length; i++)
            {
                v.Games.Add(new SessionGame { Hero = heroes[i], Place = places[i], Rating = rating, Change = changes[i] });
                rating -= changes[i];
            }
            return v;
        }

        private void Hide()
        {
            if (_root.Visibility != Visibility.Collapsed) _root.Visibility = Visibility.Collapsed;
        }

        private void Show(SessionView view, bool duos)
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (canvas == null) return;
            if (!_attached)
            {
                canvas.Children.Add(_root);
                canvas.SizeChanged += (s, e) => { if (_root.Visibility == Visibility.Visible) Layout(); };
                _attached = true;
            }
            if (_scale <= 0 && canvas.ActualHeight > 0)
            {
                // Two-line rows start a quarter larger: about the width of the strip left of the leaderboard.
                _scale = Clamp((_config.CompactRows ? 1.23 : 1) * canvas.ActualHeight / 1080.0, MinScale, MaxScale);
                Save();
            }
            int rows = Math.Max(MinGames, Math.Min(MaxGames, _config.SessionGames));
            string sig = string.Join("|", new[] { duos ? "d" : "s", _config.SessionShowGames ? "g" : "", _config.CompactRows ? "2" : "1", view.Start?.ToString(), view.Current?.ToString(), Strings.Web, _scale.ToString("0.###", CultureInfo.InvariantCulture) }
                .Concat(view.Games.Take(rows).Select(g => g.Hero + ":" + g.Place + ":" + g.Change)));
            _view = view;
            bool rebuilt = sig != _shown;
            if (rebuilt) { _shown = sig; Rebuild(); }
            if (_root.Visibility != Visibility.Visible) { _root.Visibility = Visibility.Visible; Layout(); }
            if (rebuilt) PanelDock.Invalidate();
        }

        // ── The box's content ───────────────────────────────────────────────────────────────────

        private void Rebuild()
        {
            var view = _view;
            if (view == null || _scale <= 0) return;
            double s = _scale;
            _root.Padding = new Thickness(9 * s, 6 * s, 9 * s, 7 * s);
            _handle.Margin = new Thickness(0, 2 - 6 * s, 2 - 9 * s, 0);     // in the box's own corner, whatever the padding
            _body.MinWidth = (!_config.SessionShowGames ? SummaryWidth : _config.CompactRows ? CompactWidth : BodyWidth) * s;
            _body.Children.Clear();

            int? net = view.Games.Count > 0 && view.Start.HasValue && view.Current.HasValue ? view.Current - view.Start : null;
            // The title gives way to the session's net change beside it: a long one (some languages) is cut.
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(new TextBlock
            {
                Text = Strings.T("session.title").ToUpper(CultureInfo.CurrentCulture), Foreground = UiKit.TextMuted, FontSize = 10.5 * s, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (net.HasValue)
            {
                var total = ChangeText(net.Value, 13 * s, HorizontalAlignment.Right);
                total.Margin = new Thickness(6 * s, 0, 0, 0);
                Grid.SetColumn(total, 1);
                head.Children.Add(total);
            }
            _body.Children.Add(head);

            if (view.Current.HasValue)
            {
                string from = view.Start.HasValue && view.Games.Count > 0 ? view.Start.Value.ToString(CultureInfo.InvariantCulture) + " → " : "";
                _body.Children.Add(new TextBlock
                {
                    Text = from + view.Current.Value.ToString(CultureInfo.InvariantCulture), Foreground = UiKit.TextPrimary,
                    FontSize = 17 * s, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 1 * s, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }

            if (!_config.SessionShowGames || view.Games.Count == 0) return;
            int rows = Math.Max(MinGames, Math.Min(MaxGames, _config.SessionGames));
            int most = view.Games.Any(g => g.Duos) ? 4 : 8;
            // A table under the summary: a line above it and between its rows, and one down it that
            // sets the rating changes apart. The change column holds a sign and three digits whatever
            // the numbers are ("+300" is common as a season starts), so the rows line up.
            _body.Children.Add(Line(0.9, new Thickness(0, 6 * s, 0, 0)));
            bool first = true;
            foreach (var g in view.Games.Take(rows))
            {
                if (!first) _body.Children.Add(Line(0.4, new Thickness(0)));
                first = false;
                // A hero skin is its own card ("TB_BaconShop_HERO_90_SKIN_G"); we show the hero it dresses.
                // A hero our card data doesn't hold (gone from the game since) has no picture and no name.
                BgCard hero = null;
                try { hero = g.Hero == null ? null : _store?.Lookup(Regex.Replace(g.Hero, "_SKIN_[A-Za-z0-9]+$", "")) ?? _store?.Lookup(g.Hero); } catch { }
                if (_config.CompactRows) { _body.Children.Add(CompactRow(g, hero, most, s)); continue; }
                var row = new Grid { Height = 33 * s };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ChangeWidth * s) });
                row.Children.Add(Portrait(hero, 29 * s));
                var name = new TextBlock
                {
                    Text = hero?.DisplayName ?? "", Foreground = UiKit.TextSecondary, FontSize = NameSize * s, TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = NameWidth * s, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(6 * s, 0, 4 * s, 0), VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(name, 1);
                row.Children.Add(name);
                var place = new TextBlock
                {
                    Text = g.Place.ToString(CultureInfo.InvariantCulture), FontSize = 16 * s, FontWeight = FontWeights.Bold, Foreground = Frozen(PlaceColor(g.Place, most)),
                    Width = 18 * s, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(place, 2);
                row.Children.Add(place);
                var divider = new Rectangle { Width = 1, Fill = UiKit.StrokeBrush, Opacity = 0.9, Margin = new Thickness(5 * s, 0, 0, 0), SnapsToDevicePixels = true };
                Grid.SetColumn(divider, 3);
                row.Children.Add(divider);
                if (g.Change.HasValue)
                {
                    var change = ChangeText(g.Change.Value, 14 * s, HorizontalAlignment.Right);
                    Grid.SetColumn(change, 4);
                    row.Children.Add(change);
                }
                _body.Children.Add(row);
            }
        }

        // The two-line form: the portrait beside both lines, the name on the first, the place and the
        // rating change at the two ends of the second.
        private FrameworkElement CompactRow(SessionGame g, BgCard hero, int most, double s)
        {
            var row = new Grid { Margin = new Thickness(0, 3 * s, 0, 3 * s) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var portrait = Portrait(hero, 36 * s);
            Grid.SetRowSpan(portrait, 2);
            row.Children.Add(portrait);
            var name = new TextBlock
            {
                Text = hero?.DisplayName ?? "", Foreground = UiKit.TextSecondary, FontSize = NameSize * s, TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = CompactName * s, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(7 * s, 0, 0, 0)
            };
            Grid.SetColumn(name, 1); Grid.SetColumnSpan(name, 2);
            row.Children.Add(name);
            var place = new TextBlock
            {
                Text = g.Place.ToString(CultureInfo.InvariantCulture), FontSize = 16 * s, FontWeight = FontWeights.Bold, Foreground = Frozen(PlaceColor(g.Place, most)),
                Margin = new Thickness(7 * s, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetColumn(place, 1); Grid.SetRow(place, 1);
            row.Children.Add(place);
            if (g.Change.HasValue)
            {
                var change = ChangeText(g.Change.Value, 15 * s, HorizontalAlignment.Right);
                Grid.SetColumn(change, 2); Grid.SetRow(change, 1);
                row.Children.Add(change);
            }
            return row;
        }

        private static Rectangle Line(double opacity, Thickness margin) => new Rectangle
        {
            Height = 1, Fill = UiKit.StrokeBrush, Opacity = opacity, Margin = margin, SnapsToDevicePixels = true
        };

        private static TextBlock ChangeText(int change, double size, HorizontalAlignment align) => new TextBlock
        {
            Text = change > 0 ? "+" + change.ToString(CultureInfo.InvariantCulture) : change < 0 ? "−" + (-change).ToString(CultureInfo.InvariantCulture) : "0",
            Foreground = change > 0 ? Up : change < 0 ? Down : UiKit.TextSecondary, FontSize = size, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center
        };

        /// <summary>Green for 1st through yellow for the last place that gains rating (4th of 8, 2nd of 4) to red for last.</summary>
        public static Color PlaceColor(int place, int most)
        {
            int half = most / 2;
            place = Math.Max(1, Math.Min(most, place));
            return place <= half ? Mix(Good, Middle, half > 1 ? (place - 1) / (double)(half - 1) : 1) : Mix(Middle, Bad, (place - half) / (double)(most - half));
        }

        private static Color Mix(Color a, Color b, double t) =>
            Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        private Ellipse Portrait(BgCard card, double size)
        {
            var e = new Ellipse
            {
                Width = size, Height = size, Fill = UiKit.Br(Color.FromRgb(0x23, 0x2B, 0x3B)), Stroke = UiKit.StrokeBrush, StrokeThickness = 1,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (card == null) return e;
            Action<BitmapSource> set = bmp => e.Fill = new ImageBrush(bmp) { Viewbox = HeroArt, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Stretch = Stretch.UniformToFill };
            try
            {
                var hit = CardArt.GetSync(card, false, 0);
                if (hit != null) set(hit);
                else CardArt.LoadAsync(card, false, 0).ContinueWith(t =>
                {
                    if (t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && t.Result != null)
                        _ui.BeginInvoke(new Action(() => set(t.Result)));
                });
            }
            catch { }
            return e;
        }

        private void Layout()
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (canvas == null || canvas.ActualHeight <= 0) return;
            ClampPosition();
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
                // Drag up or right to scale up; the box keeps its top-left corner.
                double before = _scale;
                _scale = Clamp(_startScale * (1 + (dx - dy) / 160.0), MinScale, MaxScale);
                _moved = true;
                Rebuild();
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
            _shown = null;                       // built at the size it ended on
            var canvas = HostServices.Overlay?.Canvas;
            if (!persist || !_moved || canvas == null || canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0) return;
            if (dragged)
            {
                PanelDock.Dropped(this);
                _xf = Canvas.GetLeft(_root) / canvas.ActualWidth;
                _yf = Canvas.GetTop(_root) / canvas.ActualHeight;
            }
            Save();
        }

        private void Save()
        {
            try
            {
                var p = _config.SessionHud ?? (_config.SessionHud = new HudPlacement());
                p.Set = true; p.XF = _xf; p.YF = _yf; p.WF = _scale;
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
