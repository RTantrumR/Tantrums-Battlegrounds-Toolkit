using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Line = System.Windows.Shapes.Line;
using Polyline = System.Windows.Shapes.Polyline;
using Polygon = System.Windows.Shapes.Polygon;
using Ellipse = System.Windows.Shapes.Ellipse;
using HsbgCardLookup.Data;
using HsbgCardLookup.Game;
using HsbgCardLookup.Localization;
using HsbgCardLookup.Search;
using Newtonsoft.Json;

namespace HsbgCardLookup.Ui
{
    /// <summary>
    /// The player's own match history, from the recorder's files (<see cref="MatchRecorder"/>). Pages:
    /// Overview (totals, rating line, places, the five most played heroes, the five latest matches),
    /// Heroes, Matches, and one match (final board, lobby, health, every round's board).
    /// An ordinary window, in the taskbar and not kept on top, so a stream can capture it by itself
    /// (it is kept out of the overlay's window mirrors for the same reason). Opened from the tray menu.
    /// Design: docs/branches/match-stats (the mockup it was ported from).
    /// </summary>
    public sealed partial class StatsWindow : Window
    {
        private const double BodyW = 1056, Pad = 22, CardPad = 16, InnerW = BodyW - 2 * CardPad - 2;
        private static readonly Color WindowBg = Color.FromRgb(0x12, 0x16, 0x1E), CardBg = Color.FromRgb(0x17, 0x1C, 0x27),
            StrokeSoft = Color.FromRgb(0x26, 0x30, 0x42), Good = Color.FromRgb(0x6F, 0xCF, 0x7C), Bad = Color.FromRgb(0xE0, 0x6C, 0x5E),
            Tie = Color.FromRgb(0x9A, 0xA6, 0xB8), OnAccent = Color.FromRgb(0x12, 0x16, 0x1E), PopBg = Color.FromRgb(0x0D, 0x11, 0x18);
        // Places 1 to 8: green, through yellow, to red.
        private static readonly Color[] PlaceColors =
        {
            Color.FromRgb(0x58, 0xC8, 0x78), Color.FromRgb(0x86, 0xCF, 0x6C), Color.FromRgb(0xB4, 0xD2, 0x60), Color.FromRgb(0xDC, 0xCB, 0x55),
            Color.FromRgb(0xE8, 0xB0, 0x4C), Color.FromRgb(0xE8, 0x95, 0x4E), Color.FromRgb(0xE4, 0x7A, 0x55), Color.FromRgb(0xDE, 0x5F, 0x5C),
        };
        // Where the hero's face sits in our render of a hero card, as CardArt hands it over (its empty
        // margin trimmed: 404×419 of the 404×558 file), in fractions.
        private static readonly Rect HeroArt = new Rect(59 / 404.0, 38 / 419.0, 256 / 404.0, 256 / 419.0);

        private readonly CardStore _store;
        private readonly List<MatchRecord> _all;
        private readonly StackPanel _body = new StackPanel { Width = BodyW, Margin = new Thickness(Pad, 16, Pad, Pad), HorizontalAlignment = HorizontalAlignment.Center };
        private readonly StackPanel _tabs = new StackPanel { Orientation = Orientation.Horizontal };
        private readonly StackPanel _filters = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        private readonly ScrollViewer _scroll;
        private readonly FrameworkElement _root;

        private int _season;
        private string _mode;                 // solo | duos | custom
        private string _page = "overview";    // overview | heroes | matches | match
        private MatchRecord _match;
        private string _heroSort = "games";
        private int _heroDir = -1, _shown = 25;

        public StatsWindow(CardStore store, string folder)
        {
            _store = store;
            _all = Load(folder);
            Title = Strings.F("stats.windowTitle", ToolkitApp.DisplayName);
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 34, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false
            });
            try { Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = UiKit.ThinScrollBarStyle(9); } catch { }
            Width = Math.Min(BodyW + 2 * Pad + 12, SystemParameters.WorkArea.Width - 20);
            Height = Math.Max(480, Math.Min(900, SystemParameters.WorkArea.Height - 40));
            MinWidth = 640; MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Tag = TrayFlight.NoMirror;
            Background = new SolidColorBrush(WindowBg);
            try { OpponentBoardPanel.StartArt(_store, Dispatcher, s => { }); } catch { }

