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
using Line = System.Windows.Shapes.Line;
using Polyline = System.Windows.Shapes.Polyline;
using Ellipse = System.Windows.Shapes.Ellipse;
using HsbgCardLookup.Data;
using HsbgCardLookup.Game;
using HsbgCardLookup.Localization;
using HsbgCardLookup.Search;
using Newtonsoft.Json;

namespace HsbgCardLookup.Ui
{
    /// <summary>
    /// The player's own match history, from the recorder's files (<see cref="MatchRecorder"/>): a season
    /// switch, totals, rating over matches, places, heroes and the latest matches with their final boards.
    /// Opened from the tray menu, like the settings. Reads the folder each time it opens; draws nothing live.
    /// </summary>
    public sealed class StatsWindow : Window
    {
        private const double PageW = 980, PageH = 760, Pad = 22;
        private static readonly Color Good = Color.FromRgb(0x6F, 0xCF, 0x7C), Bad = Color.FromRgb(0xE0, 0x6C, 0x5E);

        private readonly CardStore _store;
        private readonly List<MatchRecord> _all;
        private readonly StackPanel _body = new StackPanel { Margin = new Thickness(Pad, 14, Pad, Pad) };
        private FrameworkElement _root;
        private int _season;          // 0 = every season
        private bool? _duos;          // null = both

        public StatsWindow(CardStore store, string folder)
        {
            _store = store;
            _all = Load(folder);
            Title = Strings.F("stats.windowTitle", ToolkitApp.DisplayName);
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            try { Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = UiKit.ThinScrollBarStyle(9); } catch { }
            Width = PageW;
            Height = Math.Max(420, Math.Min(PageH, SystemParameters.WorkArea.Height - 40));
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x16, 0x1E));

