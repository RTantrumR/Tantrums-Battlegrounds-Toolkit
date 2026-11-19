using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using HsbgCardLookup.Config;
using HsbgCardLookup.Data;
using HsbgCardLookup.Game;
using HsbgCardLookup.Host;
using HsbgCardLookup.Localization;
using HsbgCardLookup.Search;

namespace HsbgCardLookup.Ui
{
    /// <summary>
    /// An opponent's last board at the top-middle of the game: hovering a leaderboard portrait shows it,
    /// a click on the portrait pins it until another one is clicked (or the same one again). Minions are
    /// drawn the way the game draws them in play (oval art in the ring, taunt, divine shield, gems), a
    /// Dark Gift as a small medallion; resting the cursor on a minion of a shown board opens its card
    /// with the board's stats, and its gifts' cards beside it.
    ///
    /// Nothing here takes mouse input: the cursor and the left button are read on the poll, so the
    /// overlay stays click-through and the game keeps every click.
    /// </summary>
    public sealed class OpponentBoardPanel
    {
        // Design units = pixels of a 1080-high game.
        private const double CellW = 124, CellH = 156, Pad = 8, HeaderH = 22;
        // The portrait's oval inside our card renders (512×672), as fractions of the image.
        private static readonly Rect MinionArt = new Rect(154 / 512.0, 71 / 672.0, 216 / 512.0, 264 / 672.0);
        private static readonly Rect SpellArt = new Rect(190 / 512.0, 112 / 672.0, 132 / 512.0, 132 / 672.0);
        private const double CardW = 250;   // the opened card

        private readonly PluginConfig _config;
        private readonly CardStore _store;
        private readonly OpponentBoards _boards;
        private readonly Action<string> _log;
        private readonly Border _root;
        private readonly TextBlock _header;
        private readonly Canvas _row;
        private readonly StackPanel _cards;       // the opened card(s), a canvas child of their own
        private bool _attached;

        /// <summary>Test aid: a cursor position (canvas units) and button state used instead of the real ones.</summary>
        public static Point? TestCursor;
        public static bool TestDown;

        /// <summary>Test aid: draw a fixed sample board (every keyword, long numbers) whatever the game is doing.</summary>
        public static bool TestBoard;
        private OpponentBoard _sample;

        private OpponentBoard Sample()
        {
            if (_sample != null) return _sample;
            var b = new OpponentBoard { PlayerId = -1, Turn = 12 };
            Func<string, int, int, BoardMinion> m = (id, atk, hp) => new BoardMinion { CardId = id, Card = _store.Lookup(id), Attack = atk, Health = hp };
            var a = m("BGS_119", 2, 1); a.DivineShield = true; a.Windfury = true; a.Tier = 1;
            var r = m("BG25_001", 2, 1); r.Taunt = true; r.Reborn = true; r.Tier = 1;
            var g = m("BG29_813", 1700, 1400); g.Golden = true; g.DivineShield = true; g.Tier = 6;
            var k = m("BG27_005", 20, 24); k.Golden = true; k.Taunt = true; k.Deathrattle = true; k.Legendary = true; k.Tier = 3;
            var e = m("BG33_822", 156, 156); e.Stealth = true; e.Venomous = true; e.Tier = 5;
            var d = m("BG36_369", 7, 7); d.Rally = true; d.Legendary = true; d.Tier = 4;
            var gift = _store.All.FirstOrDefault(c => c.Categories != null && c.Categories.Contains("darkgift"));
            if (gift != null) { d.Gifts.Add(gift); e.Gifts.Add(gift); e.Gifts.Add(gift); }
            var x = m("BG31_816", 4, 3); x.Trigger = true; x.Tier = 2;
            b.Minions.AddRange(new[] { a, r, g, k, e, d, x });
            return _sample = b;
        }

        private int _pinned;              // PLAYER_ID, 0 = none
        private bool _wasDown;
        private OpponentBoard _shown;     // what the row currently draws
        private int _shownCount;
        private double _shownScale;
        private BoardMinion _opened;

        private static readonly Brush PanelBg = Frozen(Color.FromArgb(0xD8, 0x0A, 0x0D, 0x14));