            _season = _all.Where(m => m.Season > 0).Select(m => m.Season).DefaultIfEmpty(0).Max();
            _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body, Focusable = false };
            var nav = new Grid { Margin = new Thickness(Pad, 14, Pad, 0), MaxWidth = BodyW };
            nav.Children.Add(_tabs);
            nav.Children.Add(_filters);
            var dock = new DockPanel();
            var bar = TitleBar();
            DockPanel.SetDock(bar, Dock.Top);
            DockPanel.SetDock(nav, Dock.Top);
            dock.Children.Add(bar);
            dock.Children.Add(nav);
            dock.Children.Add(_scroll);
            Content = _root = new Border { Child = dock, BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(1) };
            // A maximized window with its own chrome hangs over the screen's edges by the frame's width.
            StateChanged += (s, e) => _root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            KeyDown += (s, e) =>
            {
                if (e.Key != Key.Escape) return;
                if (_page == "match") Go("matches"); else Close();
            };
            Build();
        }

        /// <summary>The finished matches in a folder, oldest first. A file that doesn't parse is skipped.</summary>
        public static List<MatchRecord> Load(string folder)
        {
            var list = new List<MatchRecord>();
            try
            {
                if (Directory.Exists(folder))
                    foreach (string file in Directory.GetFiles(folder, "*.json"))
                    {
                        try
                        {
                            var m = JsonConvert.DeserializeObject<MatchRecord>(File.ReadAllText(file));
                            if (m != null && m.Finished && m.Place > 0 && m.Turns.Count > 0) list.Add(m);
                        }
                        catch { }
                    }
            }
            catch { }
            return list.OrderBy(m => m.StartUtc).ToList();
        }

        /// <summary>Opens a page: overview, heroes, matches, or "match" (the one whose id is given, else the latest).</summary>
        public void Go(string page, string matchId = null)
        {
            if (page == "match")
            {
                _match = (matchId != null ? _all.LastOrDefault(m => m.Id == matchId) : null) ?? _all.LastOrDefault();
                if (_match == null) page = "overview";
            }
            _page = page;
            Build();
            _scroll.ScrollToTop();
        }

        // ── Chrome ───────────────────────────────────────────────────────────────────────────────

        private UIElement TitleBar()
        {
            var name = new TextBlock
            {
                Text = Title, Foreground = UiKit.TextSecondary, FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis
            };
            var row = new DockPanel { LastChildFill = true };
            Action<string, bool, Action> button = (path, danger, click) =>
            {
                var glyph = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse(path), Stroke = UiKit.TextSecondary, StrokeThickness = 1.2,
                    Width = 10, Height = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
                var b = new Border { Width = 46, Background = Brushes.Transparent, Child = glyph };
                WindowChrome.SetIsHitTestVisibleInChrome(b, true);
                b.MouseEnter += (s, e) => { b.Background = UiKit.Br(danger ? Color.FromRgb(0xC4, 0x2B, 0x1C) : UiKit.RowBgHover); glyph.Stroke = Brushes.White; };
                b.MouseLeave += (s, e) => { b.Background = Brushes.Transparent; glyph.Stroke = UiKit.TextSecondary; };
                b.MouseLeftButtonUp += (s, e) => { e.Handled = true; click(); };
                DockPanel.SetDock(b, Dock.Right);
                row.Children.Add(b);
            };
            button("M0 0 L10 10 M10 0 L0 10", true, Close);
            button("M0.5 0.5 H9.5 V9.5 H0.5 Z", false, () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
            button("M0 5.5 H10", false, () => WindowState = WindowState.Minimized);
            row.Children.Add(name);
            return new Border { Height = 34, Child = row, Background = UiKit.Br(Color.FromRgb(0x0A, 0x0D, 0x13)) };
        }

        private Border Pill(string text, bool active, Action pick, bool tab = false)
        {
            var label = new TextBlock
            {
                Text = text, FontSize = tab ? 14 : 13, FontWeight = active || !tab ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = active ? UiKit.AccentBrush : UiKit.TextSecondary
            };
            var pill = new Border
            {
                Child = label, Padding = tab ? new Thickness(14, 7, 14, 7) : new Thickness(11, 5, 11, 5), Margin = new Thickness(0, 0, 4, 0), CornerRadius = new CornerRadius(7),
                Background = active ? UiKit.Br(UiKit.PanelActive) : tab ? Brushes.Transparent : UiKit.Br(UiKit.RowBg),
                BorderBrush = active ? UiKit.AccentBrush : tab ? Brushes.Transparent : UiKit.StrokeBrush, BorderThickness = new Thickness(1), Cursor = Cursors.Hand
            };
            if (!active)
            {
                var rest = pill.Background;
                pill.MouseEnter += (s, e) => pill.Background = UiKit.Br(tab ? UiKit.RowBg : UiKit.RowBgHover);
                pill.MouseLeave += (s, e) => pill.Background = rest;
            }
            pill.MouseLeftButtonUp += (s, e) => pick();
            return pill;
        }

        // ── Small pieces ─────────────────────────────────────────────────────────────────────────

        private Border Card(UIElement child, double width = BodyW)
        {
            var card = new Border
            {
                Child = child, Width = width, Padding = new Thickness(CardPad, 14, CardPad, 16), CornerRadius = new CornerRadius(10),
                Background = UiKit.Br(CardBg), BorderBrush = UiKit.Br(StrokeSoft), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 14)
            };
            _cards.Add(card);
            return card;
        }

        private static TextBlock Txt(string text, double size = 14, Brush brush = null, bool bold = false) => new TextBlock
        {
            Text = text, FontSize = size, Foreground = brush ?? UiKit.TextPrimary, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };

        /// <summary>A group header: small, upper case, muted.</summary>
        private static TextBlock Gh(string text) => new TextBlock
        {
            Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = UiKit.TextMuted, VerticalAlignment = VerticalAlignment.Center
        };

        private UIElement CardHead(string title, string more = null, Action go = null, string sub = null)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10), LastChildFill = false };
            row.Children.Add(Gh(title));
            if (sub != null) { var t = Txt(sub, 12.5, UiKit.TextMuted); t.Margin = new Thickness(10, 0, 0, 0); row.Children.Add(t); }
            if (more != null)
            {
                var link = Txt(more, 13, UiKit.AccentBrush, true);
                link.Cursor = Cursors.Hand;
                link.MouseEnter += (s, e) => link.TextDecorations = TextDecorations.Underline;
                link.MouseLeave += (s, e) => link.TextDecorations = null;
                link.MouseLeftButtonUp += (s, e) => go();
                DockPanel.SetDock(link, Dock.Right);
                row.Children.Add(link);
            }
            return row;
        }

        private static string N(int n) => Math.Abs(n).ToString("#,0", CultureInfo.InvariantCulture);
        private static string Signed(int n) => (n > 0 ? "+" : n < 0 ? "−" : "") + N(n);
        private static Brush SignBrush(int n) => n > 0 ? UiKit.Br(Good) : n < 0 ? UiKit.Br(Bad) : UiKit.TextSecondary;
        private static Color PlaceColor(int place) => PlaceColors[Math.Max(1, Math.Min(8, place)) - 1];
        private static string Ord(int place) => Strings.T("stats.ord." + Math.Max(1, Math.Min(8, place)));
        private static string Percent(int part, int whole) => whole == 0 ? "–" : Math.Round(100.0 * part / whole).ToString(CultureInfo.InvariantCulture) + "%";
        private static int Minutes(MatchRecord m) => (int)Math.Round(Math.Max(0, ((m.EndUtc ?? m.StartUtc) - m.StartUtc).TotalMinutes));

        private static CultureInfo DateCulture()
        {
            try { return CultureInfo.GetCultureInfo(Strings.Web); } catch { return CultureInfo.InvariantCulture; }
        }
        private static string Day(DateTime utc) => utc.ToLocalTime().ToString("d MMM", DateCulture());
        private static string Clock(DateTime utc) => utc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

        private static string ModeOf(MatchRecord m) => m.GameType == 24 || m.GameType == 39 ? "custom" : m.Duos || m.GameType == 37 ? "duos" : "solo";

        // A hero skin is its own card ("TB_BaconShop_HERO_90_SKIN_G"): stats go to the hero it dresses.
        private static string BaseHero(string cardId) => cardId == null ? "" : Regex.Replace(cardId, "_SKIN_[A-Za-z0-9]+$", "");
        private BgCard HeroCard(string cardId) => _store?.Lookup(BaseHero(cardId)) ?? _store?.Lookup(cardId);
        private string HeroName(string cardId) => HeroCard(cardId)?.DisplayName ?? BaseHero(cardId);
        private BgCard Lookup(string id) => id == null ? null : _store?.Lookup(id) ?? (id.EndsWith("_G", StringComparison.Ordinal) ? _store?.Lookup(id.Substring(0, id.Length - 2)) : null);

        /// <summary>Our render of a card, now or when it has loaded; nothing when there is none.</summary>
        private void Render(BgCard card, Action<BitmapSource> set)
        {
            if (card == null) return;
            try
            {
                var hit = CardArt.GetSync(card, false, 0);
                if (hit != null) { set(hit); return; }
                CardArt.LoadAsync(card, false, 0).ContinueWith(t =>
                {
                    if (t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && t.Result != null)
                        Dispatcher.BeginInvoke(new Action(() => set(t.Result)));
                });
            }
            catch { }
        }

        private Ellipse Round(BgCard card, double size, Rect crop, Brush ring = null, double ringWidth = 2)
        {
            var e = new Ellipse
            {
                Width = size, Height = size, Fill = UiKit.Br(Color.FromRgb(0x23, 0x2B, 0x3B)), Stroke = ring ?? UiKit.StrokeBrush, StrokeThickness = ringWidth,
                VerticalAlignment = VerticalAlignment.Center, ToolTip = card?.DisplayName
            };
            Render(card, bmp => e.Fill = new ImageBrush(bmp) { Viewbox = crop, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Stretch = Stretch.UniformToFill });
            return e;
        }

        private Ellipse HeroPortrait(string heroId, double size, bool me = false) => Round(HeroCard(heroId), size, HeroArt, me ? UiKit.TextPrimary : null);

        /// <summary>A hero portrait with the place it finished at riding on it, as on the game's leaderboard.</summary>
        private FrameworkElement PlacedPortrait(string heroId, double size, int place, bool big = false, bool me = false, Color? gap = null)
        {
            double b = big ? 34 : 21;
            var grid = new Grid { Width = size + 8, Height = size + 4, VerticalAlignment = VerticalAlignment.Center };
            var portrait = HeroPortrait(heroId, size, me);
            portrait.HorizontalAlignment = HorizontalAlignment.Left; portrait.VerticalAlignment = VerticalAlignment.Top;
            grid.Children.Add(portrait);
            grid.Children.Add(new Border
            {
                Width = b, Height = b, CornerRadius = new CornerRadius(b / 2), Background = UiKit.Br(PlaceColor(place)),
                BorderBrush = UiKit.Br(gap ?? UiKit.RowBg), BorderThickness = new Thickness(big ? 3 : 2),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, ToolTip = Ord(place),
                Child = new TextBlock
                {
                    Text = place.ToString(CultureInfo.InvariantCulture), FontSize = big ? 17 : 11.5, FontWeight = FontWeights.ExtraBold, Foreground = UiKit.Br(OnAccent),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            });
            return grid;
        }

        private static Image TierIcon(int tier, double size)
        {
            var img = new Image { Width = size, Height = size, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
            try { img.Source = ImageCache.Load(CardStore.TierIconPath(Math.Max(1, Math.Min(7, tier))), 0); } catch { }
            return img;
        }

        private static void Wlt(MatchRecord m, out int w, out int l, out int t)
        {
            w = m.Turns.Count(x => x.Result == "W"); l = m.Turns.Count(x => x.Result == "L"); t = m.Turns.Count(x => x.Result == "T");
        }

        private static TextBlock WltText(MatchRecord m, double size = 14)
        {
            int w, l, t; Wlt(m, out w, out l, out t);
            var tb = new TextBlock { FontSize = size, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            tb.Inlines.Add(new System.Windows.Documents.Run(w.ToString(CultureInfo.InvariantCulture)) { Foreground = UiKit.Br(Good) });
            tb.Inlines.Add(new System.Windows.Documents.Run(" – ") { Foreground = UiKit.TextSecondary, FontWeight = FontWeights.Normal });
            tb.Inlines.Add(new System.Windows.Documents.Run(l.ToString(CultureInfo.InvariantCulture)) { Foreground = UiKit.Br(Bad) });
            tb.Inlines.Add(new System.Windows.Documents.Run(" – ") { Foreground = UiKit.TextSecondary, FontWeight = FontWeights.Normal });
            tb.Inlines.Add(new System.Windows.Documents.Run(t.ToString(CultureInfo.InvariantCulture)) { Foreground = UiKit.Br(Tie) });
            return tb;
        }

        /// <summary>A hero's name that opens the hero's page on the site, in the toolkit's language.</summary>
        private FrameworkElement SiteLink(string heroId, double size = 14)
        {
            var card = HeroCard(heroId);
            var name = Txt(HeroName(heroId), size, null, true);
            if (card == null || string.IsNullOrEmpty(card.Slug)) return name;
            name.Cursor = Cursors.Hand;
            name.ToolTip = Strings.F("stats.site.open", card.DisplayName);
            name.MouseEnter += (s, e) => name.Foreground = UiKit.AccentBrush;
            name.MouseLeave += (s, e) => name.Foreground = UiKit.TextPrimary;
            name.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                try
                {
                    System.Diagnostics.Process.Start(Net.AssetClient.SiteBase + Locales.SitePath(Strings.Web, "/card/" + card.Slug)
                        + "?utm_source=toolkit&utm_medium=stats&utm_campaign=hero");
                }
                catch { }
            };
            return name;
        }

        /// <summary>One table row: fixed columns (a width of 0 takes what is left), each cell set to its side.</summary>
        private static Grid Columns(double[] widths, UIElement[] cells, HorizontalAlignment[] sides)
        {
            var grid = new Grid();
            for (int i = 0; i < widths.Length; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = widths[i] > 0 ? new GridLength(widths[i]) : new GridLength(1, GridUnitType.Star) });
                if (i >= cells.Length || cells[i] == null) continue;
                var fe = cells[i] as FrameworkElement;
                if (fe != null) { fe.HorizontalAlignment = sides[i]; fe.VerticalAlignment = VerticalAlignment.Center; }
                Grid.SetColumn(cells[i], i);
                grid.Children.Add(cells[i]);
            }
            return grid;
        }

        private static Border RowBox(UIElement child, double height) => new Border
        {
            Child = child, Height = height, CornerRadius = new CornerRadius(8), Background = UiKit.Br(UiKit.RowBg),
            Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 0, 4)
        };

        private static StackPanel Stack(params UIElement[] children)
        {
            var s = new StackPanel();
            foreach (var c in children) if (c != null) s.Children.Add(c);
            return s;
        }

        // ── Focus ────────────────────────────────────────────────────────────────────────────────
        // Pointing at a place, a hero or a match lights every piece that shows the same matches. A hard
        // focus also dims everything else; a soft one (rows, points) only lights its counterpart.

        private sealed class Mark { public FrameworkElement El; public string Id; public Func<Border> CardOf; public Action<bool> Lit; public FrameworkElement Label; }
        private readonly List<Mark> _marks = new List<Mark>();
        private readonly List<Border> _cards = new List<Border>();

        private void MarkAs(FrameworkElement el, string id, Func<Border> card, Action<bool> lit = null, FrameworkElement label = null) =>
            _marks.Add(new Mark { El = el, Id = id, Lit = lit, Label = label, CardOf = card });

        private void Source(FrameworkElement el, IEnumerable<string> ids, bool soft, Func<Border> card, Action parent = null)
        {
            var set = new HashSet<string>(ids);
            el.MouseEnter += (s, e) => Focus(set, soft, card?.Invoke());
            el.MouseLeave += (s, e) => { Unfocus(); parent?.Invoke(); };
        }

        private void Focus(HashSet<string> set, bool soft, Border source)
        {
            Unfocus();
            var lit = new HashSet<Border>();
            FrameworkElement label = null; int labels = 0;
            foreach (var m in _marks)
            {
                bool on = set.Contains(m.Id);
                m.Lit?.Invoke(on);
                if (!soft) m.El.Opacity = on ? 1 : 0.2;
                var owner = m.CardOf?.Invoke();
                if (on && owner != null) lit.Add(owner);
                if (on && m.Label != null) { label = m.Label; labels++; }
            }
            if (labels == 1) label.Visibility = Visibility.Visible;
            if (soft) return;
            foreach (var c in _cards) c.Opacity = c == source || lit.Contains(c) ? 1 : 0.3;
        }

        private void Unfocus()
        {
            foreach (var m in _marks)
            {
                m.Lit?.Invoke(false);
                m.El.Opacity = 1;
                if (m.Label != null) m.Label.Visibility = Visibility.Collapsed;
            }
            foreach (var c in _cards) c.Opacity = 1;
        }

        // ── Pages ────────────────────────────────────────────────────────────────────────────────

        private List<MatchRecord> InSeason() => _all.Where(m => _season == 0 || m.Season == _season).ToList();

        private void Build()
        {
            _body.Children.Clear();
            _marks.Clear(); _cards.Clear();
            _tabs.Children.Clear(); _filters.Children.Clear();
            string tab = _page == "match" ? "matches" : _page;
            foreach (string key in new[] { "overview", "heroes", "matches" })
            {
                string k = key;
                _tabs.Children.Add(Pill(Strings.T("stats.tab." + k), tab == k, () => Go(k), true));
            }
            if (_all.Count == 0)
            {
                _body.Children.Add(new TextBlock { Text = Strings.T("stats.empty"), FontSize = 15, Foreground = UiKit.TextSecondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 30, 0, 0) });
                return;
            }
            var seasons = _all.Select(m => m.Season).Where(s => s > 0).Distinct().OrderByDescending(s => s).ToList();
            foreach (int s in seasons)
            {
                int pick = s;
                _filters.Children.Add(Pill(Strings.F("stats.season", s), _season == s, () => { _season = pick; Go(_page == "match" ? "matches" : _page); }));
            }
            // Solo, Duos, Custom lobbies: only the ones this season has, and no switch for a single one.
            var modes = new[] { "solo", "duos", "custom" }.Where(k => InSeason().Any(m => ModeOf(m) == k)).ToList();
            if (!modes.Contains(_mode)) _mode = modes.FirstOrDefault();
            if (modes.Count > 1)
            {
                _filters.Children.Add(new Border { Width = 10 });
                foreach (string k in modes)
                {
                    string pick = k;
                    _filters.Children.Add(Pill(Strings.T("stats.mode." + k), _mode == k, () => { _mode = pick; Go(_page == "match" ? "matches" : _page); }));
                }
            }
            var matches = InSeason().Where(m => ModeOf(m) == _mode).ToList();
            if (_page == "match" && _match != null) { MatchPage(_match); return; }
            if (matches.Count == 0)
            {
                _body.Children.Add(new TextBlock { Text = Strings.T("stats.emptyFilter"), FontSize = 15, Foreground = UiKit.TextSecondary, Margin = new Thickness(0, 30, 0, 0) });
                return;
            }
            if (_page == "heroes")
            {
                Border card = null;
                card = Card(Stack(CardHead(Strings.T("stats.heroes.title")), HeroTable(HeroStats(matches), true, () => card)));
                _body.Children.Add(card);
            }
            else if (_page == "matches")
            {
                Border card = null;
                var stack = Stack(CardHead(Strings.T("stats.matches.title")), MatchTable(Enumerable.Reverse(matches).Take(_shown).ToList(), () => card));
                if (matches.Count > _shown)
                {
                    var more = Pill(Strings.T("stats.matches.more"), false, () => { _shown += 25; Build(); });
                    more.HorizontalAlignment = HorizontalAlignment.Center; more.Margin = new Thickness(0, 8, 0, 0);
                    stack.Children.Add(more);
                }
                card = Card(stack);
                _body.Children.Add(card);
            }
            else Overview(matches);
        }

        private void Overview(List<MatchRecord> matches)
        {
            _body.Children.Add(Tiles(matches));
            const double ChartW = 636, PlacesW = BodyW - ChartW - 14;
            Border chartCard = null, placesCard = null, heroCard = null, latestCard = null;
            chartCard = Card(Stack(CardHead(Strings.T("stats.rating.title")), RatingChart(matches, ChartW - 2 * CardPad - 2, () => chartCard)), ChartW);
            placesCard = Card(Stack(CardHead(Strings.T("stats.places.title")), PlaceBars(matches, PlacesW - 2 * CardPad - 2, () => placesCard)), PlacesW);
            chartCard.Margin = new Thickness(0, 0, 14, 14);
            var two = new StackPanel { Orientation = Orientation.Horizontal };
            two.Children.Add(chartCard); two.Children.Add(placesCard);
            _body.Children.Add(two);
            heroCard = Card(Stack(CardHead(Strings.T("stats.heroes.top"), Strings.T("stats.heroes.all"), () => Go("heroes")),
                HeroTable(HeroStats(matches).Take(5).ToList(), false, () => heroCard)));
            _body.Children.Add(heroCard);
            latestCard = Card(Stack(CardHead(Strings.T("stats.recent.title"), Strings.T("stats.matches.all"), () => Go("matches")),
                MatchTable(Enumerable.Reverse(matches).Take(5).ToList(), () => latestCard)));
            _body.Children.Add(latestCard);
        }

        // ── Tiles ────────────────────────────────────────────────────────────────────────────────

        private UIElement Tiles(List<MatchRecord> matches)
        {
            int n = matches.Count, won = 0, lost = 0, tied = 0, time = 0;
            foreach (var m in matches) { int w, l, t; Wlt(m, out w, out l, out t); won += w; lost += l; tied += t; time += Minutes(m); }
            var top = matches.Where(m => m.Place <= 4).ToList();
            var first = matches.Where(m => m.Place == 1).ToList();
            var rated = matches.Where(m => m.RatingAfter.HasValue).ToList();
            var last5 = Enumerable.Reverse(matches).Take(5).ToList();
            var last20 = Enumerable.Reverse(matches).Take(20).ToList();
            const double Gap = 10;
            double w6 = (BodyW - 5 * Gap) / 6;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            Action<string, string, string[][], List<MatchRecord>> tile = (label, value, lines, focus) =>
            {
                var stack = new StackPanel();
                stack.Children.Add(Gh(label));
                stack.Children.Add(new TextBlock { Text = value, FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = UiKit.TextPrimary, Margin = new Thickness(0, 4, 0, 0) });
                var box = new Border
                {
                    Child = stack, Width = w6, Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(10), Background = UiKit.Br(CardBg),
                    BorderBrush = UiKit.Br(StrokeSoft), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, row.Children.Count < 5 ? Gap : 0, 0), Focusable = true
                };
                // The breakdown opens under the tile, on hover or keyboard focus; the resting tile is the number alone.
                var pop = new StackPanel { MinWidth = w6 - 26 };
                foreach (var line in lines)
                {
                    var l = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 1, 0, 1) };
                    var v = new TextBlock
                    {
                        FontSize = 13, FontWeight = FontWeights.SemiBold, Text = line[1], Margin = new Thickness(18, 0, 0, 0),
                        Foreground = line.Length > 2 ? (line[2] == "+" ? UiKit.Br(Good) : UiKit.Br(Bad)) : UiKit.TextPrimary
                    };
                    DockPanel.SetDock(v, Dock.Right);
                    l.Children.Add(v);
                    l.Children.Add(new TextBlock { Text = line[0], FontSize = 13, Foreground = UiKit.TextSecondary });
                    pop.Children.Add(l);
                }
                var tip = new ToolTip
                {
                    Content = pop, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, PlacementTarget = box, VerticalOffset = 6,
                    Background = UiKit.Br(PopBg), BorderBrush = UiKit.AccentBrush, BorderThickness = new Thickness(1), Padding = new Thickness(12, 10, 12, 10), HasDropShadow = true
                };
                box.ToolTip = tip;
                ToolTipService.SetInitialShowDelay(box, 0);
                ToolTipService.SetShowDuration(box, 600000);
                ToolTipService.SetBetweenShowDelay(box, 0);
                Action<bool> hot = on => { box.BorderBrush = on ? UiKit.AccentBrush : UiKit.Br(StrokeSoft); box.Background = UiKit.Br(on ? UiKit.RowBg : CardBg); };
                box.MouseEnter += (s, e) => hot(true);
                box.MouseLeave += (s, e) => hot(false);
                box.GotKeyboardFocus += (s, e) => { hot(true); tip.IsOpen = true; };
                box.LostKeyboardFocus += (s, e) => { hot(false); tip.IsOpen = false; };
                _cards.Add(box);
                if (focus != null) Source(box, focus.Select(m => m.Id), false, () => box);
                row.Children.Add(box);
            };
            Func<int, string> str = v => v.ToString(CultureInfo.InvariantCulture);
            Func<List<MatchRecord>, string> avg = list => list.Average(m => m.Place).ToString("0.00", CultureInfo.InvariantCulture);
            tile(Strings.T("stats.tile.games"), str(n), new[]
            {
                new[] { Strings.T("stats.pop.firstRecorded"), Day(matches[0].StartUtc) },
                new[] { Strings.T("stats.pop.latest"), Day(matches[n - 1].StartUtc) },
                new[] { Strings.T("stats.pop.timePlayed"), Strings.F("stats.pop.hoursMinutes", time / 60, time % 60) },
            }, null);
            // How the latest matches went against the whole season's average on the tile.
            var recent = new List<string[]> { new[] { Strings.F("stats.pop.lastN", last5.Count), avg(last5) } };
            if (last20.Count > last5.Count) recent.Add(new[] { Strings.F("stats.pop.lastN", last20.Count), avg(last20) });
            tile(Strings.T("stats.tile.avgPlace"), avg(matches), recent.ToArray(), null);
            tile(Strings.T("stats.tile.top4"), Percent(top.Count, n), new[]
            {
                new[] { Strings.T("stats.pop.top4"), str(top.Count) }, new[] { Strings.T("stats.pop.matches"), str(n) },
            }, top);
            tile(Strings.T("stats.tile.first"), Percent(first.Count, n), new[]
            {
                new[] { Strings.T("stats.pop.firsts"), str(first.Count) }, new[] { Strings.T("stats.pop.matches"), str(n) },
            }, first);
            tile(Strings.T("stats.tile.fights"), Percent(won, won + lost + tied), new[]
            {
                new[] { Strings.T("stats.pop.won"), str(won) }, new[] { Strings.T("stats.pop.lost"), str(lost) }, new[] { Strings.T("stats.pop.tied"), str(tied) },
            }, null);
            if (rated.Count > 0)
            {
                var last = rated[rated.Count - 1];
                int net = rated.Sum(m => m.RatingChange ?? 0), lastChange = last.RatingChange ?? 0;
                tile(Strings.T("stats.tile.rating"), N(last.RatingAfter.Value), new[]
                {
                    new[] { Strings.T("stats.pop.peak"), N(rated.Max(m => m.RatingAfter.Value)) },
                    new[] { Strings.T("stats.pop.lastMatch"), Signed(lastChange), lastChange < 0 ? "-" : "+" },
                    new[] { Strings.F("stats.pop.overN", rated.Count), Signed(net), net < 0 ? "-" : "+" },
                }, null);
            }
            else tile(Strings.T("stats.tile.rating"), "–", new[] { new[] { Strings.T("stats.pop.notRated"), "" } }, null);
            return row;
        }

        // ── Charts ───────────────────────────────────────────────────────────────────────────────

        private static void Put(Canvas c, UIElement el, double x, double y) { Canvas.SetLeft(el, x); Canvas.SetTop(el, y); c.Children.Add(el); }

        private static TextBlock AxisText(string text, double width, TextAlignment align, Brush brush = null, double size = 11.5, bool bold = false) => new TextBlock
        {
            Text = text, Width = width, TextAlignment = align, FontSize = size, Foreground = brush ?? UiKit.TextMuted, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal
        };

        private static double NiceStep(double span, int lines)
        {
            foreach (double s in new double[] { 5, 10, 25, 50, 100, 200, 250, 500, 1000, 2000, 2500, 5000 }) if (span / s <= lines) return s;
            return 10000;
        }

        /// <summary>The rating after every rated match of the season, in order, with the days under it.</summary>
        private UIElement RatingChart(List<MatchRecord> matches, double W, Func<Border> card)
        {
            var pts = matches.Where(m => m.RatingAfter.HasValue).ToList();
            if (pts.Count < 2) return new TextBlock { Text = Strings.T("stats.rating.none"), FontSize = 13, Foreground = UiKit.TextMuted, Margin = new Thickness(0, 30, 0, 30) };
            const double H = 236, L = 48, R = 14, T = 22, B = 40;
            double lo = pts.Min(m => m.RatingAfter.Value), hi = pts.Max(m => m.RatingAfter.Value);
            double step = NiceStep(Math.Max(hi - lo, 50), 5);
            lo = Math.Floor(lo / step) * step; hi = Math.Ceiling(hi / step) * step;
            if (hi <= lo) hi = lo + step;
            Func<int, double> x = i => L + 8 + i * (W - L - R - 16) / (pts.Count - 1);
            Func<double, double> y = v => T + (hi - v) * (H - T - B) / (hi - lo);
            var c = new Canvas { Width = W, Height = H };
            for (double v = lo; v <= hi + 0.5; v += step)
            {
                c.Children.Add(new Line { X1 = L, X2 = W - R, Y1 = y(v), Y2 = y(v), Stroke = UiKit.Br(StrokeSoft), StrokeThickness = 1 });
                Put(c, AxisText(N((int)v), L - 8, TextAlignment.Right), 0, y(v) - 8);
            }
            var area = new Polygon { Fill = UiKit.Br(Color.FromArgb(0x16, 0xE8, 0xB5, 0x4B)) };
            var line = new Polyline { Stroke = UiKit.AccentBrush, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            for (int i = 0; i < pts.Count; i++) { var p = new Point(x(i), y(pts[i].RatingAfter.Value)); line.Points.Add(p); area.Points.Add(p); }
            area.Points.Add(new Point(x(pts.Count - 1), H - B)); area.Points.Add(new Point(x(0), H - B));
            c.Children.Add(area); c.Children.Add(line);
            // The time axis: a tick and a date under the first match of each day.
            c.Children.Add(new Line { X1 = L, X2 = W - R, Y1 = H - B, Y2 = H - B, Stroke = UiKit.StrokeBrush, StrokeThickness = 1 });
            string lastDay = null; double lastX = -99;
            for (int i = 0; i < pts.Count; i++)
            {
                string day = Day(pts[i].StartUtc);
                if (day == lastDay) continue;
                lastDay = day;
                c.Children.Add(new Line { X1 = x(i), X2 = x(i), Y1 = H - B, Y2 = H - B + 5, Stroke = UiKit.StrokeBrush, StrokeThickness = 1 });
                if (x(i) - lastX <= 50) continue;
                Put(c, AxisText(day, 60, TextAlignment.Center, UiKit.TextSecondary), x(i) - 30, H - B + 8);
                lastX = x(i);
            }
            for (int i = 0; i < pts.Count; i++)
            {
                var m = pts[i];
                double cx = x(i), cy = y(m.RatingAfter.Value), r = i == pts.Count - 1 ? 5 : 4;
                var dot = new Ellipse
                {
                    Width = 2 * r, Height = 2 * r, Fill = UiKit.Br(PlaceColor(m.Place)), Stroke = UiKit.Br(CardBg), StrokeThickness = 1.5,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
                var hit = new Grid { Width = 22, Height = 22, Background = Brushes.Transparent };
                hit.Children.Add(dot);
                Put(c, hit, cx - 11, cy - 11);
                var label = new Border
                {
                    Background = UiKit.Br(PopBg), BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(7, 2, 7, 3), Visibility = Visibility.Collapsed, IsHitTestVisible = false,
                    Child = Txt(HeroName(m.Hero) + " · " + Ord(m.Place) + " · " + N(m.RatingAfter.Value), 11.5)
                };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double lw = label.DesiredSize.Width;
                Put(c, label, Math.Min(Math.Max(cx - lw / 2, L), W - R - lw), cy < T + 34 ? cy + 12 : cy - 32);
                Panel.SetZIndex(label, 5);
                MarkAs(hit, m.Id, card, on =>
                {
                    dot.Stroke = on ? UiKit.TextPrimary : UiKit.Br(CardBg); dot.StrokeThickness = on ? 2 : 1.5;
                    dot.Width = dot.Height = on ? 2 * r + 4 : 2 * r;
                }, label);
                Source(hit, new[] { m.Id }, true, card);
            }
            return c;
        }

        private UIElement PlaceBars(List<MatchRecord> matches, double W, Func<Border> card)
        {
            const double H = 236, B = 40, T = 24;
            int max = Math.Max(1, Enumerable.Range(1, 8).Max(p => matches.Count(m => m.Place == p)));
            double bw = W / 8;
            var c = new Canvas { Width = W, Height = H };
            for (int p = 1; p <= 8; p++)
            {
                var list = matches.Where(m => m.Place == p).ToList();
                double h = Math.Max(2, list.Count * (H - T - B) / max);
                var col = new Canvas { Width = bw, Height = H - T, Background = Brushes.Transparent };
                Put(col, new Border { Width = bw - 10, Height = h, CornerRadius = new CornerRadius(4), Background = UiKit.Br(PlaceColor(p)), Opacity = list.Count == 0 ? 0.25 : 1 }, 5, H - T - B - h);
                if (list.Count > 0) Put(col, AxisText(list.Count.ToString(CultureInfo.InvariantCulture), bw, TextAlignment.Center, UiKit.TextPrimary, 12.5, true), 0, H - T - B - h - 20);
                Put(col, AxisText(p.ToString(CultureInfo.InvariantCulture), bw, TextAlignment.Center, UiKit.TextSecondary, 12.5), 0, H - T - B + 8);
                Put(c, col, (p - 1) * bw, T);
                if (list.Count > 0) Source(col, list.Select(m => m.Id), false, card);
            }
            c.Children.Add(new Line { X1 = 0, X2 = W, Y1 = H - B, Y2 = H - B, Stroke = UiKit.StrokeBrush, StrokeThickness = 1 });
            return c;
        }

        // ── Heroes ───────────────────────────────────────────────────────────────────────────────

        private sealed class HeroLine
        {
            public string Hero, Name; public List<MatchRecord> All;
            public int Games, Top, First, Net; public bool Rated; public double Avg;
        }

        private List<HeroLine> HeroStats(List<MatchRecord> matches)
        {
            var lines = matches.GroupBy(m => BaseHero(m.Hero)).Select(g =>
            {
                var list = g.ToList();
                return new HeroLine
                {
                    Hero = g.Key, Name = HeroName(g.Key), All = list, Games = list.Count, Avg = list.Average(m => m.Place),
                    Top = list.Count(m => m.Place <= 4), First = list.Count(m => m.Place == 1),
                    Net = list.Sum(m => m.RatingChange ?? 0), Rated = list.Any(m => m.RatingChange.HasValue),
                };
            }).ToList();
            Comparison<HeroLine> by;
            switch (_heroSort)
            {
                case "name": by = (a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); break;
                case "avg": by = (a, b) => a.Avg.CompareTo(b.Avg); break;
                case "top": by = (a, b) => ((double)a.Top / a.Games).CompareTo((double)b.Top / b.Games); break;
                case "first": by = (a, b) => ((double)a.First / a.Games).CompareTo((double)b.First / b.Games); break;
                case "net": by = (a, b) => a.Net.CompareTo(b.Net); break;
                default: by = (a, b) => a.Games.CompareTo(b.Games); break;
            }
            lines.Sort((a, b) =>
            {
                int c = by(a, b) * _heroDir;
                if (c == 0) c = a.Avg.CompareTo(b.Avg);
                if (c == 0) c = b.All.Max(m => m.StartUtc).CompareTo(a.All.Max(m => m.StartUtc));
                return c;
            });
            return lines;
        }

        private static readonly double[] HeroCols = { 0, 100, 140, 90, 110, 170, 140 };
        private static readonly HorizontalAlignment[] HeroSides =
        {
            HorizontalAlignment.Left, HorizontalAlignment.Center, HorizontalAlignment.Center, HorizontalAlignment.Center, HorizontalAlignment.Center, HorizontalAlignment.Center, HorizontalAlignment.Right
        };

        private UIElement HeroTable(List<HeroLine> rows, bool sortable, Func<Border> card)
        {
            var stack = new StackPanel();
            string[] keys = { "name", "games", "avg", "top", "first", null, "net" };
            string[] titles = { "stats.col.hero", "stats.col.games", "stats.col.avgPlace", "stats.col.top4", "stats.col.first", "stats.col.places", "stats.col.netRating" };
            var heads = new UIElement[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                string k = keys[i];
                bool on = sortable && k == _heroSort;
                var h = Gh(Strings.T(titles[i]) + (on ? (_heroDir > 0 ? " ▲" : " ▼") : ""));
                if (on) h.Foreground = UiKit.AccentBrush;
                if (sortable && k != null)
                {
                    h.Cursor = Cursors.Hand;
                    h.MouseLeftButtonUp += (s, e) =>
                    {
                        if (_heroSort == k) _heroDir = -_heroDir; else { _heroSort = k; _heroDir = k == "name" || k == "avg" ? 1 : -1; }
                        Build();
                    };
                }
                heads[i] = h;
            }
            stack.Children.Add(new Border { Child = Columns(HeroCols, heads, HeroSides), Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 0, 6) });
            foreach (var hero in rows)
            {
                var line = hero;
                var who = new StackPanel { Orientation = Orientation.Horizontal };
                var portrait = HeroPortrait(line.Hero, 36);
                portrait.Margin = new Thickness(0, 0, 12, 0);
                who.Children.Add(portrait);
                who.Children.Add(SiteLink(line.Hero));
                Action rowFocus = () => Focus(new HashSet<string>(line.All.Select(m => m.Id)), true, card());
                // Places: one cell per place; pointing at a filled one dims the rest and shows those matches.
                var dist = new StackPanel { Orientation = Orientation.Horizontal };
                for (int p = 1; p <= 8; p++)
                {
                    var at = line.All.Where(m => m.Place == p).ToList();
                    var cell = new Border
                    {
                        Width = 15, Height = 20, CornerRadius = new CornerRadius(3), Margin = new Thickness(1, 0, 1, 0),
                        Background = UiKit.Br(at.Count > 0 ? PlaceColor(p) : StrokeSoft), ToolTip = Ord(p) + ": " + at.Count,
                        Child = at.Count > 1 ? new TextBlock
                        {
                            Text = at.Count.ToString(CultureInfo.InvariantCulture), FontSize = 10, FontWeight = FontWeights.Bold, Foreground = UiKit.Br(OnAccent),
                            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                        } : null
                    };
                    if (at.Count > 0) Source(cell, at.Select(m => m.Id), false, card, rowFocus);
                    dist.Children.Add(cell);
                }
                var cells = new UIElement[]
                {
                    who, Txt(line.Games.ToString(CultureInfo.InvariantCulture)), Txt(line.Avg.ToString("0.00", CultureInfo.InvariantCulture)),
                    Txt(Percent(line.Top, line.Games)), Txt(Percent(line.First, line.Games)), dist,
                    line.Rated ? Txt(Signed(line.Net), 14, SignBrush(line.Net)) : Txt("–", 14, UiKit.TextMuted),
                };
                var row = RowBox(Columns(HeroCols, cells, HeroSides), 48);
                row.MouseEnter += (s, e) => rowFocus();
                row.MouseLeave += (s, e) => Unfocus();
                stack.Children.Add(row);
            }
            return stack;
        }

        // ── Matches ──────────────────────────────────────────────────────────────────────────────

        private static readonly double[] MatchCols = { 0, 150, 80, 90, 150, 150, 130 };
        private static readonly HorizontalAlignment[] MatchSides =
        {
            HorizontalAlignment.Left, HorizontalAlignment.Left, HorizontalAlignment.Center, HorizontalAlignment.Center, HorizontalAlignment.Center, HorizontalAlignment.Left, HorizontalAlignment.Right
        };

        /// <summary>What the final board was made of: its two most common minion types.</summary>
        private UIElement BoardTypes(MatchRecord m)
        {
            var counts = new Dictionary<string, int>();
            foreach (var minion in m.FinalBoard)
            {
                var card = Lookup(minion.Card);
                if (card?.MinionTypes == null) continue;
                foreach (string t in card.MinionTypes) { int n; counts.TryGetValue(t, out n); counts[t] = n + 1; }
            }
            if (counts.Count == 0) return Txt("–", 14, UiKit.TextMuted);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var kv in counts.OrderByDescending(k => k.Value).Take(2))
            {
                row.Children.Add(new Ellipse { Width = 24, Height = 24, Fill = TribeIcons.BrushFor(kv.Key), Stroke = UiKit.StrokeBrush, StrokeThickness = 1.5, ToolTip = kv.Value + " " + kv.Key });
                var n = Txt(kv.Value.ToString(CultureInfo.InvariantCulture), 14, UiKit.TextSecondary, true);
                n.Margin = new Thickness(5, 0, 10, 0);
                row.Children.Add(n);
            }
            return row;
        }

        private UIElement MatchTable(List<MatchRecord> rows, Func<Border> card)
        {
            var stack = new StackPanel();
            string[] titles = { "stats.col.hero", "stats.col.played", "stats.col.rounds", "stats.col.length", "stats.col.fights", "stats.col.finalBoard", "stats.col.rating" };
            stack.Children.Add(new Border
            {
                Child = Columns(MatchCols, titles.Select(t => (UIElement)Gh(Strings.T(t))).ToArray(), MatchSides), Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 0, 6)
            });
            foreach (var match in rows)
            {
                var m = match;
                var who = new StackPanel { Orientation = Orientation.Horizontal };
                who.Children.Add(PlacedPortrait(m.Hero, 36, m.Place));
                var name = Txt(HeroName(m.Hero), 14, null, true);
                name.Margin = new Thickness(10, 0, 0, 0);
                who.Children.Add(name);
                var played = new TextBlock { FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
                played.Inlines.Add(new System.Windows.Documents.Run(Day(m.StartUtc)) { Foreground = UiKit.TextPrimary });
                played.Inlines.Add(new System.Windows.Documents.Run(" · " + Clock(m.StartUtc)) { Foreground = UiKit.TextMuted });
                var rating = new TextBlock { FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
                if (m.RatingAfter.HasValue)
                {
                    rating.Inlines.Add(new System.Windows.Documents.Run(N(m.RatingAfter.Value) + " ") { Foreground = UiKit.TextPrimary });
                    rating.Inlines.Add(new System.Windows.Documents.Run(Signed(m.RatingChange ?? 0)) { Foreground = SignBrush(m.RatingChange ?? 0) });
                }
                else { rating.Text = "–"; rating.Foreground = UiKit.TextMuted; }
                var cells = new UIElement[]
                {
                    who, played, Txt(m.Turns.Count.ToString(CultureInfo.InvariantCulture)), Txt(Strings.F("stats.minutes", Minutes(m))), WltText(m), BoardTypes(m), rating,
                };
                var row = RowBox(Columns(MatchCols, cells, MatchSides), 48);
                row.Cursor = Cursors.Hand;
                bool lit = false, over = false;
                Action paint = () => row.Background = UiKit.Br(lit || over ? UiKit.RowBgHover : UiKit.RowBg);
                row.MouseEnter += (s, e) => { over = true; paint(); };
                row.MouseLeave += (s, e) => { over = false; paint(); };
                row.MouseLeftButtonUp += (s, e) => Go("match", m.Id);
                MarkAs(row, m.Id, card, on => { lit = on; paint(); });
                Source(row, new[] { m.Id }, true, card);
                stack.Children.Add(row);
            }
            return stack;
        }

        // ── Test aid ─────────────────────────────────────────────────────────────────────────────

        /// <summary>Test aid: the whole page (not only what the window shows) as a PNG, once its pictures had time to load.</summary>
        public void TestSnap(string path, double waitSeconds = 3)
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(waitSeconds) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                try
                {
                    double w = BodyW + 2 * Pad;
                    _body.Measure(new Size(w, double.PositiveInfinity));
                    int h = (int)Math.Ceiling(_body.DesiredSize.Height) + 4;
                    var shot = new DrawingVisual();
                    using (var dc = shot.RenderOpen())
                    {
                        dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
                        dc.DrawRectangle(new VisualBrush(_body) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null,
                            new Rect(Pad, 16, _body.ActualWidth, Math.Max(_body.ActualHeight, 1)));
                    }
                    var bmp = new RenderTargetBitmap((int)w, h, 96, 96, PixelFormats.Pbgra32);
                    bmp.Render(shot);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(bmp));
                    using (var f = File.Create(path)) enc.Save(f);
                }
                catch { }
            };
            timer.Start();
        }
    }
}
