using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace HsbgCardLookup.Ui
{
    /// <summary>
    /// The settings page list's on/off mark: a small domed lamp standing in a steel seat, drawn as seen from
    /// a little below straight above, lit from the top left (owner's pick 2026-11-24: dome, green, 20 px).
    /// Drawn in a 24 x 28 box and scaled; the lit lamp's glow reaches past the box.
    /// </summary>
    internal static class Diode
    {
        private static readonly Color Lit = Color.FromRgb(0x4C, 0xC2, 0x6A), LitHi = Color.FromRgb(0xD9, 0xF7, 0xE0),
            LitLo = Color.FromRgb(0x1F, 0x7A, 0x3A), LitDeep = Color.FromRgb(0x12, 0x4A, 0x23);
        private static readonly Color Off = Color.FromRgb(0x62, 0x6C, 0x7D), OffHi = Color.FromRgb(0xA3, 0xAC, 0xBA),
            OffLo = Color.FromRgb(0x39, 0x41, 0x4F), OffDeep = Color.FromRgb(0x22, 0x28, 0x34);
        private static readonly Brush Ink = Frozen(new SolidColorBrush(Color.FromRgb(0x07, 0x09, 0x0D)));

        private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
        private static Color A(Color c, double opacity) => Color.FromArgb((byte)(opacity * 255), c.R, c.G, c.B);

        /// <summary>The lamp, <paramref name="width"/> pixels wide (its height is 7/6 of that).</summary>
        public static FrameworkElement Create(bool on, double width = 20)
        {
            Color c = on ? Lit : Off, hi = on ? LitHi : OffHi, lo = on ? LitLo : OffLo, deep = on ? LitDeep : OffDeep;
            var canvas = new Canvas { Width = 24, Height = 28 };

            // On the plate: the shadow the piece casts, then the light the lamp throws.
            canvas.Children.Add(Oval(12.7, 20.9, 8.4, 3.6, Frozen(new SolidColorBrush(A(Colors.Black, .55))), new BlurEffect { Radius = 2.2 }));
            if (on)
            {
                var glow = new RadialGradientBrush();
                glow.GradientStops.Add(new GradientStop(A(c, .75), 0));
                glow.GradientStops.Add(new GradientStop(A(c, .28), .45));
                glow.GradientStops.Add(new GradientStop(A(c, 0), 1));
                canvas.Children.Add(Oval(12, 16.6, 15, 9.5, Frozen(glow), null));
            }

            // The steel seat: its low wall, its top, a lit rim, the well the lamp stands in.
            var side = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            side.GradientStops.Add(new GradientStop(Color.FromRgb(0x17, 0x1C, 0x26), 0));
            side.GradientStops.Add(new GradientStop(Color.FromRgb(0x4A, 0x53, 0x61), .4));
            side.GradientStops.Add(new GradientStop(Color.FromRgb(0x0F, 0x13, 0x1B), 1));
            canvas.Children.Add(Shape("M3.4,14.6 v1.3 a8.6,8.6 0 0 0 17.2,0 v-1.3 z", Frozen(side), Ink, .6));
            var steel = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            steel.GradientStops.Add(new GradientStop(Color.FromRgb(0xC3, 0xCA, 0xD6), 0));
            steel.GradientStops.Add(new GradientStop(Color.FromRgb(0x7C, 0x86, 0x96), .45));
            steel.GradientStops.Add(new GradientStop(Color.FromRgb(0x3B, 0x43, 0x50), 1));
            var top = Oval(12, 14.6, 8.6, 8.6, Frozen(steel), null);
            top.Stroke = Ink; top.StrokeThickness = .6;
            canvas.Children.Add(top);
            var rim = Shape("M5,11.8 a8,8 0 0 1 10.4,-4.9", null, Frozen(new SolidColorBrush(A(Colors.White, .55))), .7);
            rim.StrokeStartLineCap = rim.StrokeEndLineCap = PenLineCap.Round;
            canvas.Children.Add(rim);
            canvas.Children.Add(Oval(12, 14.6, 6.1, 6.1, Ink, null));

            // The lamp: a short glass wall, the dome over it, the dome's shaded foot, a faint glint.
            var wall = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            wall.GradientStops.Add(new GradientStop(deep, 0));
            wall.GradientStops.Add(new GradientStop(lo, .35));
            wall.GradientStops.Add(new GradientStop(c, .6));
            wall.GradientStops.Add(new GradientStop(deep, 1));
            canvas.Children.Add(Shape("M6.5,12.6 v1.6 a5.5,5.5 0 0 0 11,0 v-1.6 z", Frozen(wall), null, 0));
            var cap = new RadialGradientBrush { GradientOrigin = new Point(.38, .30), Center = new Point(.38, .30), RadiusX = .75, RadiusY = .75 };
            cap.GradientStops.Add(new GradientStop(A(hi, .85), 0));
            cap.GradientStops.Add(new GradientStop(c, .3));
            cap.GradientStops.Add(new GradientStop(c, .65));
            cap.GradientStops.Add(new GradientStop(lo, 1));
            canvas.Children.Add(Oval(12, 12.4, 5.5, 5.65, Frozen(cap), null));
            canvas.Children.Add(Shape("M7.3,14.3 a5.2,5.2 0 0 0 9.4,0", null, Frozen(new SolidColorBrush(A(Colors.Black, .28))), .7));
            var glint = Oval(10.2, 10.1, 1.5, .8, Frozen(new SolidColorBrush(A(Colors.White, .34))), null);
            glint.RenderTransform = new RotateTransform(-24, 1.5, .8);
            canvas.Children.Add(glint);
            canvas.Children.Add(Oval(14.1, 14.4, .6, .6, Frozen(new SolidColorBrush(A(Colors.White, .15))), null));

            return new Viewbox { Width = width, Height = width * 28 / 24, Child = canvas, IsHitTestVisible = false };
        }

        private static Ellipse Oval(double cx, double cy, double rx, double ry, Brush fill, Effect effect)
        {
            var e = new Ellipse { Width = rx * 2, Height = ry * 2, Fill = fill, Effect = effect };
            Canvas.SetLeft(e, cx - rx); Canvas.SetTop(e, cy - ry);
            return e;
        }

        private static Path Shape(string data, Brush fill, Brush stroke, double thickness) =>
            new Path { Data = Frozen(Geometry.Parse(data)), Fill = fill, Stroke = stroke, StrokeThickness = thickness };
    }
}