        public OpponentBoardPanel(PluginConfig config, CardStore store, OpponentBoards boards, Action<string> log)
        {
            _config = config; _store = store; _boards = boards; _log = log; CardStats.Log = log;
            _header = new TextBlock { Foreground = UiKit.TextSecondary, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
            _row = new Canvas();
            var stack = new StackPanel();
            stack.Children.Add(_header);
            stack.Children.Add(_row);
            _root = new Border
            {
                Background = PanelBg, BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Visibility = Visibility.Collapsed, IsHitTestVisible = false, Child = stack
            };
            _cards = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        }

        public void OnSettingsChanged() { _shown = null; }
        public void OnLanguageChanged() { _shown = null; }
        public void CloseAll() { Hide(); }

        private void Hide()
        {
            if (_root.Visibility != Visibility.Collapsed) _root.Visibility = Visibility.Collapsed;
            if (_cards.Visibility != Visibility.Collapsed) _cards.Visibility = Visibility.Collapsed;
            _opened = null;
            _shown = null;            // shown again later: drawn afresh
        }

        /// <summary>About every 100 ms, on the UI thread.</summary>
        public void Poll()
        {
            try
            {
                var g = HostServices.Game;
                var canvas = HostServices.Overlay?.Canvas;
                if (TestBoard && canvas != null && canvas.ActualHeight > 0)
                {
                    EnsureArt(canvas);
                    Show(canvas, Sample(), "Sample board");
                    OpenCardUnder(canvas, TestCursor ?? new Point(-1, -1));
                    return;
                }
                if (!_config.ShowOpponentBoards || ArrangeSession.IsActive || g == null || canvas == null || !g.IsBattlegroundsMatch
                    || canvas.ActualHeight <= 0)
                { Hide(); _pinned = 0; return; }

                EnsureArt(canvas);
                Point cursor; bool haveCursor = Cursor(canvas, out cursor);
                bool down = (GetAsyncKeyState(0x01) & 0x8000) != 0;      // VK_LBUTTON
                if (TestCursor.HasValue) { cursor = TestCursor.Value; haveCursor = true; down = TestDown; }
                bool click = down && !_wasDown;
                _wasDown = down;

                // Which portrait the mouse is on: the game's own answer when the host can read it,
                // else the solo tile geometry.
                int hovered = 0;
                int overHero = TestCursor.HasValue ? -1 : (HostServices.Memory?.HoveredLeaderboardHero() ?? -1);
                if (overHero > 0) hovered = g.Entity(overHero)?.GetTag(Tags.PLAYER_ID) ?? 0;
                else if (overHero < 0 && haveCursor && !g.IsBattlegroundsDuosMatch)
                {
                    int next = OpponentBoards.NextOpponent(g);
                    var slots = Slots(g);
                    for (int i = 0; i < slots.Count; i++)
                        if (LeaderboardOverlay.PortraitRect(canvas, i, slots[i] == next).Contains(cursor)) { hovered = slots[i]; break; }
                }
                if (click && hovered != 0) _pinned = _pinned == hovered ? 0 : hovered;

                int pid = hovered != 0 ? hovered : _pinned;
                var board = pid == 0 ? null : _boards.For(pid);
                if (board == null) { Hide(); return; }

                Show(canvas, board, HeroOf(g, pid));
                OpenCardUnder(canvas, haveCursor ? cursor : new Point(-1, -1));
            }
            catch { }
        }

        // A match is on: have the game's own art read in the background, well before a board is shown.
        private void EnsureArt(Canvas canvas)
        {
            if (Assets.GameArt.Started || _artTried) return;
            _artTried = true;
            string dir = HostServices.Shell?.HearthstoneDataDirectory();
            if (dir == null) return;
            var ids = _store.All.Where(c => !string.IsNullOrEmpty(c.ExternalId)
                && (c.CardType == "minion" || (c.Categories != null && c.Categories.Contains("darkgift")))).Select(c => c.ExternalId).ToList();
            Assets.GameArt.UseFontFile(System.IO.Path.Combine(PluginConfig.DataDir, "game-numbers.ttf"));
            Assets.GameArt.Start(dir, ids, System.IO.Path.Combine(PluginConfig.DataDir, "game-art.txt"), canvas.Dispatcher, _log);
        }
        private bool _artTried;

        // PLAYER_IDs in the order the leaderboard draws them (by place; a ghost copy can repeat a place).
        private static List<int> Slots(IGameState g)
        {
            var byPlace = new SortedDictionary<int, KeyValuePair<bool, int>>();
            foreach (var e in g.Entities())
            {
                int place = e.GetTag(Tags.PLAYER_LEADERBOARD_PLACE);
                if (place <= 0 || string.IsNullOrEmpty(e.CardId)) continue;
                int pid = e.GetTag(Tags.PLAYER_ID);
                KeyValuePair<bool, int> had;
                if (!byPlace.TryGetValue(place, out had) || (e.IsInPlay && !had.Key)) byPlace[place] = new KeyValuePair<bool, int>(e.IsInPlay, pid);
            }
            return byPlace.Values.Select(v => v.Value).ToList();
        }

        private string HeroOf(IGameState g, int pid)
        {
            foreach (var e in g.Entities())
                if (e.GetTag(Tags.PLAYER_LEADERBOARD_PLACE) > 0 && e.GetTag(Tags.PLAYER_ID) == pid && !string.IsNullOrEmpty(e.CardId))
                {
                    string id = e.CardId;
                    int skin = id.IndexOf("_SKIN_", StringComparison.Ordinal);
                    var c = _store.Lookup(id) ?? (skin > 0 ? _store.Lookup(id.Substring(0, skin)) : null);
                    if (c != null) return c.DisplayName;
                }
            return null;
        }

        private static bool Cursor(Canvas canvas, out Point p)
        {
            p = default(Point);
            try
            {
                POINT c;
                if (!GetCursorPos(out c)) return false;
                p = canvas.PointFromScreen(new Point(c.X, c.Y));
                return true;
            }
            catch { return false; }
        }

        // ── The board ───────────────────────────────────────────────────────────────────────────

        private void Show(Canvas canvas, OpponentBoard board, string hero)
        {
            if (!_attached)
            {
                canvas.Children.Add(_root);
                canvas.Children.Add(_cards);
                _root.SizeChanged += (s, e) => Place(canvas);
                _attached = true;
            }
            double scale = Math.Max(0.6, Math.Min(canvas.ActualHeight / 1080.0, 2.0));
            // A fight's minions arrive over a read or two: redraw when the count grows.
            if (!ReferenceEquals(board, _shown) || board.Minions.Count != _shownCount || scale != _shownScale)
            {
                _shown = board; _shownCount = board.Minions.Count; _shownScale = scale; _opened = null;
                _cards.Visibility = Visibility.Collapsed;
                _header.Text = hero == null ? Strings.F("boards.headerTurn", board.Turn) : Strings.F("boards.header", hero, board.Turn);
                _header.FontSize = 13 * scale;
                _header.Height = HeaderH * scale;
                _root.Padding = new Thickness(Pad * scale, 4 * scale, Pad * scale, 4 * scale);
                _row.Children.Clear();
                _row.Width = board.Minions.Count * CellW * scale;
                _row.Height = CellH * scale;
                for (int i = 0; i < board.Minions.Count; i++)
                {
                    var cell = Minion(board.Minions[i], scale);
                    Canvas.SetLeft(cell, i * CellW * scale);
                    _row.Children.Add(cell);
                }
            }
            if (_root.Visibility != Visibility.Visible) _root.Visibility = Visibility.Visible;
            Place(canvas);
        }

        private void Place(Canvas canvas)
        {
            Canvas.SetLeft(_root, Math.Max(0, (canvas.ActualWidth - _root.ActualWidth) / 2));
            Canvas.SetTop(_root, canvas.ActualHeight * 0.006);
        }

        // One minion, drawn from the game's own parts.
        //
        // data/board/*.png are HS_CardExtractor captures of a board minion with everything but one part
        // switched off (tools/BoardCaptures/pieces.json): the ring, the golden ring, the legendary dragon,
        // taunt shields, tier badges, gems, effects. Every capture is the same 1024×1345 frame scaled to
        // 307×404, so each layer is drawn at the same place and size and the parts meet as they do in the
        // game. Only the art (the game's own portrait texture, clipped to the oval the game clips it to)
        // and the numbers are ours. Units below are capture pixels × 0.15.
        private const double LayerW = 153.6, RingMidX = 81.7, LayerTop = -27;

        private FrameworkElement Minion(BoardMinion m, double s)
        {
            var c = new Canvas { Width = CellW * s, Height = CellH * s };
            double left = CellW / 2 - RingMidX;                      // the ring's middle on the cell's middle
            Action<string> layer = name => Add(c, Sprite(name, LayerW, s), left, LayerTop, s);

            if (m.Taunt) layer(m.Golden ? "taunt-gold" : "taunt");

            // The portrait's oval, measured from a capture of the art alone (normal / golden frame).
            double ax = m.Golden ? 83.25 : 83.1, ay = m.Golden ? 106.1 : 104.1, rx = m.Golden ? 36.9 : 38.0, ry = m.Golden ? 50.5 : 51.2;
            var art = new Ellipse { Width = 2 * rx * s, Height = 2 * ry * s, Fill = UiKit.Br(Color.FromRgb(0x1A, 0x21, 0x30)) };
            Add(c, art, left + ax - rx, LayerTop + ay - ry, s);
            LoadArt(art, m.Card, m.Golden, MinionArt);

            if (m.Stealth) layer("stealth");
            layer(m.Golden ? "frame-gold" : "frame");
            if (m.Legendary) layer(m.Golden ? "dragon-gold" : "dragon");
            if (m.Reborn) layer("reborn");
            if (m.DivineShield) layer("divine-shield");
            if (m.Tier >= 1 && m.Tier <= 7) layer("badge-" + m.Tier);
            layer("gem-attack"); layer("gem-health");
            // One spot under the oval; the game shows a single icon there, so the first that applies.
            if (m.Venomous) layer(m.Poisonous ? "poisonous" : "venomous");
            else if (m.Deathrattle) layer("deathrattle");
            else if (m.Rally) layer("rally");
            else if (m.Trigger) layer("trigger");
            if (m.Windfury) layer("windfury");

            if (m.Gifts.Count > 0)
            {
                var gift = new Ellipse { Width = 23 * s, Height = 23 * s, Fill = UiKit.Br(Color.FromRgb(0x2A, 0x10, 0x14)) };
                Add(c, gift, left + 104.8, LayerTop + 48.2, s);
                LoadArt(gift, m.Gifts[0], false, SpellArt);
                Add(c, Sprite("dark-gift-ring", 34, s), left + 99, LayerTop + 43, s);
                if (m.Gifts.Count > 1) Add(c, new OutlinedText("x" + m.Gifts.Count, 12 * s, Brushes.White), left + 122, LayerTop + 72, s);
            }

            // The digits' centres in the game's own render; drawn a third larger than the game does (owner).
            AddCentred(c, new OutlinedText(Short(m.Attack), 27 * s, Brushes.White), left + 56.1, LayerTop + 141.2, s);
            AddCentred(c, new OutlinedText(Short(m.Health), 27 * s, Brushes.White), left + 111.1, LayerTop + 141.0, s);
            return c;
        }

        private static void AddCentred(Canvas c, FrameworkElement el, double x, double y, double s)
        {
            Canvas.SetLeft(el, x * s - el.Width / 2); Canvas.SetTop(el, y * s - el.Height / 2);
            c.Children.Add(el);
        }

        private static void Add(Canvas c, UIElement el, double x, double y, double s)
        {
            Canvas.SetLeft(el, x * s); Canvas.SetTop(el, y * s);
            c.Children.Add(el);
        }

        private static Image Sprite(string name, double width, double s)
        {
            var img = new Image { Stretch = Stretch.Uniform, Width = width * s };
            try { img.Source = ImageCache.Load(CardStore.BoardAssetPath(name), 0); } catch { }
            return img;
        }

        // The game's own shortening of big stats: at most three digits, "1.7k", "10k", "2.8m".
        // (English letters; the game localizes them, we don't yet.)
        internal static string Short(int n)
        {
            if (n < 1000) return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            double v = n / 1000.0; string unit = "k";
            if (v >= 1000) { v /= 1000.0; unit = "m"; }
            if (v >= 10) return ((int)Math.Floor(v)).ToString(System.Globalization.CultureInfo.InvariantCulture) + unit;
            double tenths = Math.Floor(v * 10) / 10;
            return tenths.ToString(tenths == Math.Floor(tenths) ? "0" : "0.0", System.Globalization.CultureInfo.InvariantCulture) + unit;
        }

        // The card's own art from the game's files, fitted by its painted region; until that answers
        // (or when the game has none for the card) the portrait cut out of our card render.
        private static void LoadArt(Shape target, BgCard card, bool golden, Rect renderCrop)
        {
            if (card == null) return;
            Assets.GameArt.Request(card.ExternalId, pic =>
            {
                if (pic != null)
                {
                    target.Fill = new ImageBrush(pic.Image) { Viewbox = pic.Painted, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Stretch = Stretch.UniformToFill };
                    return;
                }
                Action<BitmapSource> set = bmp =>
                {
                    if (bmp != null) target.Fill = new ImageBrush(bmp) { Viewbox = renderCrop, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Stretch = Stretch.UniformToFill };
                };
                var hit = CardArt.GetSync(card, golden, 0);
                if (hit != null) { set(hit); return; }
                var ui = target.Dispatcher;
                CardArt.LoadAsync(card, golden, 0).ContinueWith(t =>
                {
                    if (t.Status != System.Threading.Tasks.TaskStatus.RanToCompletion || t.Result == null) return;
                    ui.BeginInvoke(new Action(() => set(t.Result)));
                });
            });
        }

        // ── The opened card ─────────────────────────────────────────────────────────────────────

        private void OpenCardUnder(Canvas canvas, Point cursor)
        {
            BoardMinion over = null;
            int index = -1;
            if (_shown != null && _row.IsVisible)
            {
                try
                {
                    var origin = _row.TransformToAncestor(canvas).Transform(new Point(0, 0));
                    double w = CellW * _shownScale;
                    if (cursor.Y >= origin.Y && cursor.Y <= origin.Y + CellH * _shownScale && cursor.X >= origin.X)
                    {
                        index = (int)((cursor.X - origin.X) / w);
                        if (index >= 0 && index < _shown.Minions.Count) over = _shown.Minions[index];
                    }
                    if (over == null) { _cards.Visibility = Visibility.Collapsed; _opened = null; return; }
                    if (!ReferenceEquals(over, _opened))
                    {
                        _opened = over;
                        _cards.Children.Clear();
                        _cards.Children.Add(Card(over.Card, over.Golden, over, _shownScale));
                        foreach (var gift in over.Gifts) _cards.Children.Add(Card(gift, false, null, _shownScale));
                        _cards.Visibility = Visibility.Visible;
                    }
                    double total = CardW * _shownScale * _cards.Children.Count;
                    double left = origin.X + (index + 0.5) * w - CardW * _shownScale / 2;
                    Canvas.SetLeft(_cards, Math.Max(0, Math.Min(left, canvas.ActualWidth - total)));
                    Canvas.SetTop(_cards, origin.Y + CellH * _shownScale + 6 * _shownScale);
                }
                catch { }
            }
        }

        // A card render; for a board minion the render's own digits are replaced by the board's numbers
        // (CardStats), so the gems, the frame and everything else are the render's pixels.
        private static FrameworkElement Card(BgCard card, bool golden, BoardMinion stats, double s)
        {
            double w = CardW * s, h = w * 672.0 / 512.0;
            var img = new Image { Width = w, Height = h, Stretch = Stretch.Uniform, IsHitTestVisible = false };
            if (card == null) return img;
            Action<BitmapSource> set = render =>
            {
                if (render == null) return;
                if (stats == null) { img.Source = render; return; }
                int baseAtk = (golden ? card.AttackGold : card.Attack) ?? stats.Attack, baseHp = (golden ? card.HealthGold : card.Health) ?? stats.Health;
                img.Source = CardStats.WithNumbers(render, Short(stats.Attack), Short(stats.Health), baseAtk.ToString().Length, baseHp.ToString().Length,
                    Brushes.White, Brushes.White);
            };
            var hit = CardArt.GetSync(card, golden, 0);
            if (hit != null) set(hit);
            else
            {
                var ui = img.Dispatcher;
                CardArt.LoadAsync(card, golden, 0).ContinueWith(t =>
                {
                    if (t.Status != System.Threading.Tasks.TaskStatus.RanToCompletion || t.Result == null) return;
                    ui.BeginInvoke(new Action(() => set(t.Result)));
                });
            }
            return img;
        }

        private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        /// <summary>A number in the game's face with a black outline. The element is exactly the glyphs'
        /// box, so centring the element centres the digits themselves.</summary>
        private sealed class OutlinedText : FrameworkElement
        {
            private readonly Geometry _shape;
            private readonly Brush _fill;
            private readonly Pen _pen;

            public OutlinedText(string text, double size, Brush fill)
            {
                _shape = Assets.GameArt.NumberOutline(text, size);
                var box = _shape.Bounds;
                _shape.Freeze();
                _fill = fill;
                _pen = new Pen(Brushes.Black, Math.Max(1.5, size * 0.14)) { LineJoin = PenLineJoin.Round };
                _pen.Freeze();
                Width = box.Width; Height = box.Height;
                IsHitTestVisible = false;
            }

            protected override void OnRender(DrawingContext dc)
            {
                dc.DrawGeometry(null, _pen, _shape);
                dc.DrawGeometry(_fill, null, _shape);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}
