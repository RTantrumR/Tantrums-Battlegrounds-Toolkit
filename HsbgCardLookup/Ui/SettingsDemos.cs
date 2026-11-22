using System;
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

            public void Keys(DependencyObject target, object property, double[] times, double[] values)
            {
                var k = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(_seconds) };
                for (int i = 0; i < times.Length; i++)
                    k.KeyFrames.Add(new EasingDoubleKeyFrame(values[i], KeyTime.FromTimeSpan(TimeSpan.FromSeconds(times[i])),
                        new CubicEase { EasingMode = EasingMode.EaseInOut }));
                Storyboard.SetTarget(k, target);
                Storyboard.SetTargetProperty(k, property as PropertyPath ?? new PropertyPath((DependencyProperty)property));
                _story.Children.Add(k);
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

        // ── Start with Windows: the icon waits in the tray, comes alive with the game, rests again ─

        public static FrameworkElement Autostart()
        {
            const double len = 6.4;
            var stage = new Canvas { Width = StageW, Height = 124 };
            var loop = new Loop(len);

            // The desktop's taskbar, the tray at its right end.
            Put(stage, new Rectangle { Width = StageW, Height = 28, Fill = Frozen(Color.FromRgb(0x10, 0x14, 0x1C)) }, 0, 96);
            Put(stage, new Rectangle { Width = StageW, Height = 1, Fill = UiKit.StrokeBrush }, 0, 96);

            // The game: its window (the replay frame), and its button on the taskbar.
            const double gameW = 160;
            var game = new Border
            {
                CornerRadius = new CornerRadius(4), BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(1),
                Background = Brushes.Black, ClipToBounds = true, Child = GameFrame(gameW)
            };
            Put(stage, game, 150, 4);
            var button = new Rectangle { Width = 34, Height = 16, RadiusX = 3, RadiusY = 3, Fill = Frozen(Color.FromRgb(0x2B, 0x3F, 0x5C)), Stroke = UiKit.StrokeBrush };
            Put(stage, button, 20, 102);
            loop.Show(game, 1.3, 4.4);
            loop.Show(button, 1.3, 4.4);

            // The tray icon: dim while it waits, lit while the toolkit runs.
            FrameworkElement icon;
            var logo = TrayFlight.LogoImage;
            if (logo != null) icon = new Image { Source = logo, Width = 20, Height = 20 };
            else icon = new Ellipse { Width = 18, Height = 18, Fill = UiKit.AccentBrush };
            Put(stage, icon, 436, 100);
            var lit = new Ellipse { Width = 34, Height = 34, Fill = UiKit.AccentBrush, Opacity = 0, Effect = new BlurEffect { Radius = 14 } };
            Put(stage, lit, 429, 93);
            stage.Children.Remove(lit); stage.Children.Insert(2, lit);
            loop.Keys(icon, UIElement.OpacityProperty, new[] { 0, 2.0, 2.3, 4.6, 4.9, len }, new[] { 0.4, 0.4, 1, 1, 0.4, 0.4 });
            loop.Keys(lit, UIElement.OpacityProperty, new[] { 0, 2.0, 2.3, 4.6, 4.9, len }, new[] { 0.0, 0, 0.5, 0.5, 0, 0 });

            loop.Run(stage);
            return Stage(stage);
        }

        private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    }
}
