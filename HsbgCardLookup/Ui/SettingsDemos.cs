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
using HsbgCardLookup.Data;
using HsbgCardLookup.Localization;

namespace HsbgCardLookup.Ui
{
    /// <summary>
    /// Settings' small pictures of what a switch does: one looping storyboard each, running only while
    /// the picture is on screen, and each follows the settings it stands next to (the page rebuilds or
    /// refreshes it).
    ///
    /// Owner's rules (2026-11-22): a picture shows the game as the player will see it — a real frame
    /// captured from a replay (data/demos), the toolkit's real panels and card renders over it, never a
    /// stand-in shape — and the whole interaction the setting changes. It takes only the room it needs:
    /// a picture that fits beside its label sits in the row (the turn counter), the rest get a stage.
    /// </summary>
    internal static class SettingsDemos
    {
        public const double StageW = 474;     // with its border, the page's width while the scrollbar shows
        private static readonly Brush StageBg = Frozen(Color.FromRgb(0x1B, 0x22, 0x2E));
        private static readonly Brush BoxBg = Frozen(Color.FromArgb(0xDC, 0x0A, 0x0D, 0x14));

        // ── The pieces every picture is made of ─────────────────────────────────────────────────

        /// <summary>The bordered box a picture sits in.</summary>
        public static Border Stage(UIElement child) => new Border
        {
            Child = child, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 2, 0, 12),
            Background = StageBg, BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left, Width = StageW + 2, ClipToBounds = true
        };

        /// <summary>One looping pass of <paramref name="seconds"/>; values are held between keys and eased into.</summary>
        private sealed class Loop
        {
            private readonly Storyboard _story = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
            private readonly double _seconds;
            public double Seconds => _seconds;
            public Loop(double seconds) { _seconds = seconds; }

            public void Keys(DependencyObject target, object property, double[] times, double[] values) =>
                Keys(target, property, times, values, null);

            /// <summary>As above; <paramref name="eases"/>[i], when given, is how key i is reached
            /// (null = the usual ease in and out).</summary>
            public void Keys(DependencyObject target, object property, double[] times, double[] values, IEasingFunction[] eases)
            {
                var k = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(_seconds) };
                for (int i = 0; i < times.Length; i++)
                    k.KeyFrames.Add(new EasingDoubleKeyFrame(values[i], KeyTime.FromTimeSpan(TimeSpan.FromSeconds(times[i])),
                        (eases != null && i < eases.Length ? eases[i] : null) ?? new CubicEase { EasingMode = EasingMode.EaseInOut }));
                Add(k, target, property);
            }

