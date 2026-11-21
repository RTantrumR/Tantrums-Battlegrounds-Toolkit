using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using HsbgCardLookup.Config;
using HsbgCardLookup.Localization;

namespace HsbgCardLookup.Ui
{
    /// <summary>
    /// Settings' small pictures of what a switch does, in the manner of <see cref="AssembleDemo"/>:
    /// drawn shapes on a stage, most of them one looping storyboard that runs only while the picture
    /// is on screen. Each follows the settings it stands next to (the page rebuilds or refreshes it).
    /// </summary>
    internal static class SettingsDemos
    {
        public const double StageW = 480;
        private static readonly Brush StageBg = Frozen(Color.FromRgb(0x1B, 0x22, 0x2E));
        private static readonly Brush BoxBg = Frozen(Color.FromArgb(0xDC, 0x0A, 0x0D, 0x14));
        private static readonly Brush Felt = Frozen(Color.FromRgb(0x2A, 0x3B, 0x52));

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

        private static Border Box(double w, double h, double corner = 7) => new Border
        {
            Width = w, Height = h, Background = BoxBg, BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(corner)
        };

        private static void Put(Canvas stage, UIElement el, double x, double y)
        {
            Canvas.SetLeft(el, x); Canvas.SetTop(el, y);
            stage.Children.Add(el);
        }

        // ── Turn counter: the box as it is drawn, in the corner it starts in ────────────────────

        public static FrameworkElement TurnCounter()
        {
            var stage = new Canvas { Width = StageW, Height = 62 };
            // The game's top edge, hinted: where the box sits by default.
            Put(stage, new Rectangle { Width = StageW, Height = 62, Fill = Felt, Opacity = 0.35 }, 0, 0);
            var box = new Border
            {
                Background = BoxBg, BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10, 4, 10, 5),
                Child = new TextBlock { Text = Strings.F("turnCounter.text", 7), Foreground = UiKit.TextPrimary, FontWeight = FontWeights.SemiBold, FontSize = 17 }
            };
            box.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Put(stage, box, StageW - box.DesiredSize.Width - 12, 12);
            return Stage(stage);
        }

        // ── Lobby types: the box itself, by the layout, look and banned-types settings ──────────

        public static FrameworkElement LobbyTypes(PluginConfig config, out Action refresh)
        {
            var box = LobbyTribesPanel.Preview(config, out refresh);
            return Stage(new Grid { MinHeight = 76, Children = { new Border { Padding = new Thickness(12), Child = box } } });
        }

        // ── Opponents' boards: hover shows, a click keeps (or, with pinning off, leaving hides) ─

        public static FrameworkElement BoardsHover(bool pin)
        {
            double len = pin ? 6.4 : 4.6;
            var stage = new Canvas { Width = StageW, Height = 150 };
            var loop = new Loop(len);

            double[] tops = { 12, 58, 104 };
            Ellipse hovered = null;
            for (int i = 0; i < tops.Length; i++)
            {
                var portrait = new Ellipse
                {
                    Width = 36, Height = 36, StrokeThickness = 2, Stroke = Frozen(Color.FromRgb(0x8A, 0x6A, 0x2C)),
                    Fill = new RadialGradientBrush(Color.FromRgb(0x6B, 0x55, 0x3A), Color.FromRgb(0x2B, 0x22, 0x18))
                };
                Put(stage, portrait, 22, tops[i]);
                if (i == 1) hovered = portrait;
            }

            // The board that portrait's player last fought with: five minions in a row.
            var minions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            foreach (string tribe in new[] { "Beast", "Mech", "Dragon", "Murloc", "Undead" })
                minions.Children.Add(new Ellipse { Width = 38, Height = 50, Margin = new Thickness(5, 0, 5, 0), Fill = TribeIcons.BrushFor(tribe), Stroke = TribeIcons.Ring, StrokeThickness = 1.5 });
            var board = Box(270, 84);
            board.Child = minions;
            Put(stage, board, 84, 34);
            var kept = new Rectangle { Width = 274, Height = 88, RadiusX = 9, RadiusY = 9, Stroke = UiKit.AccentBrush, StrokeThickness = 2, Opacity = 0 };
            Put(stage, kept, 82, 32);
            var ring = new Ellipse { Width = 42, Height = 42, Stroke = UiKit.AccentBrush, StrokeThickness = 2.5, Opacity = 0 };
            Put(stage, ring, 19, 55);

            var cursor = Cursor();
            Put(stage, cursor, 250, 128);
            const double px = 40, py = 78, awayX = 330, awayY = 128;      // the cursor's tip on the portrait, and off it
            if (pin)
            {
                loop.Move(cursor, new[] { 0, 0.3, 1.0, 2.2, 3.0, 4.3, 5.0, len },
                    new[] { 250.0, 250, px, px, awayX, awayX, px, px }, new[] { 128.0, 128, py, py, awayY, awayY, py, py });
                loop.Show(board, 1.0, 5.4);
                Click(stage, loop, px, py, 1.9);
                loop.Show(kept, 1.95, 5.3);
                loop.Show(ring, 1.95, 5.3);
                Click(stage, loop, px, py, 5.3);
            }
            else
            {
                loop.Move(cursor, new[] { 0, 0.3, 1.0, 2.6, 3.4, len },
                    new[] { 250.0, 250, px, px, awayX, awayX }, new[] { 128.0, 128, py, py, awayY, awayY });
                loop.Show(board, 1.0, 2.7);
            }
            // The cursor goes last so it passes over everything.
            stage.Children.Remove(cursor); stage.Children.Add(cursor);
            loop.Run(stage);
            return Stage(stage);
        }

