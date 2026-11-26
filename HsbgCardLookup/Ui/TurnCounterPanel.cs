using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using HsbgCardLookup.Config;
using HsbgCardLookup.Host;
using HsbgCardLookup.Localization;

namespace HsbgCardLookup.Ui
{
    /// <summary>
    /// The match's turn number in a small box, top right by default. Lives on the overlay canvas;
    /// moved by dragging it and scaled by its top-right grabber, the way <see cref="LobbyTribesPanel"/>
    /// is (same LL-hook gesture, config <c>TurnCounterHud</c>). XF is the box's RIGHT edge, so a
    /// longer number grows it to the left and it stays against the screen's edge. Its size is
    /// settled once, at the first showing, from the game window's height then (DefaultScale at
    /// 1080), and saved: a later change of the window's size leaves it alone.
    /// </summary>
    public sealed class TurnCounterPanel : IDockPanel
    {
        public string DockId => "turn";
        public bool DockStretches(bool stacked) => false;
        public double DockLength(bool stacked) => stacked ? _root.ActualWidth : _root.ActualHeight;
        public Border DockRoot => _root;
        public double DockScale
        {
            get { return _scale; }
            set { _scale = Clamp(value, MinScale, MaxScale); if (_root.Visibility == Visibility.Visible) Layout(); Save(); }
        }
        public void DockMoveTo(double left, double top, bool save)
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (canvas == null || canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0) return;
            Canvas.SetLeft(_root, left); Canvas.SetTop(_root, top);
            _xf = (left + _root.ActualWidth) / canvas.ActualWidth; _yf = top / canvas.ActualHeight;
            if (save) Save();
        }

        private const double MinScale = 0.4, MaxScale = 4;
        private const double DefaultScale = 1.277;     // the owner's size on a 1920x1080 client
        private const double DefaultXF = 0.995, DefaultYF = 0.008;

        private readonly PluginConfig _config;
        private readonly Dispatcher _ui;
        private readonly Border _root;
        private readonly TextBlock _text;
        private readonly Border _handle;
        private readonly DispatcherTimer _handleHide;

        private bool _attached;
        private double _xf = DefaultXF, _yf = DefaultYF, _scale;   // _scale 0 = not settled yet
        private bool _dragging, _resizing, _moved;
        private Point _startCursor;
        private double _startLeft, _startTop, _startScale;
        private int _shownTurn = -1;
        private bool _stale = true;

        private static readonly Brush PanelBg = Frozen(Color.FromArgb(0xDC, 0x0A, 0x0D, 0x14));

