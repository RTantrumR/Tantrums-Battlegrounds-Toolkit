using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using HsbgCardLookup.Host;

namespace HsbgCardLookup.Ui
{
    // An assembled group has ONE resize arrow, at the top right of the whole group, and is resized as
    // one picture: while the arrow is dragged every box is drawn scaled about the group's top-left
    // corner (a render transform, so nothing is laid out again and no box can trail another); the real
    // sizes are set once, when the arrow is let go.
    public static partial class PanelDock
    {
        private static Border _handle;
        private static DispatcherTimer _handleHide;
        private static List<IDockPanel> _sizing;        // the group being resized, null otherwise
        private static Rect _sizeBox;                   // its bounds when the drag began
        private static Point _sizeCursor;
        private static double _sizeRatio = 1;

        /// <summary>The box is part of an assembled group: it leaves the resize arrow to the group.</summary>
        public static bool Grouped(IDockPanel p) => Enabled && GroupOf(p).Count(Shown) > 1;

        private static Rect Bounds(IEnumerable<IDockPanel> group)
        {
            Rect all = Rect.Empty;
            foreach (var p in group) if (Shown(p)) all.Union(RectOf(p));
            return all;
        }

        // The mouse is over a box of a group: the group's arrow shows for a moment.
        private static void Hover(IDockPanel p)
        {
            try
            {
                if (_sizing != null || !Grouped(p)) return;
                var canvas = HostServices.Overlay?.Canvas;
                if (canvas == null) return;
                if (_handle == null)
                {
                    var arrow = new Path
                    {
                        Data = Geometry.Parse("M3 9 L9 3 M9 3 L9 6 M9 3 L6 3"),
                        Stroke = Brushes.White, StrokeThickness = 1.6, Stretch = Stretch.Uniform, Margin = new Thickness(4)
                    };
                    _handle = new Border
                    {
                        Width = 22, Height = 22, CornerRadius = new CornerRadius(5), Child = arrow, Cursor = Cursors.SizeNESW,
                        Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x00, 0x00, 0x00)),
                        BorderBrush = UiKit.AccentBrush, BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed
                    };
                    Panel.SetZIndex(_handle, 9100);
                    canvas.Children.Add(_handle);
                    try { HostServices.Overlay?.SetHitTestVisible(_handle); } catch { }
                    _handle.MouseLeftButtonDown += (s, e) => { e.Handled = true; BeginSize(e); };
                    _handle.MouseMove += (s, e) => { _handleHide.Stop(); _handleHide.Start(); };
                    _handleHide = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                    _handleHide.Tick += (s, e) => { _handleHide.Stop(); if (_sizing == null) _handle.Visibility = Visibility.Collapsed; };
                }
                _handle.Tag = GroupOf(p);
                PlaceHandle(Bounds((List<IDockPanel>)_handle.Tag));
                _handle.Visibility = Visibility.Visible;
                _handleHide.Stop(); _handleHide.Start();
            }
            catch { }
        }

        private static void PlaceHandle(Rect box)
        {
            if (box.IsEmpty) return;
            Canvas.SetLeft(_handle, box.Right - _handle.Width - 2);
            Canvas.SetTop(_handle, box.Top + 2);
        }

        private static void BeginSize(MouseButtonEventArgs e)
        {
            var canvas = HostServices.Overlay?.Canvas;
            var group = _handle.Tag as List<IDockPanel>;
            if (canvas == null || group == null || _sizing != null) return;
            var box = Bounds(group);
            if (box.IsEmpty || box.Width < 10) return;
            try { _sizeCursor = e.GetPosition(canvas); } catch { return; }
            _sizing = group.Where(Shown).ToList(); _sizeBox = box; _sizeRatio = 1;
            InstallHook();
        }

        private static void SizeMove()
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (canvas == null || _sizing == null) { EndSize(false); return; }
            Point cur;
            try { POINT p; GetCursorPos(out p); cur = canvas.PointFromScreen(new Point(p.X, p.Y)); } catch { return; }
            // Drag right to grow, by the width the group had when the drag began.
            _sizeRatio = Math.Max(0.5, Math.Min((_sizeBox.Width + cur.X - _sizeCursor.X) / _sizeBox.Width, 2.5));
            foreach (var p in _sizing)
            {
                var r = p.DockRoot;
                r.RenderTransformOrigin = new Point(0, 0);
                r.RenderTransform = new ScaleTransform(_sizeRatio, _sizeRatio, _sizeBox.Left - Canvas.GetLeft(r), _sizeBox.Top - Canvas.GetTop(r));
            }
            PlaceHandle(new Rect(_sizeBox.Left, _sizeBox.Top, _sizeBox.Width * _sizeRatio, _sizeBox.Height * _sizeRatio));
        }

        // The drag ended: the drawn scale becomes each box's real size, in one go, and the group's
        // top-left corner stays where it was.
        private static void EndSize(bool keep)
        {
            var group = _sizing;
            if (group == null) return;
            _sizing = null;
            RemoveHook();
            try
            {
                foreach (var p in group) { p.DockRoot.RenderTransform = null; }
                if (!keep || Math.Abs(_sizeRatio - 1) < 0.002) return;
                foreach (var p in group) p.DockScale = p.DockScale * _sizeRatio;
                Invalidate();                           // measured, fitted and stretched now, not on the next polls
                Layout();
                var root = RootOf(group[0]);
                var now = Bounds(group);
                if (!now.IsEmpty)
                    root.DockMoveTo(Canvas.GetLeft(root.DockRoot) + _sizeBox.Left - now.Left, Canvas.GetTop(root.DockRoot) + _sizeBox.Top - now.Top, true);
                Layout();
                PlaceHandle(Bounds(group));
            }
            catch { }
        }

        // ── Gesture hook plumbing (as the boxes' own; installed only for the gesture, never swallows) ──

        private static IntPtr _hook = IntPtr.Zero;
        private static LowLevelMouseProc _proc;

        private static void InstallHook()
        {
            if (_hook != IntPtr.Zero) return;
            try { _proc = HookProc; _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0); }
            catch { _hook = IntPtr.Zero; _proc = null; _sizing = null; }
        }

        private static void RemoveHook()
        {
            if (_hook == IntPtr.Zero) return;
            try { UnhookWindowsHookEx(_hook); } catch { }
            _hook = IntPtr.Zero; _proc = null;
        }

        private static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                if (wParam == (IntPtr)WM_MOUSEMOVE) { try { SizeMove(); } catch { } }
                else if (wParam == (IntPtr)WM_LBUTTONUP) { try { EndSize(true); } catch { } }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
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
