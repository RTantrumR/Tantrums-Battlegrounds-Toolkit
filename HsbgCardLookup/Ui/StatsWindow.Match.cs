using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Line = System.Windows.Shapes.Line;
using Polyline = System.Windows.Shapes.Polyline;
using Ellipse = System.Windows.Shapes.Ellipse;
using HsbgCardLookup.Data;
using HsbgCardLookup.Game;
using HsbgCardLookup.Localization;
using HsbgCardLookup.Search;

namespace HsbgCardLookup.Ui
{
    // One match: the header with what the hero ran with, the final board, the lobby, health by round,
    // and every round's board. Boards are drawn by the opponents' boards panel's own minion.
    public sealed partial class StatsWindow
    {
        private const int CardTypeHeroPower = 10, CardTypeTrinket = 44;
        private const double RoundScale = 0.68;
        private static readonly Regex EnchantmentSuffix = new Regex(@"e\d*$", RegexOptions.Compiled);
        private Dictionary<string, BgCard> _gifts;

        // ── Pieces ───────────────────────────────────────────────────────────────────────────────

        /// <summary>A trinket or hero power as the game shows it: the art in its ring, the coin on top.
        /// Cut from our render of the card: a square around the medallion, clipped to the ring and the coin.
        /// Measures are the 512×672 render's; CardArt hands the render over with its empty margin trimmed
        /// (the same box for every trinket, and for every hero power), so the picture is set back by it. A hero power's name banner covers the ring's bottom, so that arc is drawn from the
        /// ring's clean right side, turned a quarter.</summary>
        private FrameworkElement Medallion(BgCard card, double size, bool power)
        {
            const double Crop = 312;
            double k = size / Crop, ox = power ? 102 : 116, oy = power ? 35 : 30, r = power ? 141 : 137, coinY = power ? 47 : 52;
            // The trimmed render: where it began in the full one, and its size.
            double tx = power ? 51 : 40, ty = power ? 29 : 33, tw = power ? 409 : 431, th = power ? 615 : 607;
            var c = new Canvas { Width = size, Height = size, ToolTip = card?.DisplayName, Background = Brushes.Transparent };
            var clip = new GeometryGroup { FillRule = FillRule.Nonzero };
            clip.Children.Add(new EllipseGeometry(new Point(156 * k, 170 * k), r * k, r * k));
            clip.Children.Add(new EllipseGeometry(new Point(156 * k, coinY * k), 47 * k, 47 * k));
            c.Clip = clip;
            Func<Image> picture = () =>
            {
                var img = new Image { Width = tw * k, Height = th * k, Stretch = Stretch.Fill };
                Canvas.SetLeft(img, -(ox - tx) * k); Canvas.SetTop(img, -(oy - ty) * k);
                c.Children.Add(img);
                return img;
            };
            var main = picture();
            Image patch = null;
            if (power)
            {
                patch = picture();
                var centre = new Point((ox + 156 - tx) * k, (oy + 170 - ty) * k);      // the ring's middle, in the picture's own space
                double far = 160 * k;
                var wedge = new PathGeometry(new[]
                {
                    new PathFigure(centre, new PathSegment[]
                    {
                        new LineSegment(new Point(centre.X + far, centre.Y - far * 1.35), true),
                        new LineSegment(new Point(centre.X + far, centre.Y + far * 1.35), true),
                    }, true)
                });
                var band = new CombinedGeometry(GeometryCombineMode.Exclude, new EllipseGeometry(centre, (r + 2) * k, (r + 2) * k), new EllipseGeometry(centre, 95 * k, 95 * k));
                patch.Clip = new CombinedGeometry(GeometryCombineMode.Intersect, band, wedge);
                patch.RenderTransform = new RotateTransform(90, centre.X, centre.Y);
            }
            Render(card, bmp => { main.Source = bmp; if (patch != null) patch.Source = bmp; });
            return c;
        }

        /// <summary>Health as the game shows it: the red drop with the number on it.</summary>
        private static FrameworkElement HealthDrop(int value, double height)
        {
            double k = height / 70;      // the drop is 48×70 inside the 307×404 capture, at (198, 237)
            var c = new Canvas { Width = 48 * k, Height = height, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center, ToolTip = value.ToString(CultureInfo.InvariantCulture) };
            var img = new Image { Width = 307 * k, Stretch = Stretch.Uniform };
            try { img.Source = ImageCache.Load(CardStore.BoardAssetPath("gem-health"), 0); } catch { }
            Put(c, img, -198 * k, -237 * k);
            var outer = new Canvas { Width = 48 * k, Height = height, VerticalAlignment = VerticalAlignment.Center };
            outer.Children.Add(c);
            try
            {
                var number = new OpponentBoardPanel.OutlinedText(OpponentBoardPanel.Short(Math.Max(0, value)), height * 0.5, Brushes.White);
                Put(outer, number, 24 * k - number.Width / 2, height * 0.6 - number.Height / 2);
            }
            catch { }
            return outer;
        }

