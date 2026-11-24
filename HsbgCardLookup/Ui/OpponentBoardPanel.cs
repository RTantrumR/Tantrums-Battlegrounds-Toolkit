using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    /// In a match the board itself takes no mouse input: the cursor and the left button are read on
    /// the poll, so the overlay stays click-through and the game keeps every click. The one exception
    /// is the corner arrow of a PINNED board (shown while the cursor is on the board): dragging it
    /// scales the board about the middle of its top edge. Moving the board is for arranging from
    /// Settings, where the whole box takes the mouse: drag to move, the arrow to scale
    /// (config <c>OpponentBoardsHud</c>; unset = hugging the top edge, centred).
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
        private readonly TextBlock _header;      // whose board and from which turn: a line UNDER the board
        private readonly Canvas _deity;          // the Deity's cell, left of the warband (no width without one)
        private readonly Canvas _history;        // tavern tiers: the turn each was reached, triples made on it
        private const double HistW = 78, SideGap = 12;
        // The box never changes size with what it shows: seven minion places (the warband centred in
        // them), a place for the Deity in a lobby that has one, three columns of tiers.
        private const int Places = 7;
        private double _rowShift;                // where the first minion starts inside the row
        // A player we have not fought: no warband to show, the tiers and triples all the same.
        private readonly Dictionary<int, OpponentBoard> _noBoard = new Dictionary<int, OpponentBoard>();
        private string _shownSide;
        private readonly Canvas _row;
        private readonly StackPanel _cards;       // the opened card(s), a canvas child of their own
        private readonly Grid _grip;              // takes the mouse while arranging, collapsed otherwise
        private readonly Border _handle;          // the size arrow: while arranging, and on a pinned board
        // What the box shows now, for drawing it again at another size.
        private OpponentBoard _lastBoard;
        private string _lastHero;
        private PlayerHistory _lastHistory;
        private bool _attached;
        private const double MinScale = 0.3, MaxScale = 4.0;   // of the settled (pixel) scale
        private bool _settled;                                  // _userScale already holds the window's share
        private double _xf = 0.5, _yf = 0, _userScale = 1;
        private bool _arranging, _dragging, _resizing, _moved;
        private Point _startCursor;
        private double _startLeft, _startTop, _startScale;

        /// <summary>The game's code of the language the card renders are in ("enUS", "jaJP").</summary>
        public static Func<string> TextLanguage;

        /// <summary>Test aid: a cursor position (canvas units) and button state used instead of the real ones.</summary>
        public static Point? TestCursor;
        public static bool TestDown;

        /// <summary>Test aid: draw a fixed sample board (every keyword, long numbers) whatever the game is doing.</summary>
        public static bool TestBoard;
        private OpponentBoard _sample;
        private PlayerHistory _sampleHistory;

        private OpponentBoard Sample()
        {
            if (_sample != null) return _sample;
            var b = new OpponentBoard { PlayerId = -1, Turn = 12 };
            Func<string, int, int, BoardMinion> m = (id, atk, hp) => new BoardMinion { CardId = id, Card = _store.Lookup(id), Attack = atk, Health = hp };
            var a = m("BG36_364", 12, 9); a.DivineShield = true; a.Windfury = true; a.Tier = 5; a.Script[0] = 3560; a.Script[1] = 2670;
            var r = m("BG25_001", 2, 1); r.Taunt = true; r.Reborn = true; r.Tier = 1;
            var g = m("BG29_813", 1700, 1400); g.Golden = true; g.DivineShield = true; g.Tier = 6;
            var k = m("BG27_005", 20, 24); k.Golden = true; k.Taunt = true; k.Deathrattle = true; k.Legendary = true; k.Tier = 3;
            var e = m("BG33_822", 156, 156); e.Stealth = true; e.Venomous = true; e.Tier = 5;
            var d = m("BG36_369", 7, 7); d.Rally = true; d.Legendary = true; d.Tier = 4;
            var gift = _store.All.FirstOrDefault(c => c.Categories != null && c.Categories.Contains("darkgift"));
            if (gift != null) { d.Gifts.Add(gift); e.Gifts.Add(gift); e.Gifts.Add(gift); }
            var x = m("BG31_816", 4, 3); x.Trigger = true; x.Tier = 2; x.AltText = 1; x.Script[0] = 14; x.Script[1] = 9;
            var h = m("BG36_364", 24, 18); h.Golden = true; h.Tier = 5; h.TextId = "BG36_364_G"; h.Script[0] = 1200; h.Script[1] = 1050;
            try { foreach (string id in new[] { "BG36_364", "BG36_364_G", "BG31_816" }) HostServices.Memory?.CardText(id); } catch { }
            b.Minions.AddRange(new[] { a, r, h, k, e, d, x });
            BgCard deity;
            if (_store.ById.TryGetValue(132532, out deity) && deity != null)
                b.Deity = new BoardMinion { CardId = deity.ExternalId, TextId = deity.ExternalId, Card = deity, Attack = 21, Health = 11, Legendary = true, Tier = deity.Tier ?? 0, KeepText = true };
            _sampleHistory = new PlayerHistory { Tier = 5 };
            foreach (var t in new[] { new[] { 1, 1, 0 }, new[] { 2, 2, 1 }, new[] { 3, 5, 0 }, new[] { 4, 7, 2 }, new[] { 5, 10, 1 } })
            { _sampleHistory.TierTurn[t[0]] = t[1]; _sampleHistory.Triples[t[0]] = t[2]; }
            return _sample = b;
        }

        private int _pinned;              // PLAYER_ID, 0 = none
        // A click is caught by a fast timer of its own (the 100 ms poll misses a short one) and waits a
        // moment for the game to say which portrait the mouse is on: that answer comes with the memory
        // reader's next walk, a quarter of a second behind the mouse.
        private bool _wasDown, _testWasDown;
        private DateTime _clickAt;
        private Point _clickPos;
        private System.Windows.Threading.DispatcherTimer _clicks;

        private void WatchClicks(Canvas canvas, bool on)
        {
            if (!on) { if (_clicks != null && _clicks.IsEnabled) _clicks.Stop(); return; }
            if (_clicks == null)
            {
                _clicks = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Input, canvas.Dispatcher)
                { Interval = TimeSpan.FromMilliseconds(20) };
                _clicks.Tick += (s, e) =>
                {
                    try
                    {
                        short state = GetAsyncKeyState(0x01);              // VK_LBUTTON
                        bool down = (state & 0x8000) != 0;
                        // Down now and not before, or pressed and let go between two looks.
                        bool pressed = (down && !_wasDown) || (!down && !_wasDown && (state & 1) != 0);
                        _wasDown = down;
                        Point at;
                        var c = HostServices.Overlay?.Canvas;
                        if (pressed && c != null && Cursor(c, out at)) { _clickAt = DateTime.UtcNow; _clickPos = at; }
                    }
                    catch { }
                };
            }
            if (!_clicks.IsEnabled) { _wasDown = (GetAsyncKeyState(0x01) & 0x8000) != 0; _clicks.Start(); }
        }
        private OpponentBoard _shown;     // what the row currently draws
        private int _shownCount;
        private double _shownScale;
        private BoardMinion _opened;

        private static readonly Brush PanelBg = Frozen(Color.FromArgb(0xD8, 0x0A, 0x0D, 0x14));

        /// <summary>The pieces of the settings demonstration, at the size they have over a 1080-high game:
        /// the sample board as the panel draws it, the big card that opens over one of its minions, that
        /// minion's centre in the board and the top edge the card opens at (both in board coordinates,
        /// the same rule as <see cref="OpenCardUnder"/>).</summary>
        internal static FrameworkElement DemoBoard(PluginConfig config, CardStore store, int minion,
            out FrameworkElement card, out Point minionCentre, out double cardTop)
        {
            var p = new OpponentBoardPanel(config, store, null, null, preview: true);
            p.EnsureArt(System.Windows.Threading.Dispatcher.CurrentDispatcher);
            p.EnsureTextFace();
            var board = p.Sample();
            p.Fill(board, Strings.T("boards.sample"), p._sampleHistory, 1);
            p._root.Visibility = Visibility.Visible;
            p._root.IsHitTestVisible = false;
            // Hanging from the top edge, as Place draws it there: no top border, no top corners.
            p._root.CornerRadius = new CornerRadius(0, 0, 8, 8);
            p._root.BorderThickness = new Thickness(1, 0, 1, 1);
            p._root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            p._root.Arrange(new Rect(p._root.DesiredSize));
            minion = Math.Max(0, Math.Min(minion, board.Minions.Count - 1));
            Point origin = p._row.TranslatePoint(new Point(0, 0), p._root);
            minionCentre = new Point(origin.X + p._rowShift + (minion + 0.5) * CellW, origin.Y + CellH / 2);
            cardTop = origin.Y + CellH + HeaderH + 6;
            var m = board.Minions.Count > 0 ? board.Minions[minion] : null;
            card = m == null ? (FrameworkElement)new Canvas() : Card(m.Card, m.Golden, m, 1);
            return p._root;
        }

        public OpponentBoardPanel(PluginConfig config, CardStore store, OpponentBoards boards, Action<string> log, bool preview = false)
        {
            _config = config; _store = store; _boards = boards; _log = log ?? (s => { });
            if (!preview) { CardStats.Log = log; CardTextPaint.Log = log; }
            _header = new TextBlock { Foreground = UiKit.TextSecondary, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
            _row = new Canvas();
            _deity = new Canvas();
            _history = new Canvas();
            var body = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            body.Children.Add(_deity);
            body.Children.Add(_row);
            body.Children.Add(_history);
            var stack = new StackPanel();
            stack.Children.Add(body);
            stack.Children.Add(_header);
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
                Margin = new Thickness(0, 2, 2, 0), Cursor = Cursors.SizeNESW, Child = arrow,
                Visibility = Visibility.Collapsed
            };
            _grip = new Grid { Background = Brushes.Transparent, Visibility = Visibility.Collapsed, Cursor = Cursors.SizeAll };
            _handle.MouseLeftButtonDown += (s, e) => { e.Handled = true; BeginGesture(true, e); };
            _grip.MouseLeftButtonDown += (s, e) => { e.Handled = true; BeginGesture(false, e); };
            stack.IsHitTestVisible = false;
            var grid = new Grid();
            grid.Children.Add(stack);
            grid.Children.Add(_grip);
            grid.Children.Add(_handle);          // over the grip: the arrow scales, the rest of the box moves
            _root = new Border
            {
                Background = PanelBg, BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Visibility = Visibility.Collapsed, Child = grid
            };
            if (!preview) { try { HostServices.Overlay?.SetHitTestVisible(_grip); HostServices.Overlay?.SetHitTestVisible(_handle); } catch { } }
            LoadPlacement();
            _cards = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        }

        public void OnSettingsChanged() { _shown = null; }

        private void LoadPlacement()
        {
            _xf = 0.5; _yf = 0; _userScale = 1; _settled = false;
            var p = _config.OpponentBoardsHud;
            if (p != null && p.Set && p.WF > 0) { _xf = p.XF; _yf = p.YF; _userScale = Math.Max(0.5, Math.Min(p.WF, 2.0)); }
            if (p != null && p.Px > 0) { _userScale = Math.Max(MinScale, Math.Min(p.Px, MaxScale)); _settled = true; }
        }

        /// <summary>The saved place and size were replaced wholesale (a config import, a preset).</summary>
        public void ReloadPlacement() { LoadPlacement(); _shown = null; }
        public void OnLanguageChanged() { _shown = null; }
        public void CloseAll() { EndGesture(false); RemoveHook(); Hide(); }

        private void Hide()
        {
            if (_root.Visibility != Visibility.Collapsed) _root.Visibility = Visibility.Collapsed;
            if (_cards.Visibility != Visibility.Collapsed) _cards.Visibility = Visibility.Collapsed;
            if (!_resizing) Arrow(false);
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
                    EnsureArt(canvas.Dispatcher); EnsureTextFace();
                    Show(canvas, Sample(), Strings.T("boards.sample"), _sampleHistory);
                    OpenCardUnder(canvas, TestCursor ?? new Point(-1, -1));
                    return;
                }
                bool arranging = ArrangeSession.Active == ArrangeTarget.Boards && canvas != null && canvas.ActualHeight > 0;
                if (arranging != _arranging)
                {
                    _arranging = arranging;
                    _grip.Visibility = arranging ? Visibility.Visible : Visibility.Collapsed;
                    if (!arranging) { EndGesture(false); Hide(); }
                    Arrow(arranging);
                }
                if (arranging)
                {
                    EnsureArt(canvas.Dispatcher); EnsureTextFace();
                    Show(canvas, Sample(), Strings.T("boards.sample"), _sampleHistory);
                    return;
                }
                if (!_config.ShowOpponentBoards || ArrangeSession.IsActive || g == null || canvas == null || !g.IsBattlegroundsMatch
                    || canvas.ActualHeight <= 0)
                { Hide(); _pinned = 0; WatchClicks(null, false); return; }
                WatchClicks(canvas, _config.BoardPinOnClick && !TestCursor.HasValue);

                EnsureArt(canvas.Dispatcher); EnsureTextFace();
                Point cursor; bool haveCursor = Cursor(canvas, out cursor);
                var now = DateTime.UtcNow;
                if (TestCursor.HasValue)
                {
                    cursor = TestCursor.Value; haveCursor = true;
                    if (TestDown && !_testWasDown) { _clickAt = now; _clickPos = cursor; }
                    _testWasDown = TestDown;
                }
                // A click stays good for half a second, as long as the mouse has not left the spot.
                bool click = _clickAt != default(DateTime) && (now - _clickAt).TotalMilliseconds < 500
                    && haveCursor && (cursor - _clickPos).Length < 40;
                if (!click) _clickAt = default(DateTime);

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
                // A press on the size arrow is the arrow's own: it neither pins nor lets a board go.
                if (_resizing || (haveCursor && OverArrow(canvas, cursor))) { click = false; _clickAt = default(DateTime); }
                if (!_config.BoardPinOnClick) { _pinned = 0; _clickAt = default(DateTime); }
                else if (click && hovered != 0) { _pinned = _pinned == hovered ? 0 : hovered; _clickAt = default(DateTime); }
                else if (click && _pinned != 0 && OnBoard(canvas, cursor)) { _pinned = 0; _clickAt = default(DateTime); }   // a click on the board lets it go

                int pid = hovered != 0 ? hovered : _pinned;
                var board = pid == 0 ? null : _boards.For(pid);
                var history = pid == 0 ? null : _boards.HistoryOf(pid);
                if (board == null && history != null && !_noBoard.TryGetValue(pid, out board))
                    _noBoard[pid] = board = new OpponentBoard { PlayerId = pid };
                if (board == null) { Hide(); return; }

                Show(canvas, board, HeroOf(g, pid), history);
                // The size arrow: on the pinned board, while the cursor is on it (or dragging the arrow).
                Arrow(_resizing || (_pinned != 0 && pid == _pinned && haveCursor && OnBoard(canvas, cursor)));
                if (_resizing) return;
                OpenCardUnder(canvas, haveCursor ? cursor : new Point(-1, -1));
            }
            catch (Exception ex)
            {
                if (_failed) return;
                _failed = true;          // once: a fault here would otherwise repeat ten times a second
                try { _log("[boards] panel: " + ex); } catch { }
            }
        }
        private bool _failed;

        private void Arrow(bool show)
        {
            var want = show ? Visibility.Visible : Visibility.Collapsed;
            if (_handle.Visibility != want) _handle.Visibility = want;
        }

        private bool OverArrow(Canvas canvas, Point cursor)
        {
            if (!_handle.IsVisible || _handle.ActualWidth <= 0) return false;
            try { return _handle.TransformToAncestor(canvas).TransformBounds(new Rect(0, 0, _handle.ActualWidth, _handle.ActualHeight)).Contains(cursor); }
            catch { return false; }
        }

        private bool OnBoard(Canvas canvas, Point cursor)
        {
            if (_root.Visibility != Visibility.Visible || _root.ActualWidth <= 0) return false;
            try { return _root.TransformToAncestor(canvas).TransformBounds(new Rect(0, 0, _root.ActualWidth, _root.ActualHeight)).Contains(cursor); }
            catch { return false; }
        }

        // A match is on: have the game's own art read in the background, well before a board is shown.
        private void EnsureArt(System.Windows.Threading.Dispatcher ui)
        {
            if (Assets.GameArt.Started || _artTried) return;
            _artTried = true;
            string dir = HostServices.Shell?.HearthstoneDataDirectory();
            if (dir == null) return;
            var ids = _store.All.Where(c => !string.IsNullOrEmpty(c.ExternalId)
                && (c.CardType == "minion" || (c.Categories != null && c.Categories.Contains("darkgift")))).Select(c => c.ExternalId).ToList();
            Assets.GameArt.UseFontFile(System.IO.Path.Combine(PluginConfig.DataDir, "game-numbers.ttf"));
            Assets.GameArt.Start(dir, ids, System.IO.Path.Combine(PluginConfig.DataDir, "game-art.txt"), ui, _log);
        }
        private bool _artTried;
        private string _faceAsked;

        // The text face of the cards' language is copied out of the game's files ahead of the first card.
        private void EnsureTextFace()
        {
            string language = null;
            try { language = TextLanguage?.Invoke(); } catch { }
            if (language == null || language == _faceAsked || !Assets.GameArt.Started) return;
            _faceAsked = language;
            Assets.GameArt.TextFaceFor(language);
        }

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

        private void Show(Canvas canvas, OpponentBoard board, string hero, PlayerHistory history)
        {
            if (!_attached)
            {
                canvas.Children.Add(_root);
                canvas.Children.Add(_cards);
                _root.SizeChanged += (s, e) => Place(canvas);
                _attached = true;
            }
            // The size is settled once, from the game window's height at the first showing, then kept.
            if (!_settled)
            {
                _userScale = Math.Round(_userScale * Math.Max(0.6, Math.Min(canvas.ActualHeight / 1080.0, 2.0)), 3);
                _settled = true;
                try
                {
                    var hud = _config.OpponentBoardsHud ?? (_config.OpponentBoardsHud = new HudPlacement());
                    hud.Px = _userScale; _config.Save();
                }
                catch { }
            }
            _lastBoard = board; _lastHero = hero; _lastHistory = history;
            Fill(board, hero, history, _userScale);
            if (_root.Visibility != Visibility.Visible) _root.Visibility = Visibility.Visible;
            Place(canvas);
        }

        private void Fill(OpponentBoard board, string hero, PlayerHistory history, double scale)
        {
            // A fight's minions arrive over a read or two: redraw when the count grows.
            string side = (board.Deity == null ? "" : board.Deity.Attack + "/" + board.Deity.Health) + "#" + history?.Signature();
            if (!ReferenceEquals(board, _shown) || board.Minions.Count != _shownCount || scale != _shownScale || side != _shownSide)
            {
                _shownSide = side;
                Sides(board, history, scale);
                _shown = board; _shownCount = board.Minions.Count; _shownScale = scale; _opened = null;
                _cards.Visibility = Visibility.Collapsed;
                _header.Text = board.Minions.Count == 0 && board.Deity == null ? Strings.F("boards.headerNone", hero ?? "")
                    : hero == null ? Strings.F("boards.headerTurn", board.Turn) : Strings.F("boards.header", hero, board.Turn);
                _header.FontSize = 13 * scale;
                _header.Height = HeaderH * scale;
                _root.Padding = new Thickness(Pad * scale, 4 * scale, Pad * scale, 2 * scale);
                _row.Children.Clear();
                int places = Math.Max(Places, board.Minions.Count);
                _row.Width = places * CellW * scale;
                _rowShift = (places - board.Minions.Count) * CellW * scale / 2;
                _row.Height = CellH * scale;
                for (int i = 0; i < board.Minions.Count; i++)
                {
                    var cell = Minion(board.Minions[i], scale);
                    Canvas.SetLeft(cell, _rowShift + i * CellW * scale);
                    _row.Children.Add(cell);
                }
            }
        }

        // Left of the warband: the Deity as one more minion, behind a thin line. Right of it: one cell per
        // tavern tier with the turn it was reached on and the triples made while on it; a tier not
        // reached yet is only its dimmed icon.
        private void Sides(OpponentBoard board, PlayerHistory history, double s)
        {
            _deity.Children.Clear();
            _deity.Height = CellH * s;
            // Only for a player we have fought, in a lobby with Aberrations: the place costs width.
            bool deityPlace = board.Deity != null || (_boards != null && _boards.LobbyHasDeity && board.Minions.Count > 0);
            _deity.Width = deityPlace ? (CellW + SideGap) * s : 0;
            if (deityPlace && board.Deity == null) Add(_deity, Line(s), CellW + SideGap / 2, CellH * 0.12, s);
            if (board.Deity != null)
            {
                _deity.Children.Add(Minion(board.Deity, s));
                Add(_deity, Line(s), CellW + SideGap / 2, CellH * 0.12, s);
            }

            _history.Children.Clear();
            _history.Height = CellH * s;
            int tiers = Math.Min(7, Math.Max(6, history?.Tier ?? 0)), cols = (tiers + 1) / 2;
            double cellH = CellH / 2;
            _history.Width = (SideGap + cols * HistW) * s;
            Add(_history, Line(s), SideGap / 2, CellH * 0.12, s);
            if (history == null) return;
            for (int tier = 1; tier <= tiers; tier++)
            {
                double x = SideGap + (tier - 1) % cols * HistW, y = (tier - 1) / cols * cellH;
                bool reached = tier <= history.Tier;
                var icon = new Image { Stretch = Stretch.Uniform, Width = 31 * s, Opacity = reached ? 1 : 0.25 };
                try { icon.Source = ImageCache.Load(CardStore.TierIconPath(tier), 0); } catch { }
                Add(_history, icon, x + (reached ? 4 : (HistW - 31) / 2), y + 10, s);
                if (!reached) continue;

                int triples; history.Triples.TryGetValue(tier, out triples);
                Add(_history, Triples(triples, s), x + 37, y + 8, s);

                int turn;
                if (history.TierTurn.TryGetValue(tier, out turn))
                    Add(_history, new TextBlock
                    {
                        Text = Strings.F("boards.headerTurn", turn), FontSize = 12.5 * s, Width = HistW * s, FontWeight = FontWeights.Bold,
                        TextAlignment = TextAlignment.Center, Foreground = UiKit.TextPrimary
                    }, x, y + 48, s);
            }
        }

        // Triples made on a tier, as the game's own leaderboard panel marks them: its picture of three
        // cards and an arrow (cut from the client's leaderboard texture, dataoard	riples.png) with
        // the count over the cards.
        private static FrameworkElement Triples(int count, double s)
        {
            var c = new Canvas { Width = 40 * s, Height = 33 * s, Opacity = count > 0 ? 1 : 0.6 };
            c.Children.Add(Sprite("triples", 40, s));
            var number = new OutlinedText(count.ToString(), 19 * s, Brushes.White);
            Canvas.SetLeft(number, 13 * s - number.Width / 2);
            Canvas.SetTop(number, 19 * s - number.Height / 2);
            c.Children.Add(number);
            return c;
        }

        private static Rectangle Line(double s) =>
            new Rectangle { Width = Math.Max(1, s), Height = CellH * 0.76 * s, Fill = UiKit.StrokeBrush };

        private void Place(Canvas canvas)
        {
            if (_dragging) return;
            double cw = canvas.ActualWidth, ch = canvas.ActualHeight;
            double top = Clamp(_yf * ch, 0, Math.Max(0, ch - _root.ActualHeight));
            Canvas.SetLeft(_root, Clamp(_xf * cw - _root.ActualWidth / 2, 0, Math.Max(0, cw - _root.ActualWidth)));
            Canvas.SetTop(_root, top);
            Edge(top);
        }

        // Against the top edge the box has no top border or top corners: it hangs from the screen's edge.
        private void Edge(double top)
        {
            bool hug = top < 0.5;
            if (hug == _hug) return;
            _hug = hug;
            _root.CornerRadius = hug ? new CornerRadius(0, 0, 8, 8) : new CornerRadius(8);
            _root.BorderThickness = hug ? new Thickness(1, 0, 1, 1) : new Thickness(1);
        }
        private bool _hug;

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

        // ── Arranging: move / scale (LL mouse hook for the gesture only, as LobbyTribesPanel) ────

        private void BeginGesture(bool resize, MouseButtonEventArgs e)
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (!_attached || _dragging || _resizing || canvas == null) return;
            try { _startCursor = e.GetPosition(canvas); } catch { return; }
            _startLeft = Canvas.GetLeft(_root); _startTop = Canvas.GetTop(_root); _startScale = _userScale;
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
            try { POINT p; GetCursorPos(out p); cur = canvas.PointFromScreen(new Point(p.X, p.Y)); }
            catch { return; }
            double dx = cur.X - _startCursor.X, dy = cur.Y - _startCursor.Y;
            if (Math.Abs(dx) + Math.Abs(dy) > 1) _moved = true;
            if (_dragging)
            {
                double top = Clamp(_startTop + dy, 0, Math.Max(0, ch - _root.ActualHeight));
                if (top < 6) top = 0;                                   // snaps to the top edge
                Canvas.SetLeft(_root, Clamp(_startLeft + dx, 0, Math.Max(0, cw - _root.ActualWidth)));
                Canvas.SetTop(_root, top);
                Edge(top);
            }
            else if (_resizing)
            {
                // Drag right to scale up, by the width the box had when the drag began (it grows both
                // ways from its middle, hence twice the cursor's travel).
                double w0 = Math.Max(80, _root.ActualWidth / (_userScale / _startScale));
                double next = Clamp(Math.Round(_startScale * (w0 + 2 * dx) / w0, 2), MinScale, MaxScale);
                if (next != _userScale && _lastBoard != null) { _userScale = next; Show(canvas, _lastBoard, _lastHero, _lastHistory); }
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
            if (dragged)
            {
                _xf = (Canvas.GetLeft(_root) + _root.ActualWidth / 2) / canvas.ActualWidth;
                _yf = Canvas.GetTop(_root) / canvas.ActualHeight;
                if (Math.Abs(_xf - 0.5) < 0.01) _xf = 0.5;              // near the middle is the middle
            }
            try
            {
                var p = _config.OpponentBoardsHud ?? (_config.OpponentBoardsHud = new HudPlacement());
                p.Set = true; p.XF = _xf; p.YF = _yf; p.WF = _userScale; p.Px = _userScale;
                _config.Save();
            }
            catch { }
        }

        private IntPtr _hook = IntPtr.Zero;
        private LowLevelMouseProc _proc;

        private void InstallHook()
        {
            if (_hook != IntPtr.Zero) return;
            try { _proc = HookProc; _hook = SetWindowsHookEx(14, _proc, GetModuleHandle(null), 0); }       // WH_MOUSE_LL
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
                if (wParam == (IntPtr)0x0200) { try { OnGestureMove(); } catch { } }       // WM_MOUSEMOVE
                else if (wParam == (IntPtr)0x0202) { try { EndGesture(); } catch { } }     // WM_LBUTTONUP
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
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
            // The tier badge is our own bundled tier icon, set where the game's badge sits.
            if (m.Tier >= 1 && m.Tier <= 7)
            {
                var badge = new Image { Stretch = Stretch.Uniform, Width = 38.5 * s };
                try { badge.Source = ImageCache.Load(CardStore.TierIconPath(m.Tier), 0); } catch { }
                Add(c, badge, left + 64, LayerTop + 30.5, s);
            }
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
                    origin.X += _rowShift;
                    if (cursor.Y >= origin.Y && cursor.Y <= origin.Y + CellH * _shownScale && cursor.X >= origin.X)
                    {
                        index = (int)((cursor.X - origin.X) / w);
                        if (index >= 0 && index < _shown.Minions.Count) over = _shown.Minions[index];
                    }
                    double centre = origin.X + (index + 0.5) * w;
                    if (over == null && _shown.Deity != null && _deity.ActualWidth > 0)
                    {
                        var at = _deity.TransformToAncestor(canvas).Transform(new Point(0, 0));
                        if (new Rect(at.X, at.Y, w, CellH * _shownScale).Contains(cursor)) { over = _shown.Deity; centre = at.X + w / 2; }
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
                    double left = centre - CardW * _shownScale / 2;
                    Canvas.SetLeft(_cards, Math.Max(0, Math.Min(left, canvas.ActualWidth - total)));
                    double below = origin.Y + (CellH + HeaderH) * _shownScale + 6 * _shownScale, cardH = CardW * _shownScale * 672.0 / 512.0;
                    Canvas.SetTop(_cards, below + cardH <= canvas.ActualHeight ? below : Math.Max(0, origin.Y - 6 * _shownScale - cardH));
                }
                catch (Exception ex) { if (!_failed) { _failed = true; try { _log("[boards] card: " + ex); } catch { } } }
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
                // The text as it stood on this board, when it differs from what the render shows.
                if (!stats.KeepText) try
                {
                    // In the language of the render: the client holds every language's text.
                    string language = "enUS";
                    try { language = TextLanguage?.Invoke() ?? "enUS"; } catch { }
                    string shown = golden ? (card.DisplayTextGold ?? card.DisplayText) : card.DisplayText;
                    string text = SnapshotText.Build(HostServices.Memory?.CardText(stats.TextId ?? card.ExternalId), stats, language);
                    if (text != null && SnapshotText.Plain(text) != SnapshotText.Plain(shown))
                        render = CardTextPaint.WithText(render, shown, text, Assets.GameArt.TextFaceFor(language), Assets.GameArt.TextLineSpace(language));
                }
                catch { }
                int baseAtk = (golden ? card.AttackGold : card.Attack) ?? stats.Attack, baseHp = (golden ? card.HealthGold : card.Health) ?? stats.Health;
                var numbered = CardStats.WithNumbers(render, Short(stats.Attack), Short(stats.Health), baseAtk.ToString().Length, baseHp.ToString().Length,
                    Brushes.White, Brushes.White);
                img.Source = numbered;
                if (!ReferenceEquals(numbered, render))
                {
                    // The numbered picture has a clear margin each side for overhanging numbers: draw it
                    // wider by that margin and pull it back, so the card itself keeps its place and size.
                    double fit = Math.Min(w / render.PixelWidth, h / render.PixelHeight), pad = fit * CardStats.Margin;
                    img.Width = w + 2 * pad; img.Height = h;
                    img.Margin = new Thickness(-pad, 0, -pad, 0);
                }
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
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
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
