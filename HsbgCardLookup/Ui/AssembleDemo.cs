using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace HsbgCardLookup.Ui
{
    /// <summary>
    /// Settings' picture of what "Assemble panels" does: a small lobby types box is carried to a small
    /// sidebar, the two sides that will meet light up, and the boxes join into one. Drawn boxes, not
    /// the live panels; one looping storyboard that runs only while the picture is on screen.
    /// </summary>
    public static class AssembleDemo
    {
        private static readonly Color Spark = Color.FromRgb(0x7F, 0xE3, 0xFF);
        private static readonly Brush BoxBg = Frozen(Color.FromRgb(0x0A, 0x0D, 0x14));

        public static FrameworkElement Build()
        {
            const double w = 150, topH = 40, sideH = 96, x = 176, y = 30;       // the joined place
            var stage = new Canvas { Width = 496, Height = 176, ClipToBounds = true };

            var types = Box(w, topH);
            var icons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            foreach (string tribe in new[] { "Beast", "Dragon", "Murloc", "Pirate", "Undead" })
                icons.Children.Add(new Ellipse { Width = 20, Height = 20, Margin = new Thickness(3, 0, 3, 0), Fill = TribeIcons.BrushFor(tribe), Stroke = TribeIcons.Ring, StrokeThickness = 1 });
            types.Child = icons;

            var side = Box(w, sideH);
            var rows = new StackPanel { Margin = new Thickness(9, 7, 9, 7) };
            double[] names = { 78, 60, 88, 70, 82, 64 };
            for (int i = 0; i < names.Length; i++)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = false };
                var rating = new Rectangle { Width = 26, Height = 7, RadiusX = 3, RadiusY = 3, Fill = UiKit.AccentBrush, Opacity = 0.8 };
                DockPanel.SetDock(rating, Dock.Right);
                row.Children.Add(rating);
                row.Children.Add(new Rectangle { Width = names[i], Height = 7, RadiusX = 3, RadiusY = 3, Fill = UiKit.TextSecondary, Opacity = 0.75 });
                rows.Children.Add(row);
            }
            side.Child = rows;
            Canvas.SetLeft(side, x); Canvas.SetTop(side, y + topH - 1);

            var barA = Bar(w); var barB = Bar(w);
            Canvas.SetLeft(barB, x); Canvas.SetTop(barB, y + topH - 2.5);
            var seam = new Rectangle
            {
                Width = w, Height = 4, Fill = Brushes.White, RadiusX = 2, RadiusY = 2, Opacity = 0,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(0, 1),
                Effect = new DropShadowEffect { Color = Spark, BlurRadius = 18, ShadowDepth = 0, Opacity = 1 }
            };
            Canvas.SetLeft(seam, x); Canvas.SetTop(seam, y + topH - 3);

            foreach (UIElement el in new UIElement[] { side, types, barA, barB, seam }) stage.Children.Add(el);

            // One pass: carry, offer, join, rest, fade.
            var story = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
            Func<double, KeyTime> at = s => KeyTime.FromTimeSpan(TimeSpan.FromSeconds(s));
            Action<DependencyObject, object, Timeline> add = (target, property, line) =>
            {
                Storyboard.SetTarget(line, target);
                Storyboard.SetTargetProperty(line, property as PropertyPath ?? new PropertyPath((DependencyProperty)property));
                story.Children.Add(line);
            };
            Func<double[], double[], IEasingFunction, DoubleAnimationUsingKeyFrames> keys = (times, values, ease) =>
            {
                var k = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(6) };
                for (int i = 0; i < times.Length; i++)
                    k.KeyFrames.Add(i > 0 && ease != null && i == times.Length - 2
                        ? (DoubleKeyFrame)new EasingDoubleKeyFrame(values[i], at(times[i]), ease)
                        : new EasingDoubleKeyFrame(values[i], at(times[i]), new CubicEase { EasingMode = EasingMode.EaseInOut }));
                return k;
            };
            var snap = new BackEase { Amplitude = 0.7, EasingMode = EasingMode.EaseOut };
            double gap = 14;
            //                              start   carried        joined         rest   fade
            double[] t = { 0, 0.3, 1.7, 2.6, 2.9, 5.2, 5.7, 6.0 };
            add(types, Canvas.LeftProperty, keys(t, new[] { 26.0, 26, x + 6, x + 6, x, x, x, x }, snap));
            add(types, Canvas.TopProperty, keys(t, new[] { 6.0, 6, y - gap, y - gap, y, y, y, y }, snap));
            add(barA, Canvas.LeftProperty, keys(t, new[] { 26.0, 26, x + 6, x + 6, x, x, x, x }, snap));
            add(barA, Canvas.TopProperty, keys(t, new[] { 6.0 + topH - 1.5, 6 + topH - 1.5, y - gap + topH - 1.5, y - gap + topH - 1.5, y + topH - 1.5, y + topH - 1.5, y + topH - 1.5, y + topH - 1.5 }, snap));
            foreach (var bar in new[] { barA, barB })
                add(bar, UIElement.OpacityProperty, keys(new[] { 0, 1.6, 1.9, 2.2, 2.5, 2.75, 3.0, 6.0 }, new[] { 0.0, 0, 1, 0.5, 1, 1, 0, 0 }, null));
            add(seam, UIElement.OpacityProperty, keys(new[] { 0, 2.85, 2.95, 3.25, 3.8, 6.0 }, new[] { 0.0, 0, 1, 1, 0, 0 }, null));
            add(seam, new PropertyPath("RenderTransform.ScaleX"), keys(new[] { 0, 2.9, 3.2, 6.0 }, new[] { 0.0, 0, 1, 1 }, null));
            foreach (var box in new[] { types, side })
                add(box, UIElement.OpacityProperty, keys(new[] { 0, 0.3, 5.3, 5.8, 6.0 }, new[] { 0.0, 1, 1, 0, 0 }, null));

            // The corners on the seam square off as the boxes meet.
            Action<Border, CornerRadius> corners = (box, joined) =>
            {
                var k = new ObjectAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(6) };
                k.KeyFrames.Add(new DiscreteObjectKeyFrame(new CornerRadius(7), at(0)));
                k.KeyFrames.Add(new DiscreteObjectKeyFrame(joined, at(2.85)));
                add(box, Border.CornerRadiusProperty, k);
            };
            corners(types, new CornerRadius(7, 7, 0, 0));
            corners(side, new CornerRadius(0, 0, 7, 7));

            stage.Loaded += (s, e) => { try { story.Begin(stage, true); } catch { } };
            stage.Unloaded += (s, e) => { try { story.Stop(stage); } catch { } };

            return new Border
            {
                Child = stage, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 2, 0, 12),
                Background = Frozen(Color.FromRgb(0x1B, 0x22, 0x2E)), BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left
            };
        }

        private static Border Box(double w, double h) => new Border
        {
            Width = w, Height = h, Background = BoxBg, BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7), Opacity = 0
        };

        private static Rectangle Bar(double w) => new Rectangle
        {
            Width = w, Height = 3, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(Spark), Opacity = 0,
            Effect = new DropShadowEffect { Color = Spark, BlurRadius = 12, ShadowDepth = 0, Opacity = 1 }
        };

        private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    }
}