        // A Dark Gift on a minion is named by its enchantment: the enchantment's id minus its "e<digits>"
        // suffix is the gift's card. One gift can leave several enchantments, so it counts as often as its
        // most frequent one.
        private List<BgCard> GiftsOf(Dictionary<string, int> enchantments)
        {
            var list = new List<BgCard>();
            if (enchantments == null || _store == null) return list;
            if (_gifts == null)
                _gifts = _store.All.Where(c => c.Categories != null && c.Categories.Contains("darkgift") && !string.IsNullOrEmpty(c.ExternalId))
                    .GroupBy(c => c.ExternalId).ToDictionary(x => x.Key, x => x.First());
            var most = new Dictionary<BgCard, int>();
            foreach (var kv in enchantments)
            {
                BgCard gift;
                if (!_gifts.TryGetValue(EnchantmentSuffix.Replace(kv.Key, ""), out gift)) continue;
                int n; most.TryGetValue(gift, out n);
                most[gift] = Math.Max(n, kv.Value);
            }
            foreach (var kv in most) for (int i = 0; i < kv.Value; i++) list.Add(kv.Key);
            return list;
        }

        private BoardMinion ToBoard(RecordedMinion m)
        {
            string kw = m.Keywords ?? "";
            return new BoardMinion
            {
                CardId = m.Card, Card = Lookup(m.Card), Attack = m.Attack, Health = m.Health, Golden = m.Golden, Tier = m.Tier,
                Taunt = kw.IndexOf('T') >= 0, DivineShield = kw.IndexOf('D') >= 0, Windfury = kw.IndexOf('W') >= 0, Reborn = kw.IndexOf('R') >= 0,
                Stealth = kw.IndexOf('S') >= 0, Poisonous = kw.IndexOf('P') >= 0, Venomous = kw.IndexOf('V') >= 0,
                Legendary = kw.IndexOf('e') >= 0, Deathrattle = kw.IndexOf('d') >= 0, Rally = kw.IndexOf('r') >= 0, Trigger = kw.IndexOf('t') >= 0,
                Gifts = GiftsOf(m.Enchantments),
            };
        }

        private FrameworkElement Board(List<RecordedMinion> minions, double scale)
        {
            if (minions == null || minions.Count == 0) return Txt(Strings.T("stats.match.emptyBoard"), 13, UiKit.TextMuted);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var m in minions.Take(7))
            {
                FrameworkElement cell;
                try { cell = OpponentBoardPanel.Minion(ToBoard(m), scale); }
                catch { cell = new Canvas { Width = OpponentBoardPanel.CellW * scale, Height = OpponentBoardPanel.CellH * scale }; }
                cell.ToolTip = (Lookup(m.Card)?.DisplayName ?? m.Card) + "  " + m.Attack.ToString(CultureInfo.InvariantCulture) + "/" + m.Health.ToString(CultureInfo.InvariantCulture);
                row.Children.Add(cell);
            }
            return row;
        }

        private List<BgCard> CardsOf(RecordedTurn turn, int type) =>
            turn == null ? new List<BgCard>() : turn.Cards.Where(c => c.Type == type).Select(c => Lookup(c.Card)).Where(c => c != null).ToList();

        // ── The page ─────────────────────────────────────────────────────────────────────────────

