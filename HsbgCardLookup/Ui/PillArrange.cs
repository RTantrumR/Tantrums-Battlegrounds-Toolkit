using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using HsbgCardLookup.Config;
using HsbgCardLookup.Host;

namespace HsbgCardLookup.Ui
{
    /// <summary>
    /// Moving and sizing one of the corner pills (<see cref="SearchButton"/>, <see cref="NotificationBell"/>)
    /// while it is being arranged: drag the pill to move it, drag the arrow at its top-right corner to
    /// size it. The same LL-hook gesture as <see cref="TurnCounterPanel"/>, installed only for the gesture.
    ///
    /// The placement is read from the config on every layout, never cached: XF/YF = the pill's middle as
    /// canvas fractions, WF = a size factor on top of the game's height-only scale (so a resized pill
    /// still tracks the window like the game's own pills). Unset = the pill's default corner spot.
    /// A gesture writes straight into the placement and asks the pill to lay itself out again, so there
    /// is one layout path; a press that never moved puts the old values back.
    /// </summary>
    internal sealed class PillArrange
    {
        internal const double MinScale = 0.5, MaxScale = 2.5;

        private readonly PluginConfig _config;
        private readonly Func<HudPlacement> _placement;   // creates it when missing
        private readonly Action _relayout;
        private Border _handle;
        private FrameworkElement _root;
        private bool _arranging, _dragging, _resizing, _moved;
        private Point _startCursor;
        private double _startCx, _startCy, _startScale;
        private bool _oldSet; private double _oldX, _oldY, _oldW;

        public PillArrange(PluginConfig config, Func<HudPlacement> placement, Action relayout)
        {
            _config = config; _placement = placement; _relayout = relayout;
        }

        public bool Arranging => _arranging;

        /// <summary>The pill's size factor from a placement (1 when it was never arranged).</summary>
        internal static double UserScale(HudPlacement p) =>
            p != null && p.Set && p.WF > 0 ? Clamp(p.WF, MinScale, MaxScale) : 1;

        internal static bool Placed(HudPlacement p) => p != null && p.Set;

        /// <summary>Where an arranged pill of this size goes, kept whole inside the canvas.</summary>
        internal static Rect RectAt(HudPlacement p, double cw, double ch, double w, double h) =>
            new Rect(Clamp(p.XF * cw - w / 2, 0, Math.Max(0, cw - w)), Clamp(p.YF * ch - h / 2, 0, Math.Max(0, ch - h)), w, h);

        /// <summary>Canvas thread: hook the pill up once it is on the canvas.</summary>
        public void Attach(Canvas canvas, FrameworkElement root)
        {
            _root = root;
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
                Cursor = Cursors.SizeNESW, Child = arrow, Visibility = Visibility.Collapsed
            };
            _handle.MouseLeftButtonDown += (s, e) => { e.Handled = true; Begin(true, e); };
            root.MouseLeftButtonDown += (s, e) => { if (!_arranging) return; e.Handled = true; Begin(false, e); };
            canvas.Children.Add(_handle);
            try { HostServices.Overlay?.SetHitTestVisible(_handle); } catch { }
        }

        public void Detach(Canvas canvas)
        {
            End(false);
            if (_handle != null) canvas.Children.Remove(_handle);
        }