        // ── Card search: the key opens it, a card is dragged out, the key closes it again ───────

        public static FrameworkElement SearchFlow(PluginConfig config)
        {
            const double len = 6.6;
            var stage = new Canvas { Width = StageW, Height = 168 };
            var loop = new Loop(len);
            bool drag = config.DragFromGrid || config.DragFromDetail;

            string key = string.IsNullOrEmpty(config.BrowserKey) || config.BrowserKey == "None" ? "—" : config.BrowserKey;
            var cap = new Border
            {
                MinWidth = 44, Height = 34, Padding = new Thickness(9, 0, 9, 0), CornerRadius = new CornerRadius(6),
                Background = Frozen(Color.FromRgb(0x2A, 0x33, 0x45)), BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(1, 1, 1, 3),
                Child = new TextBlock { Text = key, Foreground = UiKit.TextPrimary, FontWeight = FontWeights.SemiBold, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            Put(stage, cap, 22, 112);
            double closeAt = drag ? 4.4 : 3.2;
            // A press: the cap sinks two pixels and lights up.
            loop.Keys(cap, Canvas.TopProperty,
                new[] { 0, 0.5, 0.6, 0.85, 0.95, closeAt, closeAt + 0.1, closeAt + 0.35, closeAt + 0.45, len },
                new[] { 112.0, 112, 114, 114, 112, 112, 114, 114, 112, 112 });
            var glow = new Rectangle { Width = 60, Height = 44, RadiusX = 9, RadiusY = 9, Fill = UiKit.AccentBrush, Opacity = 0, Effect = new BlurEffect { Radius = 12 } };
            Put(stage, glow, 14, 107);
            stage.Children.Remove(glow); stage.Children.Insert(0, glow);
            loop.Keys(glow, UIElement.OpacityProperty,
                new[] { 0, 0.5, 0.65, 1.0, closeAt, closeAt + 0.15, closeAt + 0.5, len }, new[] { 0.0, 0, 0.55, 0, 0, 0.55, 0, 0 });

            // The search overlay: a bar and two rows of cards.
            var overlay = Box(230, 140, 10);
            var inside = new StackPanel { Margin = new Thickness(10, 9, 10, 8) };
            inside.Children.Add(new Border { Height = 16, CornerRadius = new CornerRadius(8), Background = Frozen(Color.FromRgb(0x24, 0x2E, 0x42)), Margin = new Thickness(0, 0, 0, 8) });
            const double cardW = 34, cardH = 42;
            for (int r = 0; r < 2; r++)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                for (int c = 0; c < 5; c++)
                    row.Children.Add(Card(cardW, cardH, r == 0 && c == 4 && drag ? 0.25 : 1));
                inside.Children.Add(row);
            }
            overlay.Child = inside;
            Put(stage, overlay, 112, 14);
            loop.Show(overlay, 0.75, closeAt + 0.15);

            if (drag)
            {
                // Its fifth card is pulled out and left on the screen.
                double fromX = 112 + 11 + 4 * (cardW + 4) + 2, fromY = 14 + 10 + 24;
                const double toX = 392, toY = 58;
                var card = Card(cardW, cardH, 1);
                card.Margin = new Thickness(0);
                card.BorderBrush = UiKit.AccentBrush;
                Put(stage, card, fromX, fromY);
                loop.Move(card, new[] { 0, 2.0, 3.2, len }, new[] { fromX, fromX, toX, toX }, new[] { fromY, fromY, toY, toY });
                // "Hide dragged cards with app": it goes with the overlay, or it stays a while longer.
                loop.Show(card, 1.9, config.HideDraggedWithApp ? closeAt + 0.15 : len - 0.6);
                var cursor = Cursor();
                Put(stage, cursor, 300, 150);
                loop.Move(cursor, new[] { 0, 1.2, 2.0, 3.2, 3.9, len },
                    new[] { 300.0, 300, fromX + 14, toX + 14, 330, 330 }, new[] { 150.0, 150, fromY + 20, toY + 20, 150, 150 });
            }
            loop.Run(stage);
            return Stage(stage);
        }

        private static Border Card(double w, double h, double opacity) => new Border
        {
            Width = w, Height = h, CornerRadius = new CornerRadius(4), Margin = new Thickness(2, 0, 2, 0), Opacity = opacity,
            Background = new LinearGradientBrush(Color.FromRgb(0x4A, 0x3B, 0x6B), Color.FromRgb(0x2A, 0x24, 0x40), 90),
            BorderBrush = Frozen(Color.FromRgb(0x6C, 0x5A, 0x96)), BorderThickness = new Thickness(1)
        };

        // ── Start with Windows: the icon waits in the tray, comes alive with the game, rests again ─

        public static FrameworkElement Autostart()
        {
            const double len = 6.4;
            var stage = new Canvas { Width = StageW, Height = 124 };
            var loop = new Loop(len);

            // The desktop's taskbar, the tray at its right end.
            Put(stage, new Rectangle { Width = StageW, Height = 28, Fill = Frozen(Color.FromRgb(0x10, 0x14, 0x1C)) }, 0, 96);
            Put(stage, new Rectangle { Width = StageW, Height = 1, Fill = UiKit.StrokeBrush }, 0, 96);

            // The game: a window, and its button on the taskbar.
            var game = new Border
            {
                Width = 210, Height = 78, CornerRadius = new CornerRadius(6), BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(1),
                Background = new LinearGradientBrush(Color.FromRgb(0x2B, 0x3F, 0x5C), Color.FromRgb(0x16, 0x20, 0x32), 90),
                Child = new Ellipse { Width = 150, Height = 46, Fill = Felt, Opacity = 0.9, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            Put(stage, game, 135, 9);
            var button = new Rectangle { Width = 34, Height = 16, RadiusX = 3, RadiusY = 3, Fill = Frozen(Color.FromRgb(0x2B, 0x3F, 0x5C)), Stroke = UiKit.StrokeBrush };
            Put(stage, button, 20, 102);
            loop.Show(game, 1.3, 4.4);
            loop.Show(button, 1.3, 4.4);

            // The toolkit's own boxes over the game, once it has come up.
            var boxA = Box(46, 14, 4); var boxB = Box(30, 40, 4);
            Put(stage, boxA, 143, 16); Put(stage, boxB, 306, 38);
            loop.Show(boxA, 2.2, 4.4); loop.Show(boxB, 2.2, 4.4);

            // The tray icon: dim while it waits, lit while the toolkit runs.
            FrameworkElement icon;
            var logo = TrayFlight.LogoImage;
            if (logo != null) icon = new Image { Source = logo, Width = 20, Height = 20 };
            else icon = new Ellipse { Width = 18, Height = 18, Fill = UiKit.AccentBrush };
            Put(stage, icon, 440, 100);
            var lit = new Ellipse { Width = 34, Height = 34, Fill = UiKit.AccentBrush, Opacity = 0, Effect = new BlurEffect { Radius = 14 } };
            Put(stage, lit, 433, 93);
            stage.Children.Remove(lit); stage.Children.Insert(2, lit);
            loop.Keys(icon, UIElement.OpacityProperty, new[] { 0, 2.0, 2.3, 4.6, 4.9, len }, new[] { 0.4, 0.4, 1, 1, 0.4, 0.4 });
            loop.Keys(lit, UIElement.OpacityProperty, new[] { 0, 2.0, 2.3, 4.6, 4.9, len }, new[] { 0.0, 0, 0.5, 0.5, 0, 0 });

            loop.Run(stage);
            return Stage(stage);
        }

        private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    }
}