            /// <summary>A value that jumps at the given times (a corner radius, say): values[i] from times[i] on.</summary>
            public void Steps(DependencyObject target, object property, double[] times, object[] values)
            {
                var k = new ObjectAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(_seconds) };
                for (int i = 0; i < times.Length; i++)
                    k.KeyFrames.Add(new DiscreteObjectKeyFrame(values[i], KeyTime.FromTimeSpan(TimeSpan.FromSeconds(times[i]))));
                Add(k, target, property);
            }

            private void Add(Timeline line, DependencyObject target, object property)
            {
                Storyboard.SetTarget(line, target);
                Storyboard.SetTargetProperty(line, property as PropertyPath ?? new PropertyPath((DependencyProperty)property));
                _story.Children.Add(line);
            }

            /// <summary>Opacity 0 → 1 at <paramref name="on"/>, back to 0 at <paramref name="off"/> (0.3 s each way).</summary>
            public void Show(UIElement target, double on, double off, double full = 1)
            {
                target.Opacity = 0;
                Keys(target, UIElement.OpacityProperty,
                    new[] { 0, on, on + 0.3, off, off + 0.3, _seconds }, new[] { 0.0, 0, full, full, 0, 0 });
            }

            public void Move(UIElement target, double[] times, double[] xs, double[] ys)
            {
                Keys(target, Canvas.LeftProperty, times, xs);
                Keys(target, Canvas.TopProperty, times, ys);
            }

            public void Run(FrameworkElement stage)
            {
                stage.Loaded += (s, e) => { try { _story.Begin(stage, true); } catch { } };
                stage.Unloaded += (s, e) => { try { _story.Stop(stage); } catch { } };
            }

            /// <summary>For a loop put together once the stage is on screen: begin now, and again
            /// whenever it comes back.</summary>
            public void RunNow(FrameworkElement stage)
            {
                try { _story.Begin(stage, true); } catch { }
                Run(stage);
            }

            /// <summary>Test aid: hold the loop at <paramref name="t"/> seconds, applied at once (to render frames).</summary>
            public void SeekTo(FrameworkElement stage, double t)
            {
                _story.Pause(stage);
                _story.SeekAlignedToLastTick(stage, TimeSpan.FromSeconds(t), TimeSeekOrigin.BeginTime);
            }
        }

        private static Path Cursor() => new Path
        {
            Data = Geometry.Parse("M0 0 L0 16 L4.5 12 L7.5 19 L10 18 L7 11 L13 11 Z"),
            Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1, StrokeLineJoin = PenLineJoin.Round,
            Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = 0.6 }
        };

        // A click: a ring that opens out from the cursor's tip and fades.
        private static Ellipse Click(Canvas stage, Loop loop, double x, double y, double at)
        {
            var ring = new Ellipse
            {
                Width = 26, Height = 26, Stroke = UiKit.AccentBrush, StrokeThickness = 2, Opacity = 0,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(0.2, 0.2)
            };
            Canvas.SetLeft(ring, x - 13); Canvas.SetTop(ring, y - 13);
            stage.Children.Add(ring);
            loop.Keys(ring, UIElement.OpacityProperty, new[] { 0, at, at + 0.05, at + 0.45, loop.Seconds }, new[] { 0.0, 0, 1, 0, 0 });
            loop.Keys(ring, new PropertyPath("RenderTransform.ScaleX"), new[] { 0, at, at + 0.45, loop.Seconds }, new[] { 0.2, 0.2, 1, 1 });
            loop.Keys(ring, new PropertyPath("RenderTransform.ScaleY"), new[] { 0, at, at + 0.45, loop.Seconds }, new[] { 0.2, 0.2, 1, 1 });
            return ring;
        }

        private static void Put(Canvas stage, UIElement el, double x, double y)
        {
            Canvas.SetLeft(el, x); Canvas.SetTop(el, y);
            stage.Children.Add(el);
        }

        // ── Turn counter: the box itself, at its real size, in the row beside the switch ─────────

        public static FrameworkElement TurnCounter() => new Border
        {
            Background = BoxBg, BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 4, 10, 5), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Child = new TextBlock { Text = Strings.F("turnCounter.text", 7), Foreground = UiKit.TextPrimary, FontWeight = FontWeights.SemiBold, FontSize = 17 }
        };

        // ── A real game frame ──────────────────────────────────────────────────────────────────

        // Captured from a replay at 1920×1080 (the game's own render: no overlay, no replay controls),
        // cropped to game pixels x 200..1620, y 0..760: the leaderboard column on the left, the tavern
        // under the top edge where the board hangs.
        private const double FrameX = 200, FrameW = 1420, FrameH = 760;
        // The third leaderboard portrait's centre in that capture, in game pixels.
        private static readonly Point PortraitThree = new Point(295, 395);

        private static Image GameFrame(double width)
        {
            var img = new Image { Width = width, Height = width * FrameH / FrameW, Stretch = Stretch.Fill, IsHitTestVisible = false };
            try
            {
                string path = CardStore.DemoAssetPath("game-leaderboard");
                if (path != null) img.Source = ImageCache.Load(path, (int)Math.Min(FrameW, Math.Ceiling(width * 2)));
            }
            catch { }
            return img;
        }

        // ── Lobby types: the box itself, by the layout, look and banned-types settings ──────────

        public static FrameworkElement LobbyTypes(PluginConfig config, out Action refresh)
        {
            var box = LobbyTribesPanel.Preview(config, out refresh);
            return Stage(new Grid { MinHeight = 76, Children = { new Border { Padding = new Thickness(12), Child = box } } });
        }

        // ── Opponents' boards: the game's own leaderboard, the toolkit's real board and card ─────
        //
        // Over the replay frame, at the scale they have over a 1080-high game: the cursor rests on the
        // third portrait and that player's board hangs from the top edge. With pinning on, a click keeps
        // it open, the cursor moves onto a minion and its big card opens under it, and a click on the
        // board lets it go. With pinning off the board goes when the cursor leaves the portrait (and a
        // minion can't be reached), so that loop stops there.

        public static FrameworkElement BoardsHover(bool pin, PluginConfig config, CardStore store)
        {
            double k = StageW / FrameW;
            double len = pin ? 7.2 : 4.8;
            var stage = new Canvas { Width = StageW, Height = Math.Round(FrameH * k) };
            var loop = new Loop(len);

            // Everything in game pixels, scaled down as one.
            var game = new Canvas { Width = FrameW, Height = FrameH, RenderTransform = new ScaleTransform(k, k) };
            game.Children.Add(GameFrame(FrameW));
            stage.Children.Add(game);

            FrameworkElement card; Point centre; double cardTop;
            // The fourth minion: its card opens over the middle of the tavern, clear of the portraits.
            var board = OpponentBoardPanel.DemoBoard(config, store, 3, out card, out centre, out cardTop);
            double boardLeft = 960 - FrameX - board.DesiredSize.Width / 2;   // hangs from the top edge, centred
            Put(game, board, boardLeft, 0);
            double cardW = double.IsNaN(card.Width) ? 250 : card.Width;
            Put(game, card, Math.Max(0, Math.Min(boardLeft + centre.X - cardW / 2, FrameW - cardW)), cardTop);

            // Cursor and click rings stay at the stage's own size, so they read at this scale.
            var portrait = new Point((PortraitThree.X - FrameX) * k, PortraitThree.Y * k);
            var minion = new Point((boardLeft + centre.X) * k, (centre.Y + 18) * k);
            var rest = new Point(StageW * 0.62, stage.Height * 0.82);
            var cursor = Cursor();
            if (pin)
            {
                loop.Move(cursor, new[] { 0, 0.3, 1.0, 1.6, 2.4, 5.0, 5.7, len },
                    new[] { rest.X, rest.X, portrait.X, portrait.X, minion.X, minion.X, rest.X, rest.X },
                    new[] { rest.Y, rest.Y, portrait.Y, portrait.Y, minion.Y, minion.Y, rest.Y, rest.Y });
                Click(stage, loop, portrait.X, portrait.Y, 1.15);
                loop.Show(board, 1.2, 4.95);
                loop.Show(card, 2.5, 4.95);
                Click(stage, loop, minion.X, minion.Y, 4.9);
            }
            else
            {
                loop.Move(cursor, new[] { 0, 0.3, 1.0, 2.8, 3.4, len },
                    new[] { rest.X, rest.X, portrait.X, portrait.X, rest.X, rest.X },
                    new[] { rest.Y, rest.Y, portrait.Y, portrait.Y, rest.Y, rest.Y });
                loop.Show(board, 1.0, 2.8);
                card.Visibility = Visibility.Collapsed;
            }
            // The cursor goes last so it passes over everything.
            Put(stage, cursor, rest.X, rest.Y);
            loop.Run(stage);
            return Stage(stage);
        }

        // ── Card search: the key opens it over the game, a card is dragged out, the key closes it ─

        public static FrameworkElement SearchFlow(PluginConfig config, CardStore store)
        {
            const double len = 6.6;
            var stage = new Canvas { Width = StageW, Height = 176 };
            var loop = new Loop(len);
            bool drag = config.DragFromGrid || config.DragFromDetail;

            // The game it opens over: the replay frame from its top edge.
            Put(stage, GameFrame(StageW), 0, 0);

            string key = string.IsNullOrEmpty(config.BrowserKey) || config.BrowserKey == "None" ? "—" : config.BrowserKey;
            var cap = new Border
            {
                MinWidth = 44, Height = 34, Padding = new Thickness(9, 0, 9, 0), CornerRadius = new CornerRadius(6),
                Background = Frozen(Color.FromRgb(0x2A, 0x33, 0x45)), BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(1, 1, 1, 3),
                Child = new TextBlock { Text = key, Foreground = UiKit.TextPrimary, FontWeight = FontWeights.SemiBold, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            Put(stage, cap, 22, 128);
            double closeAt = drag ? 4.4 : 3.2;
            // A press: the cap sinks two pixels and lights up.
            loop.Keys(cap, Canvas.TopProperty,
                new[] { 0, 0.5, 0.6, 0.85, 0.95, closeAt, closeAt + 0.1, closeAt + 0.35, closeAt + 0.45, len },
                new[] { 128.0, 128, 130, 130, 128, 128, 130, 130, 128, 128 });
            var glow = new Rectangle { Width = 60, Height = 44, RadiusX = 9, RadiusY = 9, Fill = UiKit.AccentBrush, Opacity = 0, Effect = new BlurEffect { Radius = 12 } };
            Put(stage, glow, 14, 123);
            stage.Children.Remove(glow); stage.Children.Insert(1, glow);
            loop.Keys(glow, UIElement.OpacityProperty,
                new[] { 0, 0.5, 0.65, 1.0, closeAt, closeAt + 0.15, closeAt + 0.5, len }, new[] { 0.0, 0, 0.55, 0, 0, 0.55, 0, 0 });

            // The search as it is laid out: the search box, three columns of cards, the chosen card on
            // the right. Real card renders from the art pack.
            string[] ids = { "BG36_364", "BG25_001", "BG29_813", "BG27_005", "BG33_822", "BG36_369" };
            const double ow = 252, oh = 152, ox = 104, oy = 10, artW = 40, cellW = 46, cellH = 58, detailW = 78;
            var overlay = new Border
            {
                Width = ow, Height = oh, Background = Frozen(UiKit.PanelBg), BorderBrush = UiKit.StrokeBrush,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), ClipToBounds = true
            };
            var inside = new Canvas();
            overlay.Child = inside;
            Put(inside, new Border
            {
                Width = ow - 20, Height = 18, Background = Frozen(UiKit.FieldBg), BorderBrush = UiKit.AccentBrush, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Child = new TextBlock { Text = "🔍", FontSize = 8, Foreground = UiKit.TextMuted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) }
            }, 9, 8);
            for (int i = 0; i < ids.Length; i++)
                Put(inside, Art(store, ids[i], artW), 9 + i % 3 * cellW, 34 + i / 3 * cellH);
            Put(inside, Art(store, ids[0], detailW), ow - detailW - 14, 34);
            Put(stage, overlay, ox, oy);
            loop.Show(overlay, 0.75, closeAt + 0.15);

            if (drag)
            {
                // The chosen card is pulled out of the detail column (or out of the grid) and left on the screen.
                bool fromDetail = config.DragFromDetail;
                double fromX = ox + (fromDetail ? ow - detailW - 14 : 9), fromY = oy + 34, fromW = fromDetail ? detailW : artW;
                const double toX = 380, toY = 44;
                var card = Art(store, ids[0], detailW);
                Put(stage, card, fromX, fromY);
                loop.Move(card, new[] { 0, 2.0, 3.2, len }, new[] { fromX, fromX, toX, toX }, new[] { fromY, fromY, toY, toY });
                // "Hide dragged cards with app": it goes with the overlay, or it stays a while longer.
                loop.Show(card, 1.9, config.HideDraggedWithApp ? closeAt + 0.15 : len - 0.6);
                var cursor = Cursor();
                Put(stage, cursor, 300, 160);
                double grabX = fromX + fromW / 2, grabY = fromY + fromW * 0.6;
                loop.Move(cursor, new[] { 0, 1.2, 2.0, 3.2, 3.9, len },
                    new[] { 300.0, 300, grabX, toX + detailW / 2, 330, 330 }, new[] { 160.0, 160, grabY, toY + detailW * 0.6, 160, 160 });
            }
            loop.Run(stage);
            return Stage(stage);
        }

        // A card render as the search shows it (the art pack's WebP, or the CDN's), at a width.
        private static Image Art(CardStore store, string id, double width)
        {
            var img = new Image { Width = width, Height = width * 672.0 / 512.0, Stretch = Stretch.Uniform, IsHitTestVisible = false };
            try
            {
                var c = store?.Lookup(id);
                if (c != null) { ArtImage.SetDecode(img, (int)Math.Ceiling(width * 2)); ArtImage.SetCard(img, c); }
            }
            catch { }
            return img;
        }

        // ── Assemble panels: the real lobby types box dragged onto the real sidebar, and joined ────
        //
        // The game's left edge (data/demos/game-left.jpg: game pixels x 0..960 of the same capture,
        // full height, Replay Watcher's bar patched out) with the sidebar just right of the leaderboard.
        // Two passes, as the owner asked (2026-11-22): a one-row box with the lobby's types joins the
        // sidebar's top, then a one-column box with the banned types too joins its side. Each time the
        // cursor carries the box within reach, the two edges that will meet light up and an outline
        // shows the place, as PanelDock offers a join; on the drop the join plays as PanelDock plays it:
        // the smaller of the two is brought up to the other's width (on top) or height (at the side) by
        // SIZE, its text and icons growing with it (PanelDock.Join), so the two read as one panel with
        // no band left over; the box is pulled the last stretch in (BackEase 0.55, 260 ms), the meeting
        // corners square off, the sidebar is nudged, a line of light runs along the seam, sparks fly.

        private const double LeftW = 960, LeftH = 1080;
        private static readonly Color Spark = Color.FromRgb(0x7F, 0xE3, 0xFF);
        private const double PassLen = 5.8;

        private sealed class JoinPass
        {
            public Border Box;
            public bool Stacked;          // true: on the sidebar's top; false: at its right side
            public double T0;             // when the pass starts in the loop
            public Border Ghost;
            public Rectangle BarA, BarB, Seam;
            public readonly List<Rectangle> Sparks = new List<Rectangle>();
            public ScaleTransform Grow;   // the box's size, when it is the smaller one
        }

        public static FrameworkElement Assemble(PluginConfig config)
        {
            double k = StageW / 760;
            var stage = new Canvas { Width = StageW, Height = Math.Round(330 * k) };

            // Game pixels: a 1920×1080 canvas (the panels size and place themselves by it), shifted and
            // scaled to the part in view once the panels are laid out.
            var shift = new TranslateTransform();
            var zoom = new ScaleTransform(k, k);
            var game = new Canvas
            {
                Width = 1920, Height = 1080, IsHitTestVisible = false,
                RenderTransform = new TransformGroup { Children = { shift, zoom } }
            };
            var frame = new Image { Width = LeftW, Height = LeftH, Stretch = Stretch.Fill };
            try
            {
                string path = CardStore.DemoAssetPath("game-left");
                if (path != null) frame.Source = ImageCache.Load(path, (int)Math.Min(LeftW, Math.Ceiling(StageW * 2.5)));
            }
            catch { }
            Put(game, frame, 0, 0);
            stage.Children.Add(game);

            // The sidebar, the real one, with its sample standings and the settings' content switches
            // (the rating shown when nothing else is assigned to it, as its own preview does).
            var side = new MmrSidePanel(game);
            // Just right of the leaderboard, over the tavern, at its default size: there is room there
            // for a box on its top and one at its side (at its default spot by the screen's left edge, a
            // box at its side would land on the leaderboard portraits).
            side.Place(392 / 1920.0, 0.30, 210 / 1920.0);
            bool rating = config.MmrInPanel || !config.PanelInUse;
            side.NameMode = config.PanelNameMode;
            side.ShowRating = rating;
            side.ShowDeltas = rating && config.ShowMmrDeltas;
            side.ShowTiers = Game.BgMmr.TiersInPanelFor(config);
            side.ShowTribes = Game.BgMmr.TribesInPanelFor(config);
            side.DimDead = config.DimDeadPlayers;
            side.SetStandings(MmrSidePanel.SampleRows(false));

            // The types boxes, the real ones, in the user's look.
            Func<string, bool, Border> types = (layout, banned) =>
            {
                Action unused;
                var demo = new PluginConfig { LobbyTribesLayout = layout, LobbyTribesStyle = config.LobbyTribesStyle, ShowLobbyTribes = true, ShowBannedTribes = banned };
                return LobbyTribesPanel.Preview(demo, out unused) as Border;
            };
            var passes = new List<JoinPass>
            {
                new JoinPass { Box = types("1x5", false), Stacked = true, T0 = 0 },
                new JoinPass { Box = types("5x1", true), Stacked = false, T0 = PassLen },
            };
            if (passes.Any(p => p.Box == null)) return Stage(stage);
            foreach (var p in passes)
            {
                p.Box.Opacity = 0;
                p.Grow = new ScaleTransform(1, 1);
                p.Box.RenderTransform = p.Grow;
                game.Children.Add(p.Box);
                // PanelDock's offer and join pieces, one set per pass.
                p.Ghost = new Border
                {
                    BorderBrush = new SolidColorBrush(Spark), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(7),
                    Background = new SolidColorBrush(Color.FromArgb(0x22, Spark.R, Spark.G, Spark.B)), Opacity = 0
                };
                p.BarA = SparkBar(); p.BarB = SparkBar();
                p.Seam = new Rectangle
                {
                    Fill = Brushes.White, RadiusX = 2, RadiusY = 2, Opacity = 0,
                    Effect = new DropShadowEffect { Color = Spark, BlurRadius = 22, ShadowDepth = 0, Opacity = 1 },
                    RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(p.Stacked ? 0 : 1, p.Stacked ? 1 : 0)
                };
                foreach (UIElement el in new UIElement[] { p.Ghost, p.BarA, p.BarB, p.Seam }) game.Children.Add(el);
                for (int i = 0; i < 9; i++)
                {
                    var spark = new Rectangle { Fill = new SolidColorBrush(i % 3 == 0 ? Colors.White : Spark), Opacity = 0 };
                    p.Sparks.Add(spark);
                    game.Children.Add(spark);
                }
            }
            var cursor = Cursor();
            Put(stage, cursor, StageW * 0.78, stage.Height * 0.8);

            // The panels' sizes and the sidebar's place are only known once laid out on screen.
            bool built = false;
            stage.Loaded += (s, e) =>
            {
                if (built) return;
                built = true;
                stage.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { Choreograph(stage, shift, zoom, side, passes, cursor); }
                    catch { }
                }), System.Windows.Threading.DispatcherPriority.ContextIdle);
            };
            return Stage(stage);
        }

        private static Rectangle SparkBar() => new Rectangle
        {
            Fill = new SolidColorBrush(Spark), RadiusX = 2, RadiusY = 2, Opacity = 0,
            Effect = new DropShadowEffect { Color = Spark, BlurRadius = 14, ShadowDepth = 0, Opacity = 1 }
        };

        private static void Choreograph(Canvas stage, TranslateTransform shift, ScaleTransform zoom, MmrSidePanel side,
            List<JoinPass> passes, UIElement cursor)
        {
            double len = PassLen * passes.Count;
            var loop = new Loop(len);
            var sb = side.Bounds;
            if (sb.Width <= 0 || sb.Height <= 0) sb = new Rect(392, 324, 210, 210);
            var root = side.DockRoot;
            var r = root.CornerRadius;
            var snap = new BackEase { Amplitude = 0.55, EasingMode = EasingMode.EaseOut };
            IEasingFunction none = null;
            // Within a pass: show, grab, carry, offer, drop, pulled in, rest, fade, put back unseen.
            const double grab = 0.8, carried = 1.9, drop = 2.6, pulled = 2.86, fade = 5.0, back = 5.4;

            // Where everything goes, pass by pass, in game pixels. At the drop the smaller of the two is
            // scaled up to the other's width (on top) or height (at the side), from its top-left corner.
            var geo = new List<double[]>();  // startX, startY, nearX, nearY, landX, landY, bw, bh, boxScale, sideScale
            double top = sb.Y - 30, bottom = sb.Bottom + 20, right = sb.Right + 20;
            foreach (var p in passes)
            {
                double bw = p.Box.ActualWidth > 0 ? p.Box.ActualWidth : 260, bh = p.Box.ActualHeight > 0 ? p.Box.ActualHeight : 60;
                double mine = p.Stacked ? bw : bh, its = p.Stacked ? sb.Width : sb.Height;
                double boxScale = mine < its ? its / mine : 1, sideScale = its < mine ? mine / its : 1;
                double landX = p.Stacked ? sb.X : sb.X + sb.Width * sideScale;
                double landY = p.Stacked ? sb.Y - bh * boxScale : sb.Y;
                double nearX = p.Stacked ? sb.X + 14 : sb.Right + 18, nearY = p.Stacked ? sb.Y - bh - 18 : sb.Y + 14;
                double startX = p.Stacked ? sb.X + 80 : sb.Right + 70, startY = p.Stacked ? nearY - 92 : sb.Y - 40;
                geo.Add(new[] { startX, startY, nearX, nearY, landX, landY, bw, bh, boxScale, sideScale });
                top = Math.Min(top, Math.Min(startY, landY) - 24);
                bottom = Math.Max(bottom, Math.Max(startY + bh, Math.Max(nearY + bh, Math.Max(sb.Y + sb.Height * sideScale, landY + bh * boxScale))) + 20);
                right = Math.Max(right, Math.Max(startX + bw, Math.Max(landX + bw * boxScale, sb.X + sb.Width * sideScale)) + 24);
            }
            // The view: from the screen's left edge to whatever the joined panels need, no narrower than
            // the leaderboard and the sidebar want to be readable.
            double viewW = Math.Max(760, Math.Min(LeftW, right));
            double k = StageW / viewW;
            top = Math.Max(0, top); bottom = Math.Min(LeftH, bottom);
            zoom.ScaleX = k; zoom.ScaleY = k;
            shift.Y = -top;
            stage.Height = Math.Round((bottom - top) * k);
            Func<double, double, Point> onStage = (x, y) => new Point(x * k, (y - top) * k);
            var rest = new Point(StageW * 0.8, stage.Height * 0.85);

            // The sidebar: shown for each pass, sized and squared where the box joins it, nudged on the join.
            var sideOpacity = new List<double[]> { new[] { 0.0, 0 } };
            var sideSize = new List<double[]> { new[] { 0.0, 1 } };
            var nudgeKeys = new List<double[]> { new[] { 0.0, 1 } };
            var cornerTimes = new List<double> { 0 };
            var cornerValues = new List<object> { r };
            var cursorKeys = new List<double[]> { new[] { 0.0, rest.X, rest.Y } };

            for (int n = 0; n < passes.Count; n++)
            {
                var p = passes[n]; var g = geo[n]; double t = p.T0;
                double startX = g[0], startY = g[1], nearX = g[2], nearY = g[3], landX = g[4], landY = g[5];
                double bw = g[6], bh = g[7], boxScale = g[8], sideScale = g[9];

                // The box: carried in, pulled into place with the overshoot, brought up to size, corners squared.
                double[] times = { 0, t + 0.9, t + carried, t + drop, t + pulled, t + back, t + back + 0.01, len };
                loop.Keys(p.Box, Canvas.LeftProperty, times, new[] { startX, startX, nearX, nearX, landX, landX, startX, startX }, new[] { none, none, none, none, snap });
                loop.Keys(p.Box, Canvas.TopProperty, times, new[] { startY, startY, nearY, nearY, landY, landY, startY, startY }, new[] { none, none, none, none, snap });
                double[] sizeTimes = { 0, t + drop, t + drop + 0.16, t + back, t + back + 0.01, len };
                var sizeValues = new[] { 1, 1, boxScale, boxScale, 1, 1 };
                // Through the element: a storyboard aimed at the transform object itself does nothing.
                loop.Keys(p.Box, new PropertyPath("RenderTransform.ScaleX"), sizeTimes, sizeValues);
                loop.Keys(p.Box, new PropertyPath("RenderTransform.ScaleY"), sizeTimes, sizeValues);
                loop.Show(p.Box, t + 0.05, t + fade);
                loop.Steps(p.Box, Border.CornerRadiusProperty, new[] { 0, t + drop, t + back },
                    new object[] { new CornerRadius(7), p.Stacked ? new CornerRadius(7, 7, 0, 0) : new CornerRadius(0, 7, 7, 0), new CornerRadius(7) });

                sideOpacity.Add(new[] { t + 0.05, 0 }); sideOpacity.Add(new[] { t + 0.35, 1 });
                sideOpacity.Add(new[] { t + fade, 1 }); sideOpacity.Add(new[] { t + fade + 0.3, 0 });
                sideSize.Add(new[] { t + drop, 1 }); sideSize.Add(new[] { t + drop + 0.16, sideScale });
                sideSize.Add(new[] { t + back, sideScale }); sideSize.Add(new[] { t + back + 0.01, 1 });
                nudgeKeys.Add(new[] { t + drop + 0.18, 1 }); nudgeKeys.Add(new[] { t + drop + 0.29, 1.03 }); nudgeKeys.Add(new[] { t + drop + 0.40, 1 });
                cornerTimes.Add(t + drop);
                cornerValues.Add(p.Stacked ? new CornerRadius(0, 0, r.BottomRight, r.BottomLeft) : new CornerRadius(r.TopLeft, 0, 0, r.BottomLeft));
                cornerTimes.Add(t + back); cornerValues.Add(r);

                // The offer, while the box is held within reach (PanelDock.PlaceOffer): the outline of its
                // place beside the sidebar as it is now, as wide (or tall) as the larger of the two, and the
                // two edges that will meet, pulsing.
                if (p.Stacked)
                {
                    Edge(p.Ghost, sb.X, sb.Y - bh, Math.Max(bw, sb.Width), bh);
                    Edge(p.BarA, nearX, nearY + bh - 1.5, bw, 3);
                    Edge(p.BarB, sb.X, sb.Y - 1.5, sb.Width, 3);
                }
                else
                {
                    Edge(p.Ghost, sb.Right, sb.Y, bw, Math.Max(bh, sb.Height));
                    Edge(p.BarA, nearX - 1.5, nearY, 3, bh);
                    Edge(p.BarB, sb.Right - 1.5, sb.Y, 3, sb.Height);
                }
                double on = t + carried + 0.05;
                loop.Keys(p.Ghost, UIElement.OpacityProperty, new[] { 0, on, on + 0.01, t + drop, t + drop + 0.01, len }, new[] { 0.0, 0, 1, 1, 0, 0 });
                foreach (var b in new[] { p.BarA, p.BarB })
                    loop.Keys(b, UIElement.OpacityProperty, new[] { 0, on, on + 0.01, on + 0.21, on + 0.42, on + 0.63, t + drop, t + drop + 0.01, len },
                        new[] { 0.0, 0, 1, 0.45, 1, 0.45, 1, 0, 0 });

                // The seam of the joined panel lit from its middle out, then faded; sparks jump off it.
                double sx, sy, sl;
                if (p.Stacked) { sx = sb.X; sy = sb.Y; sl = Math.Max(bw * boxScale, sb.Width * sideScale); Edge(p.Seam, sx, sy - 2, sl, 4); }
                else { sx = landX; sy = sb.Y; sl = Math.Max(bh * boxScale, sb.Height * sideScale); Edge(p.Seam, sx - 2, sy, 4, sl); }
                loop.Keys(p.Seam, new PropertyPath(p.Stacked ? "RenderTransform.ScaleX" : "RenderTransform.ScaleY"),
                    new[] { 0, t + drop + 0.14, t + drop + 0.32, t + drop + 0.8, t + drop + 0.81, len }, new[] { 0.0, 0, 1, 1, 0, 0 });
                loop.Keys(p.Seam, UIElement.OpacityProperty, new[] { 0, t + drop + 0.13, t + drop + 0.14, t + drop + 0.33, t + drop + 0.75, len },
                    new[] { 0.0, 0, 1, 1, 0, 0 });
                var rnd = new Random(7 + n);
                for (int i = 0; i < p.Sparks.Count; i++)
                {
                    var spark = p.Sparks[i];
                    double size = 3 + rnd.NextDouble() * 3, along = (i + 0.5) / p.Sparks.Count * sl;
                    spark.Width = size; spark.Height = size;
                    double x = p.Stacked ? sx + along : sx - size / 2, y = p.Stacked ? sy - size / 2 : sy + along;
                    double start = t + drop + 0.24 + rnd.NextDouble() * 0.12, time = 0.32 + rnd.NextDouble() * 0.16;
                    double off = (i % 2 == 0 ? -1 : 1) * (14 + rnd.NextDouble() * 18), drift = (rnd.NextDouble() - 0.5) * 20;
                    double toX = p.Stacked ? x + drift : x + off, toY = p.Stacked ? y + off : y + drift;
                    loop.Keys(spark, Canvas.LeftProperty, new[] { 0, start, start + time, len }, new[] { x, x, toX, toX });
                    loop.Keys(spark, Canvas.TopProperty, new[] { 0, start, start + time, len }, new[] { y, y, toY, toY });
                    loop.Keys(spark, UIElement.OpacityProperty, new[] { 0, start, start + 0.02, start + time, len }, new[] { 0.0, 0, 1, 0, 0 });
                }

                // The cursor takes the box by its middle, carries it, lets go and leaves.
                Point at = onStage(startX + bw / 2, startY + bh / 2), near = onStage(nearX + bw / 2, nearY + bh / 2);
                cursorKeys.Add(new[] { t + 0.3, rest.X, rest.Y });
                cursorKeys.Add(new[] { t + grab, at.X, at.Y });
                cursorKeys.Add(new[] { t + 0.9, at.X, at.Y });
                cursorKeys.Add(new[] { t + carried, near.X, near.Y });
                cursorKeys.Add(new[] { t + drop + 0.1, near.X, near.Y });
                cursorKeys.Add(new[] { t + drop + 0.8, rest.X, rest.Y });
                Click(stage, loop, at.X, at.Y, t + grab + 0.02);
            }

            Action<DependencyObject, object, List<double[]>> keys = (target, property, list) =>
            {
                list.Add(new[] { len, list[list.Count - 1][1] });
                loop.Keys(target, property, list.Select(a => a[0]).ToArray(), list.Select(a => a[1]).ToArray());
            };
            root.Opacity = 0;
            keys(root, UIElement.OpacityProperty, sideOpacity);
            // Its size from its top-left corner (where it stays), then the nudge about its middle.
            var grow = new ScaleTransform(1, 1);
            var nudge = new ScaleTransform(1, 1, sb.Width / 2, sb.Height / 2);
            root.RenderTransformOrigin = new Point(0, 0);
            root.RenderTransform = new TransformGroup { Children = { nudge, grow } };
            keys(root, new PropertyPath("RenderTransform.Children[1].ScaleY"), new List<double[]>(sideSize));
            keys(root, new PropertyPath("RenderTransform.Children[1].ScaleX"), sideSize);
            keys(root, new PropertyPath("RenderTransform.Children[0].ScaleY"), new List<double[]>(nudgeKeys));
            keys(root, new PropertyPath("RenderTransform.Children[0].ScaleX"), nudgeKeys);
            loop.Steps(root, Border.CornerRadiusProperty, cornerTimes.ToArray(), cornerValues.ToArray());
            cursorKeys.Add(new[] { len, rest.X, rest.Y });
            loop.Move(cursor, cursorKeys.Select(a => a[0]).ToArray(), cursorKeys.Select(a => a[1]).ToArray(), cursorKeys.Select(a => a[2]).ToArray());
            stage.Children.Remove(cursor); stage.Children.Add(cursor);
            loop.RunNow(stage);
            AssembleSeek = t => loop.SeekTo(stage, t);
            AssembleLength = len;
            AssembleView = stage.Parent as FrameworkElement ?? stage;
        }

        /// <summary>Test aid: the last "Assemble panels" picture built — hold it at a time, its loop
        /// length, the element to render (to make frames of it).</summary>
        internal static Action<double> AssembleSeek;
        internal static double AssembleLength;
        internal static FrameworkElement AssembleView;

        private static void Edge(FrameworkElement el, double x, double y, double w, double h)
        {
            Canvas.SetLeft(el, x); Canvas.SetTop(el, y); el.Width = Math.Max(0, w); el.Height = Math.Max(0, h);
        }

        private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    }
}
