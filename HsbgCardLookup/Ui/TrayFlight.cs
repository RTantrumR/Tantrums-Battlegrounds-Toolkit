using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace HsbgCardLookup.Ui
{
    /// <summary>
    /// What a closing settings window turns into: its picture folds into the toolkit's logo, and the
    /// logo flies to the toolkit's icon in the Windows tray, so the place to open the settings from
    /// again is seen every time they close. Only a host with a tray icon sets <see cref="Target"/>;
    /// without it (the HDT plugin) the window simply closes.
    ///
    /// The flight is a window of its own (no activation, clicks pass through), started as the settings
    /// window closes: the close itself is never held back, and anything going wrong here costs the
    /// animation, nothing else.
    /// </summary>
    public static class TrayFlight
    {
        /// <summary>The tray icon's place on screen, in device pixels; null when it cannot be told.</summary>
        public static Func<Rect?> Target;
        /// <summary>The logo's file (an .ico: its largest picture is used).</summary>
        public static string LogoPath;
        /// <summary>The flight window's <see cref="FrameworkElement.Tag"/>: a host that copies the
        /// toolkit's windows somewhere (the standalone's overlay mirrors them for OBS) leaves this one
        /// out, like the settings window it comes from.</summary>
        public const string NoMirror = "toolkit:no-mirror";

        private const double LogoSize = 72;
        private static ImageSource _logo;

        /// <summary>Call from the window's Closing: takes its picture and place, and plays.</summary>
        public static void From(Window window)
        {
            try
            {
                if (Target == null || window == null || !window.IsVisible || !SystemParameters.ClientAreaAnimation) return;
                var source = PresentationSource.FromVisual(window);
                if (source?.CompositionTarget == null) return;
                Rect? device = Target();
                if (device == null) return;
                var toDip = source.CompositionTarget.TransformFromDevice;
                var to = new Rect(toDip.Transform(device.Value.TopLeft), toDip.Transform(device.Value.BottomRight));
                var from = new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
                if (from.Width < 1 || from.Height < 1 || to.Width < 1) return;

                var shot = new RenderTargetBitmap((int)Math.Ceiling(from.Width), (int)Math.Ceiling(from.Height), 96, 96, PixelFormats.Pbgra32);
                shot.Render(window);
                shot.Freeze();
                Play(shot, from, to, Logo());
            }
            catch { }
        }

        /// <summary>The toolkit's logo, or null where the host has none to show.</summary>
        public static ImageSource LogoImage => LogoPath == null ? null : Logo();

        private static ImageSource Logo()
        {
            if (_logo != null) return _logo;
            try
            {
                var decoder = BitmapDecoder.Create(new Uri(LogoPath), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                BitmapFrame best = null;
                foreach (var frame in decoder.Frames)
                    if (best == null || frame.PixelWidth > best.PixelWidth) best = frame;
                best?.Freeze();
                _logo = best;
            }
            catch { }
            return _logo;
        }

        private static void Play(ImageSource shot, Rect from, Rect to, ImageSource logo)
        {
            if (logo == null) return;
            // One window over both ends of the flight, a little room around for the logo.
            var area = Rect.Union(from, to);
            area.Inflate(LogoSize, LogoSize);
            var stage = new Canvas { Width = area.Width, Height = area.Height };

            var picture = new Image { Source = shot, Width = from.Width, Height = from.Height, RenderTransformOrigin = new Point(0.5, 0.5) };
            var fold = new ScaleTransform(1, 1);
            picture.RenderTransform = fold;
            Canvas.SetLeft(picture, from.Left - area.Left);
            Canvas.SetTop(picture, from.Top - area.Top);
            stage.Children.Add(picture);

            var start = new Point(from.Left + from.Width / 2 - area.Left, from.Top + from.Height / 2 - area.Top);
            var end = new Point(to.Left + to.Width / 2 - area.Left, to.Top + to.Height / 2 - area.Top);
            var mark = new Image { Source = logo, Width = LogoSize, Height = LogoSize, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5) };
            var size = new ScaleTransform(1.5, 1.5);
            var move = new TranslateTransform(0, 0);
            var both = new TransformGroup();
            both.Children.Add(size);
            both.Children.Add(move);
            mark.RenderTransform = both;
            Canvas.SetLeft(mark, start.X - LogoSize / 2);
            Canvas.SetTop(mark, start.Y - LogoSize / 2);
            stage.Children.Add(mark);

            var window = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                Topmost = true, ShowInTaskbar = false, ShowActivated = false, ResizeMode = ResizeMode.NoResize, Tag = NoMirror,
                IsHitTestVisible = false, Focusable = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = area.Left, Top = area.Top, Width = area.Width, Height = area.Height,
                Content = stage
            };
            window.SourceInitialized += (s, e) =>
            {
                try
                {
                    var handle = new WindowInteropHelper(window).Handle;
                    SetWindowLong(handle, GWL_EXSTYLE, GetWindowLong(handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
                }
                catch { }
            };

            // 0–260 ms: the window's picture folds toward its middle and gives way to the logo.
            // 240–660 ms: the logo flies to the tray icon, shrinking to its size, and is gone on arrival.
            var foldTime = TimeSpan.FromMilliseconds(260);
            var flyAt = TimeSpan.FromMilliseconds(240);
            var flyTime = TimeSpan.FromMilliseconds(420);
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            Func<double, double, TimeSpan, TimeSpan, IEasingFunction, DoubleAnimation> anim = (a, b, at, time, easing) =>
                new DoubleAnimation(a, b, new Duration(time)) { BeginTime = at, EasingFunction = easing, FillBehavior = FillBehavior.HoldEnd };

            fold.BeginAnimation(ScaleTransform.ScaleXProperty, anim(1, 0.1, TimeSpan.Zero, foldTime, ease));
            fold.BeginAnimation(ScaleTransform.ScaleYProperty, anim(1, 0.1, TimeSpan.Zero, foldTime, ease));
            picture.BeginAnimation(UIElement.OpacityProperty, anim(1, 0, TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(200), null));

            double small = Math.Max(0.15, Math.Min(1, to.Width / LogoSize));
            var showLogo = anim(0, 1, TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(160), null);
            mark.BeginAnimation(UIElement.OpacityProperty, showLogo);
            // The logo's size on one track: a little large as it appears, its own size as the flight
            // begins, the tray icon's size on arrival.
            foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            {
                var track = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
                track.KeyFrames.Add(new LinearDoubleKeyFrame(1.5, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                track.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(foldTime)));
                track.KeyFrames.Add(new EasingDoubleKeyFrame(small, KeyTime.FromTimeSpan(flyAt + flyTime), ease));
                size.BeginAnimation(property, track);
            }

            // Across first, down late: the path bends instead of running straight.
            move.BeginAnimation(TranslateTransform.XProperty,
                anim(0, end.X - start.X, flyAt, flyTime, new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            var down = anim(0, end.Y - start.Y, flyAt, flyTime, new CubicEase { EasingMode = EasingMode.EaseIn });
            down.Completed += (s, e) => { try { window.Close(); } catch { } };
            move.BeginAnimation(TranslateTransform.YProperty, down);

            window.Show();
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020, WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