        public TurnCounterPanel(PluginConfig config, Dispatcher ui)
        {
            _config = config; _ui = ui;
            _text = new TextBlock { Foreground = UiKit.TextPrimary, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

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
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -3, -3, 0), Visibility = Visibility.Collapsed,
                Cursor = Cursors.SizeNESW, Child = arrow
            };
            var grid = new Grid();
            grid.Children.Add(_text);
            grid.Children.Add(_handle);
            _root = new Border
            {
                Background = PanelBg, BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7), Visibility = Visibility.Collapsed,
                Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.75 },
                Child = grid
            };
            _root.SizeChanged += (s, e) => { if (!_dragging && !_resizing) ClampPosition(); };
            PanelDock.Register(this);

            _handleHide = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            _handleHide.Tick += (s, e) => { _handleHide.Stop(); if (!_resizing) _handle.Visibility = Visibility.Collapsed; };
            _handle.MouseLeftButtonDown += (s, e) => { e.Handled = true; BeginGesture(true, e); };
            _root.MouseLeftButtonDown += (s, e) => { e.Handled = true; BeginGesture(false, e); };
            _root.MouseMove += (s, e) =>
            {
                if (PanelDock.Grouped(this)) { _handle.Visibility = Visibility.Collapsed; return; }     // an assembled group has one arrow of its own
                _handle.Visibility = Visibility.Visible; _handleHide.Stop(); _handleHide.Start();
            };
            try { HostServices.Overlay?.SetHitTestVisible(_root); } catch { }

            LoadPlacement();
        }

        private void LoadPlacement()
        {
            _xf = DefaultXF; _yf = DefaultYF; _scale = 0;
            var p = _config.TurnCounterHud;
            if (p != null && p.WF > 0) { _xf = p.XF; _yf = p.YF; _scale = Clamp(p.WF, MinScale, MaxScale); }
        }

        /// <summary>The saved place and size were replaced wholesale (a config import, a preset).</summary>
        public void ReloadPlacement() { LoadPlacement(); _stale = true; if (_root.Visibility == Visibility.Visible) Layout(); }

        /// <summary>About every 100 ms, on the UI thread.</summary>
        public void Poll()
        {
            try
            {
                if (!_config.ShowTurnCounter || ArrangeSession.IsActive) { Hide(); return; }
                int turn = 0;
                var g = HostServices.Game;
                if (g != null && g.IsBattlegroundsMatch) turn = g.TurnNumber;
                else if (OpponentBoardPanel.TestBoard) turn = 7;     // the sample board's test aid shows a sample turn too, out of a match
                if (turn < 1) { Hide(); return; }
                Show(turn);
            }
            catch { }
        }

        public void OnSettingsChanged() { _stale = true; }
        public void OnLanguageChanged() { _stale = true; }
        public void CloseAll() { _ui.Invoke(new Action(() => { Hide(); RemoveHook(); })); }

        private void Hide()
        {
            if (_root.Visibility != Visibility.Collapsed) _root.Visibility = Visibility.Collapsed;
        }

        private void Show(int turn)
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (canvas == null) return;
            if (!_attached)
            {
                canvas.Children.Add(_root);
                canvas.SizeChanged += (s, e) => { if (_root.Visibility == Visibility.Visible) Layout(); };
                _attached = true;
            }
            if (turn != _shownTurn || _stale)
            {
                _shownTurn = turn; _stale = false;
                _text.Text = Strings.F("turnCounter.text", turn);
            }
            if (_root.Visibility != Visibility.Visible) { _root.Visibility = Visibility.Visible; Layout(); }
        }

        private void Layout()
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (canvas == null || canvas.ActualHeight <= 0) return;
            if (_scale <= 0)
            {
                _scale = Clamp(DefaultScale * canvas.ActualHeight / 1080.0, MinScale, MaxScale);
                Save();
            }
            double s = _scale;
            _root.Padding = new Thickness(10 * s, 4 * s, 10 * s, 5 * s);
            // The arrow sits in the box's own top-right corner whatever the padding has grown to.
            _handle.Margin = new Thickness(0, 2 - 4 * s, 2 - 10 * s, 0);
            _text.FontSize = 17 * s;
            ClampPosition();
        }

        private void ClampPosition()
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (canvas == null) return;
            double cw = canvas.ActualWidth, ch = canvas.ActualHeight;
            if (cw <= 0 || ch <= 0 || PanelDock.Holds(this)) return;
            Canvas.SetLeft(_root, Clamp(_xf * cw - _root.ActualWidth, 0, Math.Max(0, cw - _root.ActualWidth)));
            Canvas.SetTop(_root, Clamp(_yf * ch, 0, Math.Max(0, ch - _root.ActualHeight)));
        }

        // ── Move / resize gestures (LL mouse hook, installed only for the gesture) ───────────────

        private void BeginGesture(bool resize, MouseButtonEventArgs e)
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (!_attached || _dragging || _resizing || canvas == null) return;
            try { _startCursor = e.GetPosition(canvas); } catch { return; }
            _startLeft = Canvas.GetLeft(_root); _startTop = Canvas.GetTop(_root); _startScale = _scale;
            if (double.IsNaN(_startLeft) || double.IsNaN(_startTop)) return;
            _moved = false; _dragging = !resize; _resizing = resize;
            InstallHook();
        }

        private void OnGestureMove()
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (canvas == null) { EndGesture(false); return; }
            double cw = canvas.ActualWidth, ch = canvas.ActualHeight;
            if (cw <= 0 || ch <= 0) return;
            Point cur;
            try { GetCursorPos(out POINT p); cur = canvas.PointFromScreen(new Point(p.X, p.Y)); }
            catch { return; }
            double dx = cur.X - _startCursor.X, dy = cur.Y - _startCursor.Y;
            if (Math.Abs(dx) + Math.Abs(dy) > 1) _moved = true;
            if (_dragging)
            {
                Canvas.SetLeft(_root, Clamp(_startLeft + dx, 0, Math.Max(0, cw - _root.ActualWidth)));
                Canvas.SetTop(_root, Clamp(_startTop + dy, 0, Math.Max(0, ch - _root.ActualHeight)));
                PanelDock.Dragging(this);
            }
            else if (_resizing)
            {
                // Drag up or right to scale up; the box keeps its right edge (see ClampPosition).
                double before = _scale;
                _scale = Clamp(_startScale * (1 + (dx - dy) / 120.0), MinScale, MaxScale);
                _moved = true;
                Layout();
                PanelDock.Scaled(this, _scale / before);
            }
        }

        private void EndGesture(bool persist = true)
        {
            if (!_dragging && !_resizing) return;
            bool dragged = _dragging;
            _dragging = _resizing = false;
            RemoveHook();
            var canvas = HostServices.Overlay?.Canvas;
            if (!persist || !_moved || canvas == null || canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0) return;
            if (dragged)
            {
                PanelDock.Dropped(this);
                _xf = (Canvas.GetLeft(_root) + _root.ActualWidth) / canvas.ActualWidth;
                _yf = Canvas.GetTop(_root) / canvas.ActualHeight;
            }
            Save();
        }

        private void Save()
        {
            try
            {
                var p = _config.TurnCounterHud ?? (_config.TurnCounterHud = new HudPlacement());
                p.Set = true; p.XF = _xf; p.YF = _yf; p.WF = _scale;
                _config.Save();
            }
            catch { }
        }

        private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

        // ── Gesture hook plumbing (canvas thread; never swallows) ────────────────────────────────

        private IntPtr _hook = IntPtr.Zero;
        private LowLevelMouseProc _proc;

        private void InstallHook()
        {
            if (_hook != IntPtr.Zero) return;
            try { _proc = HookProc; _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0); }
            catch { _hook = IntPtr.Zero; _proc = null; _dragging = _resizing = false; }
        }

        private void RemoveHook()
        {
            if (_hook == IntPtr.Zero) return;
            try { UnhookWindowsHookEx(_hook); } catch { }
            _hook = IntPtr.Zero; _proc = null;
        }

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                if (wParam == (IntPtr)WM_MOUSEMOVE) { try { OnGestureMove(); } catch { } }
                else if (wParam == (IntPtr)WM_LBUTTONUP) { try { EndGesture(); } catch { } }
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