        private void MatchPage(MatchRecord m)
        {
            var back = Txt(Strings.T("stats.match.back"), 13, UiKit.AccentBrush, true);
            back.Cursor = Cursors.Hand; back.HorizontalAlignment = HorizontalAlignment.Left; back.Margin = new Thickness(0, 0, 0, 12);
            back.MouseEnter += (s, e) => back.TextDecorations = TextDecorations.Underline;
            back.MouseLeave += (s, e) => back.TextDecorations = null;
            back.MouseLeftButtonUp += (s, e) => Go("matches");
            _body.Children.Add(back);

            // Header: who, when, and what the hero ran with.
            var last = m.Turns.LastOrDefault();
            var powers = CardsOf(last, CardTypeHeroPower);
            if (powers.Count == 0) powers = m.HeroPowers.Select(Lookup).Where(c => c != null).ToList();
            var trinkets = CardsOf(last, CardTypeTrinket);
            BgCard deity = null;
            if (m.Mechanics != null && m.Mechanics.Deity > 0) _store?.ById.TryGetValue(m.Mechanics.Deity, out deity);

            var kit = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Action<string, IEnumerable<FrameworkElement>> group = (label, items) =>
            {
                var list = items.ToList();
                if (list.Count == 0) return;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
                foreach (var it in list) { it.Margin = new Thickness(0, 0, 6, 0); row.Children.Add(it); }
                var g = Stack(Gh(label), row);
                g.Margin = new Thickness(26, 0, 0, 0);
                kit.Children.Add(g);
            };
            group(Strings.T(powers.Count > 1 ? "stats.match.heroPowers" : "stats.match.heroPower"), powers.Select(c => Medallion(c, 74, true)));
            group(Strings.T("stats.match.trinkets"), trinkets.Select(c => Medallion(c, 74, false)));
            if (deity != null)
            {
                var d = Round(deity, 60, OpponentBoardPanel.MinionArt);
                d.Margin = new Thickness(0, 10, 0, 4);
                group(Strings.T("stats.match.deity"), new FrameworkElement[] { d });
            }

            var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
            title.Children.Add(SiteLink(m.Hero, 22));
            var meta = new List<string> { m.StartUtc.ToLocalTime().ToString("d MMM yyyy", DateCulture()), Clock(m.StartUtc), Strings.T("stats.modeOne." + ModeOf(m)) };
            if (m.Season > 0) meta.Add(Strings.F("stats.season", m.Season));
            if (!string.IsNullOrEmpty(m.Build)) meta.Add(Strings.F("stats.match.patch", string.Join(".", m.Build.Split('.').Take(3))));
            var sub = Txt(string.Join(" · ", meta), 13, UiKit.TextSecondary);
            sub.Margin = new Thickness(0, 4, 0, 0);
            title.Children.Add(sub);
            var top = new DockPanel { LastChildFill = true };
            var portrait = PlacedPortrait(m.Hero, 84, m.Place, true, false, CardBg);
            DockPanel.SetDock(portrait, Dock.Left);
            DockPanel.SetDock(kit, Dock.Right);
            top.Children.Add(portrait); top.Children.Add(kit); top.Children.Add(title);

            // One strip of numbers under it.
            int won, lost, tied; Wlt(m, out won, out lost, out tied);
            var strip = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            Action<string, UIElement> stat = (label, value) =>
            {
                int i = strip.ColumnDefinitions.Count;
                strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var row = new StackPanel { Orientation = Orientation.Horizontal, Height = 32, Margin = new Thickness(0, 3, 0, 0) };
                row.Children.Add(value);
                var cell = new Border
                {
                    Child = Stack(Gh(label), row), Padding = new Thickness(i == 0 ? 0 : 16, 12, 0, 0),
                    BorderBrush = UiKit.Br(StrokeSoft), BorderThickness = new Thickness(i == 0 ? 0 : 1, 1, 0, 0)
                };
                Grid.SetColumn(cell, i);
                strip.Children.Add(cell);
            };
            Func<string, Brush, UIElement> big = (text, brush) => new TextBlock { Text = text, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = brush ?? UiKit.TextPrimary, VerticalAlignment = VerticalAlignment.Center };
            Func<string, Brush, UIElement> small = (text, brush) => new TextBlock { Text = text, FontSize = 13, Foreground = brush ?? UiKit.TextMuted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 4, 10, 0) };
            Func<UIElement[], UIElement> join = parts => { var p = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var x in parts) p.Children.Add(x); return p; };
            stat(Strings.T("stats.match.place"), big(Ord(m.Place), UiKit.Br(PlaceColor(m.Place))));
            stat(Strings.T("stats.col.rounds"), join(new[] { big(m.Turns.Count.ToString(CultureInfo.InvariantCulture), null), small(Strings.F("stats.minutes", Minutes(m)), null) }));
            stat(Strings.T("stats.match.fights"), WltText(m, 22));
            stat(Strings.T("stats.match.damage"), join(new[]
            {
                big(m.Turns.Sum(t => t.Dealt).ToString(CultureInfo.InvariantCulture), null), small(Strings.T("stats.match.dealt"), null),
                big(m.Turns.Sum(t => t.Taken).ToString(CultureInfo.InvariantCulture), null), small(Strings.T("stats.match.taken"), null),
            }));
            var triples = new Image { Height = 28, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            try { triples.Source = ImageCache.Load(CardStore.BoardAssetPath("triples"), 0); } catch { }
            stat(Strings.T("stats.match.triples"), join(new UIElement[] { triples, big(m.Turns.Select(t => t.Triples).DefaultIfEmpty(0).Max().ToString(CultureInfo.InvariantCulture), null) }));
            stat(Strings.T("stats.col.rating"), m.RatingAfter.HasValue
                ? join(new[] { big(N(m.RatingAfter.Value), null), small(Signed(m.RatingChange ?? 0), SignBrush(m.RatingChange ?? 0)) })
                : big("–", UiKit.TextMuted));
            _body.Children.Add(Card(Stack(top, strip)));

            // Final board, full size.
            var final = Board(m.FinalBoard, 1);
            final.HorizontalAlignment = HorizontalAlignment.Center;
            _body.Children.Add(Card(Stack(CardHead(Strings.T("stats.match.finalBoard")), final)));

            // The lobby, in finishing order.
            var lobby = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var p in m.Players.OrderBy(p => p.Place))
            {
                var pic = PlacedPortrait(p.Hero, 54, p.Place, false, p.Me, CardBg);
                pic.HorizontalAlignment = HorizontalAlignment.Center;
                var name = new TextBlock
                {
                    Text = string.IsNullOrEmpty(p.Name) ? HeroName(p.Hero) : p.Name, FontSize = 11.5, Foreground = UiKit.TextSecondary, TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0), ToolTip = HeroName(p.Hero)
                };
                var cell = Stack(pic, name);
                cell.Width = 88; cell.Margin = new Thickness(0, 0, 14, 0);
                lobby.Children.Add(cell);
            }
            _body.Children.Add(Card(Stack(CardHead(Strings.T("stats.match.lobby")), lobby)));

            Border healthCard = null, roundsCard = null;
            healthCard = Card(Stack(CardHead(Strings.T("stats.match.health")), HealthChart(m, InnerW, () => healthCard)));
            _body.Children.Add(healthCard);
            roundsCard = Card(Stack(CardHead(Strings.T("stats.match.rounds"), null, null, Strings.T("stats.match.roundsSub")), Rounds(m, () => roundsCard)));
            _body.Children.Add(roundsCard);
        }

        private UIElement HealthChart(MatchRecord m, double W, Func<Border> card)
        {
            var t = m.Turns;
            if (t.Count < 2) return null;
            const double H = 150, L = 34, R = 28, T = 24, B = 26;
            double max = Math.Ceiling(Math.Max(10, t.Max(q => q.Health)) / 10.0) * 10;
            Func<int, double> x = i => L + i * (W - L - R) / (t.Count - 1);
            Func<double, double> y = v => T + (max - Math.Max(v, 0)) * (H - T - B) / max;
            var c = new Canvas { Width = W, Height = H };
            foreach (double v in new[] { 0, max / 2, max })
            {
                c.Children.Add(new Line { X1 = L, X2 = W - R, Y1 = y(v), Y2 = y(v), Stroke = UiKit.Br(StrokeSoft), StrokeThickness = 1 });
                Put(c, AxisText(((int)v).ToString(CultureInfo.InvariantCulture), L - 7, TextAlignment.Right), 0, y(v) - 8);
            }
            var line = new Polyline { Stroke = UiKit.TextSecondary, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            for (int i = 0; i < t.Count; i++)
            {
                line.Points.Add(new Point(x(i), y(t[i].Health)));
                // A new tavern tier: its icon over a dotted line.
                if (i == 0 || t[i].Tier != t[i - 1].Tier)
                {
                    c.Children.Add(new Line { X1 = x(i), X2 = x(i), Y1 = T, Y2 = H - B, Stroke = UiKit.StrokeBrush, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2, 4 } });
                    Put(c, TierIcon(t[i].Tier, 22), x(i) - 11, 0);
                }
                Put(c, AxisText(t[i].Number.ToString(CultureInfo.InvariantCulture), 30, TextAlignment.Center, UiKit.TextSecondary), x(i) - 15, H - 20);
            }
            c.Children.Add(line);
            for (int i = 0; i < t.Count; i++)
            {
                var turn = t[i];
                var dot = new Ellipse
                {
                    Width = 9, Height = 9, Fill = UiKit.Br(turn.Result == "W" ? Good : turn.Result == "L" ? Bad : Tie), Stroke = UiKit.Br(CardBg), StrokeThickness = 1.5,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
                var hit = new Grid { Width = 24, Height = 24, Background = Brushes.Transparent, ToolTip = Strings.F("stats.match.round", turn.Number) + ": " + turn.Health };
                hit.Children.Add(dot);
                Put(c, hit, x(i) - 12, y(turn.Health) - 12);
                string id = "r" + turn.Number;
                MarkAs(hit, id, card, on => { dot.Stroke = on ? UiKit.TextPrimary : UiKit.Br(CardBg); dot.StrokeThickness = on ? 2 : 1.5; dot.Width = dot.Height = on ? 13 : 9; });
                Source(hit, new[] { id }, true, card);
            }
            return c;
        }

        private UIElement Rounds(MatchRecord m, Func<Border> card)
        {
            // What was picked up between rounds: the trinkets in play that the round before did not have.
            var gained = new Dictionary<RecordedTurn, List<BgCard>>();
            List<string> before = null;
            foreach (var t in m.Turns)
            {
                var now = t.Cards.Where(c => c.Type == CardTypeTrinket && Lookup(c.Card) != null).Select(c => c.Card).ToList();
                gained[t] = now.Where(id => before == null || !before.Contains(id)).Select(Lookup).ToList();
                before = now;
            }
            bool any = gained.Values.Any(g => g.Count > 0);
            double[] cols = { 84, 180, any ? 104 : 0.001, 0 };
            var sides = new[] { HorizontalAlignment.Left, HorizontalAlignment.Left, HorizontalAlignment.Left, HorizontalAlignment.Left };
            var stack = new StackPanel();
            stack.Children.Add(new Border
            {
                Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 0, 6),
                Child = Columns(cols, new UIElement[] { Gh(Strings.T("stats.match.colRound")), Gh(Strings.T("stats.match.colOpponent")), any ? Gh(Strings.T("stats.match.colPickedUp")) : null, Gh(Strings.T("stats.match.colBoard")) }, sides)
            });
            foreach (var turn in Enumerable.Reverse(m.Turns))
            {
                var t = turn;
                var state = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
                var tier = TierIcon(t.Tier, 28);
                tier.Margin = new Thickness(0, 0, 8, 0); tier.ToolTip = Strings.F("stats.match.tier", t.Tier);
                state.Children.Add(tier);
                state.Children.Add(HealthDrop(t.Health, 34));
                var round = Stack(Txt(Strings.F("stats.match.round", t.Number), 14, null, true), state);

                var opp = new StackPanel();
                if (!string.IsNullOrEmpty(t.OpponentHero))
                {
                    var who = new StackPanel { Orientation = Orientation.Horizontal };
                    var pic = HeroPortrait(t.OpponentHero, 32);
                    pic.Margin = new Thickness(0, 0, 9, 0);
                    who.Children.Add(pic);
                    var name = Txt(HeroName(t.OpponentHero), 14);
                    name.MaxWidth = 130;
                    who.Children.Add(name);
                    opp.Children.Add(who);
                }
                string result = t.Result == "W" ? Strings.F("stats.match.won", t.Dealt) : t.Result == "L" ? Strings.F("stats.match.lost", t.Taken) : Strings.T("stats.match.tied");
                var res = Txt(result, 13, UiKit.Br(t.Result == "W" ? Good : t.Result == "L" ? Bad : Tie), true);
                res.Margin = new Thickness(0, 5, 0, 0);
                opp.Children.Add(res);

                var picked = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var c in gained[t]) picked.Children.Add(Medallion(c, 48, false));

                var row = new Border
                {
                    Child = Columns(cols, new UIElement[] { round, opp, picked, Board(t.Board, RoundScale) }, sides),
                    CornerRadius = new CornerRadius(8), Background = UiKit.Br(UiKit.RowBg), Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(0, 0, 0, 4)
                };
                string id = "r" + t.Number;
                bool lit = false, over = false;
                Action paint = () => row.Background = UiKit.Br(lit || over ? UiKit.RowBgHover : UiKit.RowBg);
                row.MouseEnter += (s, e) => { over = true; paint(); };
                row.MouseLeave += (s, e) => { over = false; paint(); };
                MarkAs(row, id, card, on => { lit = on; paint(); });
                Source(row, new[] { id }, true, card);
                stack.Children.Add(row);
            }
            return stack;
        }
    }
}