        /// <summary>Canvas thread: the arrange session for this pill started or ended.</summary>
        public void SetArranging(bool on)
        {
            if (on == _arranging) return;
            _arranging = on;
            if (!on) End(false);
            if (_root != null) _root.Cursor = on ? Cursors.SizeAll : Cursors.Hand;
            if (_handle != null) _handle.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>After the pill is laid out: the arrow goes on its top-right corner, over it.</summary>
        public void PlaceHandle(double left, double top, double w)
        {
            if (_handle == null) return;
            Canvas.SetLeft(_handle, left + w - _handle.Width * 0.6);
            Canvas.SetTop(_handle, top - _handle.Height * 0.4);
            Panel.SetZIndex(_handle, Panel.GetZIndex(_root) + 1);
        }

        public void Hide()
        {
            End(false);
            if (_handle != null) _handle.Visibility = Visibility.Collapsed;
        }

        // ── The gesture ──────────────────────────────────────────────────────────────────────────

        private void Begin(bool resize, MouseButtonEventArgs e)
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (!_arranging || _dragging || _resizing || canvas == null || _root == null) return;
            double cw = canvas.ActualWidth, ch = canvas.ActualHeight;
            double left = Canvas.GetLeft(_root), top = Canvas.GetTop(_root);
            if (cw <= 0 || ch <= 0 || double.IsNaN(left) || double.IsNaN(top)) return;
            try { _startCursor = e.GetPosition(canvas); } catch { return; }

            var p = _placement();
            _oldSet = p.Set; _oldX = p.XF; _oldY = p.YF; _oldW = p.WF;
            _startScale = UserScale(p);
            _startCx = left + _root.ActualWidth / 2;
            _startCy = top + _root.ActualHeight / 2;
            // From here the pill is drawn from the placement, even where it was still at its default spot.
            p.Set = true; p.XF = _startCx / cw; p.YF = _startCy / ch; p.WF = _startScale;

            _moved = false; _dragging = !resize; _resizing = resize;
            InstallHook();
        }

        private void OnMove()
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (canvas == null) { End(false); return; }
            double cw = canvas.ActualWidth, ch = canvas.ActualHeight;
            if (cw <= 0 || ch <= 0) return;
            Point cur;
            try { GetCursorPos(out POINT pt); cur = canvas.PointFromScreen(new Point(pt.X, pt.Y)); }
            catch { return; }
            double dx = cur.X - _startCursor.X, dy = cur.Y - _startCursor.Y;
            if (Math.Abs(dx) + Math.Abs(dy) > 1) _moved = true;
            var p = _placement();
            if (_dragging)
            {
                p.XF = Clamp((_startCx + dx) / cw, 0, 1);
                p.YF = Clamp((_startCy + dy) / ch, 0, 1);
            }
            else if (_resizing)
            {
                // Up or right grows it, about its middle.
                p.WF = Math.Round(Clamp(_startScale * (1 + (dx - dy) / 120.0), MinScale, MaxScale), 3);
            }
            _relayout();
        }

        private void End(bool persist)
        {
            if (!_dragging && !_resizing) return;
            _dragging = _resizing = false;
            RemoveHook();
            var p = _placement();
            var canvas = HostServices.Overlay?.Canvas;
            if (!persist || !_moved || canvas == null || canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0 || _root == null)
            {
                p.Set = _oldSet; p.XF = _oldX; p.YF = _oldY; p.WF = _oldW;
                _relayout();
                return;
            }
            _relayout();
            // Keep the spot the pill was actually drawn at (kept inside the canvas), not where the cursor went.
            double left = Canvas.GetLeft(_root), top = Canvas.GetTop(_root);
            if (!double.IsNaN(left) && !double.IsNaN(top))
            {
                p.XF = (left + _root.Width / 2) / canvas.ActualWidth;
                p.YF = (top + _root.Height / 2) / canvas.ActualHeight;
            }
            try { _config.Save(); } catch { }
        }

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

        // ── Gesture hook plumbing (canvas thread; never swallows) ────────────────────────────────

        private IntPtr _hook = IntPtr.Zero;
        private LowLevelMouseProc _proc;

        private void InstallHook()
        {
            if (_hook != IntPtr.Zero) return;
            try { _proc = HookProc; _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0); }
            catch { _hook = IntPtr.Zero; _proc = null; }
            if (_hook == IntPtr.Zero) End(false);
        }

        private void RemoveHook()
        {
            if (_hook == IntPtr.Zero) return;
            try { UnhookWindowsHookEx(_hook); } catch { }
            _hook = IntPtr.Zero; _proc = null;
        }

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            var hook = _hook;
            if (nCode >= 0)
            {
                if (wParam == (IntPtr)WM_MOUSEMOVE) { try { OnMove(); } catch { } }
                else if (wParam == (IntPtr)WM_LBUTTONUP) { try { End(true); } catch { } }
            }
            return CallNextHookEx(hook, nCode, wParam, lParam);
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private const int WH_MOUSE_LL = 14, WM_MOUSEMOVE = 0x0200, WM_LBUTTONUP = 0x0202;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);
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
