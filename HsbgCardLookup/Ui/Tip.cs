using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace HsbgCardLookup.Ui
{
    internal enum TipKind { Info, Warning }

    /// <summary>
    /// The toolkit's own tooltip: a dark panel in the window's colours that wraps its text at a
    /// readable width, instead of the small one-line box Windows draws for a plain string tooltip.
    /// <c>Tip.Set(element, text)</c> wherever a <c>ToolTip = "…"</c> would go; an optional title line
    /// sits above the text, and <see cref="TipKind.Warning"/> draws it in red.
    ///
    /// It opens just under its element (not at the cursor), sooner than Windows' default and stays
    /// while the cursor is on the element.
    /// </summary>
    internal static class Tip
    {
        private const double MaxWidth = 340;
        private static readonly Color WarnRed = Color.FromRgb(0xFF, 0x6B, 0x6B);
        private static readonly Brush Panel = Frozen(Color.FromRgb(0x16, 0x1C, 0x28));
        private static readonly Brush WarnRim = Frozen(Color.FromRgb(0xB8, 0x3A, 0x3A));

        // The tooltip draws nothing of its own: the panel inside it is the whole look.
        private static readonly ControlTemplate Bare = MakeBare();

        public static void Set(FrameworkElement el, string text, TipKind kind = TipKind.Info, string title = null)
        {
            if (el == null) return;
            if (string.IsNullOrEmpty(text) && string.IsNullOrEmpty(title)) { el.ToolTip = null; return; }
            var tip = Make(text, kind, title);
            // Under the element; lined up with its right edge when it sits in the right half of its
            // window, so a tip for a title-bar icon opens over the window instead of past its edge.
            tip.Placement = PlacementMode.Custom;
            tip.CustomPopupPlacementCallback = (popup, target, offset) =>
            {
                bool right = false;
                try
                {
                    var win = Window.GetWindow(el);
                    if (win != null && win.ActualWidth > 0)
                        right = el.TranslatePoint(new Point(target.Width / 2, 0), win).X > win.ActualWidth / 2;
                }
                catch { }
                double leftX = 0, rightX = target.Width - popup.Width;
                return new[]
                {
                    new CustomPopupPlacement(new Point(right ? rightX : leftX, target.Height), PopupPrimaryAxis.Horizontal),
                    new CustomPopupPlacement(new Point(right ? leftX : rightX, target.Height), PopupPrimaryAxis.Horizontal),
                    new CustomPopupPlacement(new Point(right ? rightX : leftX, -popup.Height), PopupPrimaryAxis.Horizontal),
                };
            };
            el.ToolTip = tip;
            ToolTipService.SetPlacement(el, PlacementMode.Custom);
            ToolTipService.SetInitialShowDelay(el, 250);
            ToolTipService.SetBetweenShowDelay(el, 100);
            ToolTipService.SetShowDuration(el, 60000);
        }

        public static ToolTip Make(string text, TipKind kind = TipKind.Info, string title = null)
        {
            var body = new StackPanel();
            if (!string.IsNullOrEmpty(title))
                body.Children.Add(new TextBlock
                {
                    Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
                    Foreground = kind == TipKind.Warning ? Frozen(WarnRed) : UiKit.AccentBrush, Margin = new Thickness(0, 0, 0, 4)
                });
            if (!string.IsNullOrEmpty(text))
                body.Children.Add(new TextBlock
                {
                    Text = text, FontSize = 13, LineHeight = 18.5, TextWrapping = TextWrapping.Wrap,
                    Foreground = kind == TipKind.Warning && string.IsNullOrEmpty(title) ? Frozen(WarnRed) : UiKit.TextPrimary
                });

            var card = new Border
            {
                Background = Panel, BorderBrush = kind == TipKind.Warning ? WarnRim : UiKit.StrokeBrush, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 10), MaxWidth = MaxWidth,
                Margin = new Thickness(6, 2, 6, 10),   // room for the shadow inside the popup
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Direction = 270, Opacity = 0.55, Color = Colors.Black },
                Child = body
            };
            TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);

            return new ToolTip
            {
                Template = Bare, Content = card, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Padding = new Thickness(0), HasDropShadow = false
            };
        }

        private static ControlTemplate MakeBare()
        {
            var t = new ControlTemplate(typeof(ToolTip)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
            t.Seal();
            return t;
        }

        private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    }
}