            _season = _all.Where(m => m.Season > 0).Select(m => m.Season).DefaultIfEmpty(0).Max();
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body };
            var dock = new DockPanel();
            var bar = TitleBar();
            DockPanel.SetDock(bar, Dock.Top);
            dock.Children.Add(bar);
            dock.Children.Add(scroll);
            Content = _root = dock;
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
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

        // ── Chrome ───────────────────────────────────────────────────────────────────────────────

        private UIElement TitleBar()
        {
            var name = new TextBlock
            {
                Text = Title, Foreground = UiKit.TextSecondary, FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis
            };
            var glyph = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M0 0 L10 10 M10 0 L0 10"), Stroke = UiKit.TextSecondary, StrokeThickness = 1.3,
                Width = 10, Height = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            var close = new Border { Width = 46, Background = Brushes.Transparent, Child = glyph };
            close.MouseEnter += (s, e) => { close.Background = UiKit.Br(Color.FromRgb(0xC4, 0x2B, 0x1C)); glyph.Stroke = Brushes.White; };
            close.MouseLeave += (s, e) => { close.Background = Brushes.Transparent; glyph.Stroke = UiKit.TextSecondary; };
            close.MouseLeftButtonDown += (s, e) => e.Handled = true;
            close.MouseLeftButtonUp += (s, e) => { e.Handled = true; Close(); };
            var row = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(close, Dock.Right);
            row.Children.Add(close);
            row.Children.Add(name);
            var bar = new Border
            {
                Height = 34, Child = row, Background = UiKit.Br(Color.FromRgb(0x0A, 0x0D, 0x13)),
                BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(0, 0, 0, 1)
            };
            bar.MouseLeftButtonDown += (s, e) => { try { DragMove(); } catch { } };
            return bar;
        }

        private Border Pill(string text, bool active, Action pick)
        {
            var label = new TextBlock { Text = text, FontSize = 13, Foreground = active ? UiKit.TextPrimary : UiKit.TextSecondary };
            var pill = new Border
            {
                Child = label, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 0), CornerRadius = new CornerRadius(7),
                Background = active ? UiKit.Br(UiKit.PanelActive) : UiKit.Br(UiKit.RowBg),
                BorderBrush = active ? UiKit.AccentBrush : UiKit.StrokeBrush, BorderThickness = new Thickness(1), Cursor = Cursors.Hand
            };
            pill.MouseLeftButtonUp += (s, e) => pick();
            return pill;
        }

        private static Border Card(UIElement child, double width = double.NaN) => new Border
        {
            Child = child, Width = width, Padding = new Thickness(16, 12, 16, 14), CornerRadius = new CornerRadius(10),
            Background = UiKit.Br(Color.FromRgb(0x17, 0x1C, 0x27)), BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(1)
        };

        private static TextBlock Heading(string text) => new TextBlock
        {
            Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = UiKit.TextMuted, Margin = new Thickness(0, 0, 0, 10)
        };

        private static TextBlock Cell(string text, double width, Brush brush = null, TextAlignment align = TextAlignment.Right, double size = 13) => new TextBlock
        {
            Text = text, Width = width, FontSize = size, TextAlignment = align, Foreground = brush ?? UiKit.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };

        private static string Signed(int n) => (n > 0 ? "+" : n < 0 ? "−" : "") + Math.Abs(n).ToString(CultureInfo.InvariantCulture);
        private static Brush SignBrush(int n) => n > 0 ? UiKit.Br(Good) : n < 0 ? UiKit.Br(Bad) : UiKit.TextSecondary;
        private static Color PlaceColor(int place) => place == 1 ? UiKit.Accent : place <= 4 ? Good : Bad;

        // ── Content ──────────────────────────────────────────────────────────────────────────────

        private void Build()
        {
            _body.Children.Clear();
            var seasons = _all.Select(m => m.Season).Where(s => s > 0).Distinct().OrderByDescending(s => s).ToList();
            bool bothModes = _all.Any(m => m.Duos) && _all.Any(m => !m.Duos);

            var filters = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            filters.Children.Add(Pill(Strings.T("stats.allSeasons"), _season == 0, () => { _season = 0; Build(); }));
            foreach (int s in seasons)
            {
                int season = s;
                filters.Children.Add(Pill(Strings.F("stats.season", season), _season == season, () => { _season = season; Build(); }));
            }
            if (bothModes)
            {
                filters.Children.Add(new Border { Width = 14 });
                filters.Children.Add(Pill(Strings.T("stats.solo"), _duos == false, () => { _duos = _duos == false ? (bool?)null : false; Build(); }));
                filters.Children.Add(Pill(Strings.T("stats.duos"), _duos == true, () => { _duos = _duos == true ? (bool?)null : true; Build(); }));
            }
            _body.Children.Add(filters);

            var matches = _all.Where(m => (_season == 0 || m.Season == _season) && (_duos == null || m.Duos == _duos)).ToList();
            if (matches.Count == 0)
            {
                _body.Children.Add(new TextBlock
                {
                    Text = Strings.T(_all.Count == 0 ? "stats.empty" : "stats.emptyFilter"), Foreground = UiKit.TextSecondary, FontSize = 14,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 30, 0, 0), HorizontalAlignment = HorizontalAlignment.Center
                });
                return;
            }

            _body.Children.Add(Tiles(matches));
            var charts = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            charts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.55, GridUnitType.Star) });
            charts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            charts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var rating = Card(RatingChart(matches)); var places = Card(PlaceBars(matches));
            Grid.SetColumn(places, 2);
            charts.Children.Add(rating); charts.Children.Add(places);
            _body.Children.Add(charts);
            _body.Children.Add(new Border { Height = 12 });
            _body.Children.Add(Card(Heroes(matches)));
            _body.Children.Add(new Border { Height = 12 });
            _body.Children.Add(Card(Recent(matches)));
        }

        private UIElement Tiles(List<MatchRecord> m)
        {
            int rated = m.Count(x => x.RatingChange.HasValue), net = m.Sum(x => x.RatingChange ?? 0);
            var last = m.LastOrDefault(x => x.RatingAfter.HasValue);
            var fights = m.SelectMany(x => x.Turns).Where(t => t.Result != null).ToList();
            int won = fights.Count(t => t.Result == "W");
            var grid = new UniformGrid6();
            grid.Add(Strings.T("stats.tile.games"), m.Count.ToString(CultureInfo.InvariantCulture), null);
            grid.Add(Strings.T("stats.tile.avgPlace"), m.Average(x => x.Place).ToString("0.00", CultureInfo.InvariantCulture), null);
            grid.Add(Strings.T("stats.tile.top4"), Percent(m.Count(x => x.Place <= 4), m.Count), null);
            grid.Add(Strings.T("stats.tile.first"), Percent(m.Count(x => x.Place == 1), m.Count), null);
            grid.Add(Strings.T("stats.tile.fights"), fights.Count == 0 ? "–" : Percent(won, fights.Count), null);
            grid.Add(Strings.T("stats.tile.rating"), last == null ? "–" : last.RatingAfter.Value.ToString(CultureInfo.InvariantCulture),
                rated == 0 ? null : Signed(net), rated == 0 ? null : SignBrush(net));
            return grid.Element;
        }

        private static string Percent(int part, int whole) => whole == 0 ? "–" : (100.0 * part / whole).ToString("0", CultureInfo.InvariantCulture) + "%";

        // Six equal tiles in a row.
        private sealed class UniformGrid6
        {
            public readonly Grid Element = new Grid();
            private int _n;
            public void Add(string label, string value, string note, Brush noteBrush = null)
            {
                if (_n > 0) Element.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                Element.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var stack = new StackPanel();
                stack.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = UiKit.TextMuted });
                var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                line.Children.Add(new TextBlock { Text = value, FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = UiKit.TextPrimary });
                if (note != null)
                    line.Children.Add(new TextBlock { Text = note, FontSize = 13, Foreground = noteBrush ?? UiKit.TextSecondary, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 0, 4) });
                stack.Children.Add(line);
                var card = Card(stack);
                card.Padding = new Thickness(14, 10, 10, 12);
                Grid.SetColumn(card, _n * 2);
                Element.Children.Add(card);
                _n++;
            }
        }

        private UIElement RatingChart(List<MatchRecord> matches)
        {
            const double W = 500, H = 170, Left = 44;
            var stack = new StackPanel();
            stack.Children.Add(Heading(Strings.T("stats.rating.title")));
            var pts = matches.Where(m => m.RatingAfter.HasValue).Select(m => m.RatingAfter.Value).ToList();
            if (pts.Count < 2)
            {
                stack.Children.Add(new TextBlock { Text = Strings.T("stats.rating.none"), Foreground = UiKit.TextSecondary, FontSize = 13, Height = H, TextWrapping = TextWrapping.Wrap });
                return stack;
            }
            int lo = pts.Min(), hi = pts.Max();
            int step = hi - lo > 1200 ? 500 : hi - lo > 500 ? 200 : hi - lo > 200 ? 100 : 50;
            lo = lo / step * step; hi = (hi + step - 1) / step * step;
            if (hi == lo) hi = lo + step;
            var canvas = new Canvas { Width = W, Height = H, HorizontalAlignment = HorizontalAlignment.Left };
            Func<int, double> y = v => H - 14 - (H - 24) * (v - lo) / (double)(hi - lo);
            Func<int, double> x = i => Left + (W - Left - 10) * i / (double)(pts.Count - 1);
            for (int v = lo; v <= hi; v += step)
            {
                canvas.Children.Add(new Line { X1 = Left, X2 = W, Y1 = y(v), Y2 = y(v), Stroke = UiKit.StrokeBrush, StrokeThickness = 1, Opacity = 0.5 });
                var label = new TextBlock { Text = v.ToString(CultureInfo.InvariantCulture), FontSize = 11, Foreground = UiKit.TextMuted, Width = Left - 8, TextAlignment = TextAlignment.Right };
                Canvas.SetTop(label, y(v) - 8);
                canvas.Children.Add(label);
            }
            var line = new Polyline { Stroke = UiKit.AccentBrush, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            for (int i = 0; i < pts.Count; i++) line.Points.Add(new Point(x(i), y(pts[i])));
            canvas.Children.Add(line);
            if (pts.Count <= 60)
                for (int i = 0; i < pts.Count; i++)
                {
                    var dot = new Ellipse { Width = 6, Height = 6, Fill = UiKit.AccentBrush };
                    Canvas.SetLeft(dot, x(i) - 3); Canvas.SetTop(dot, y(pts[i]) - 3);
                    canvas.Children.Add(dot);
                }
            stack.Children.Add(canvas);
            return stack;
        }

        private UIElement PlaceBars(List<MatchRecord> matches)
        {
            const double H = 132;
            var stack = new StackPanel();
            stack.Children.Add(Heading(Strings.T("stats.places.title")));
            var counts = Enumerable.Range(1, 8).Select(p => matches.Count(m => m.Place == p)).ToArray();
            int max = Math.Max(1, counts.Max());
            var grid = new Grid { Height = H + 38 };
            for (int p = 1; p <= 8; p++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var col = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(3, 0, 3, 0) };
                col.Children.Add(new TextBlock { Text = counts[p - 1] == 0 ? "" : counts[p - 1].ToString(CultureInfo.InvariantCulture), FontSize = 12, Foreground = UiKit.TextSecondary, HorizontalAlignment = HorizontalAlignment.Center });
                col.Children.Add(new Border
                {
                    Height = Math.Max(2, H * counts[p - 1] / max), CornerRadius = new CornerRadius(4, 4, 0, 0), Margin = new Thickness(0, 3, 0, 0),
                    Background = UiKit.Br(PlaceColor(p)), Opacity = counts[p - 1] == 0 ? 0.25 : 0.9
                });
                col.Children.Add(new TextBlock { Text = p.ToString(CultureInfo.InvariantCulture), FontSize = 12, Foreground = UiKit.TextMuted, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
                Grid.SetColumn(col, p - 1);
                grid.Children.Add(col);
            }
            stack.Children.Add(grid);
            return stack;
        }

        // A hero skin is its own card ("TB_BaconShop_HERO_90_SKIN_G"): stats go to the hero it dresses.
        private static string BaseHero(string cardId) => cardId == null ? "" : Regex.Replace(cardId, "_SKIN_[A-Za-z0-9]+$", "");

        private BgCard HeroCard(string cardId) => _store?.Lookup(BaseHero(cardId)) ?? _store?.Lookup(cardId);
        private string HeroName(string cardId) => HeroCard(cardId)?.DisplayName ?? BaseHero(cardId);

        private Image Art(BgCard card, bool golden, double width)
        {
            var img = new Image { Width = width, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
            if (card == null) return img;
            int decode = (int)(width * 2);
            try
            {
                var hit = CardArt.GetSync(card, golden, decode);
                if (hit != null) img.Source = hit;
                else CardArt.LoadAsync(card, golden, decode).ContinueWith(t =>
                {
                    if (t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && t.Result != null)
                        Dispatcher.BeginInvoke(new Action(() => img.Source = t.Result));
                });
            }
            catch { }
            return img;
        }

        private UIElement Heroes(List<MatchRecord> matches)
        {
            var stack = new StackPanel();
            stack.Children.Add(Heading(Strings.T("stats.heroes.title")));
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            head.Children.Add(Cell(Strings.T("stats.col.hero"), 300, UiKit.TextMuted, TextAlignment.Left, 12));
            foreach (string key in new[] { "stats.col.games", "stats.col.avgPlace", "stats.col.top4", "stats.col.first", "stats.col.netRating" })
                head.Children.Add(Cell(Strings.T(key), 110, UiKit.TextMuted, TextAlignment.Right, 12));
            stack.Children.Add(head);
            foreach (var g in matches.GroupBy(m => BaseHero(m.Hero)).OrderByDescending(g => g.Count()).ThenBy(g => g.Average(m => m.Place)))
            {
                var list = g.ToList();
                var row = new StackPanel { Orientation = Orientation.Horizontal, Height = 34 };
                var name = new StackPanel { Orientation = Orientation.Horizontal, Width = 300 };
                name.Children.Add(new Border { Width = 26, Height = 30, Margin = new Thickness(0, 0, 10, 0), Child = Art(HeroCard(g.Key), false, 26) });
                name.Children.Add(Cell(HeroName(g.Key), 260, null, TextAlignment.Left));
                row.Children.Add(name);
                row.Children.Add(Cell(list.Count.ToString(CultureInfo.InvariantCulture), 110));
                row.Children.Add(Cell(list.Average(m => m.Place).ToString("0.00", CultureInfo.InvariantCulture), 110));
                row.Children.Add(Cell(Percent(list.Count(m => m.Place <= 4), list.Count), 110));
                row.Children.Add(Cell(Percent(list.Count(m => m.Place == 1), list.Count), 110));
                int net = list.Sum(m => m.RatingChange ?? 0);
                row.Children.Add(list.Any(m => m.RatingChange.HasValue) ? Cell(Signed(net), 110, SignBrush(net)) : Cell("–", 110, UiKit.TextMuted));
                stack.Children.Add(new Border { Child = row, BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(0, 1, 0, 0) });
            }
            return stack;
        }

        private UIElement Recent(List<MatchRecord> matches)
        {
            const int Shown = 30;
            var stack = new StackPanel();
            stack.Children.Add(Heading(Strings.T("stats.recent.title")));
            foreach (var m in Enumerable.Reverse(matches).Take(Shown))
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Height = 52 };
                var place = new Border
                {
                    Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = UiKit.Br(PlaceColor(m.Place)), Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = m.Place.ToString(CultureInfo.InvariantCulture), FontSize = 15, FontWeight = FontWeights.Bold, Foreground = UiKit.Br(Color.FromRgb(0x12, 0x16, 0x1E)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                };
                row.Children.Add(place);
                var who = new StackPanel { Width = 196, VerticalAlignment = VerticalAlignment.Center };
                who.Children.Add(new TextBlock { Text = HeroName(m.Hero), FontSize = 13, Foreground = UiKit.TextPrimary, TextTrimming = TextTrimming.CharacterEllipsis });
                who.Children.Add(new TextBlock
                {
                    Text = m.StartUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "  ·  " + Strings.F("stats.recent.rounds", m.Turns.Count),
                    FontSize = 11, Foreground = UiKit.TextMuted, Margin = new Thickness(0, 2, 0, 0)
                });
                row.Children.Add(who);
                row.Children.Add(m.RatingChange.HasValue ? Cell(Signed(m.RatingChange.Value), 64, SignBrush(m.RatingChange.Value)) : Cell("", 64));
                row.Children.Add(Cell(m.RatingAfter.HasValue ? m.RatingAfter.Value.ToString(CultureInfo.InvariantCulture) : "", 62, UiKit.TextSecondary));
                var board = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0) };
                foreach (var minion in m.FinalBoard.Take(7))
                {
                    var card = _store?.Lookup(minion.Card) ?? _store?.Lookup(Regex.Replace(minion.Card ?? "", "_G$", ""));
                    var cell = new StackPanel { Width = 68, VerticalAlignment = VerticalAlignment.Center };
                    cell.Children.Add(new Border { Height = 30, Child = Art(card, minion.Golden, 34), ToolTip = card?.DisplayName ?? minion.Card });
                    cell.Children.Add(new TextBlock
                    {
                        Text = minion.Attack.ToString(CultureInfo.InvariantCulture) + "/" + minion.Health.ToString(CultureInfo.InvariantCulture),
                        FontSize = 11, Foreground = minion.Golden ? UiKit.AccentBrush : UiKit.TextSecondary, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
                    });
                    board.Children.Add(cell);
                }
                row.Children.Add(board);
                stack.Children.Add(new Border { Child = row, BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(0, 1, 0, 0) });
            }
            if (matches.Count > Shown)
                stack.Children.Add(new TextBlock { Text = Strings.F("stats.recent.more", matches.Count - Shown), FontSize = 12, Foreground = UiKit.TextMuted, Margin = new Thickness(0, 10, 0, 0) });
            return stack;
        }

        /// <summary>Test aid: the whole page (not only what the window shows) as a PNG, once its pictures had time to load.</summary>
        public void TestSnap(string path, double waitSeconds = 2.5)
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(waitSeconds) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                try
                {
                    _body.Measure(new Size(PageW - 12, double.PositiveInfinity));
                    int w = (int)PageW, h = (int)Math.Ceiling(_body.DesiredSize.Height) + 4;
                    var shot = new DrawingVisual();
                    using (var dc = shot.RenderOpen())
                    {
                        dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
                        dc.DrawRectangle(new VisualBrush(_body) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, _body.ActualWidth, Math.Max(_body.ActualHeight, 1)));
                    }
                    var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
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
