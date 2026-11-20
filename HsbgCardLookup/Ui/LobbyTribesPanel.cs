using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using HsbgCardLookup.Config;
using HsbgCardLookup.Data;
using HsbgCardLookup.Host;

namespace HsbgCardLookup.Ui
{
    /// <summary>
    /// This lobby's minion types for the whole match: one small box of round type icons with the
    /// type's name under each. Lives on the overlay canvas; moved by dragging it and scaled by its
    /// top-right grabber, the way <see cref="MmrSidePanel"/> is (same LL-hook gesture, same
    /// canvas-fraction placement, config <c>LobbyTribesHud</c>).
    /// </summary>
    public sealed class LobbyTribesPanel : IDockPanel
    {
        public string DockId => "tribes";
        public Border DockRoot => _root;
        public double DockScale
        {
            get { return _scale; }
            set { _scale = Clamp(value, MinScale, MaxScale); if (_root.Visibility == Visibility.Visible) Layout(); Save(false); }
        }
        public void DockMoveTo(double left, double top, bool save)
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (canvas == null || canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0) return;
            Canvas.SetLeft(_root, left); Canvas.SetTop(_root, top);
            _xf = left / canvas.ActualWidth; _yf = top / canvas.ActualHeight;
            if (save) Save(true);
        }

        private const double RefCellW = 46;       // one type's column at scale 1: the box is always this
                                                  // wide per type, a longer name is set smaller instead
        private const double LabelSize = 10.5, MinLabelSize = 6;
        private const double MinScale = 0.4, MaxScale = 4.8;   // of the settled (pixel) scale
        private const double DefaultXF = 0.005, DefaultYF = 0.115;

        /// <summary>English tribe name → the label to draw (the card language's vocabulary).</summary>
        public static Func<string, string> TribeLabel;

        private readonly PluginConfig _config;
        private readonly Dispatcher _ui;
        private readonly Border _root;
        private readonly StackPanel _row;
        private readonly Border _handle;
        private readonly DispatcherTimer _handleHide;
        private readonly List<Ellipse> _icons = new List<Ellipse>();
        private readonly List<TextBlock> _labels = new List<TextBlock>();

        private bool _attached;
        // _scale is the box's own scale in pixels terms once settled: at the first showing it takes the
        // game window's height into account (x height/1080), and is kept from then on (config Px).
        private double _xf = DefaultXF, _yf = DefaultYF, _scale = 1;
        private bool _settled;
        private bool _dragging, _resizing, _moved;
        private Point _startCursor;
        private double _startLeft, _startTop, _startScale;

        // Match state: the types are read once a second from the game's start until the list is there.
        private volatile bool _active;
        private List<string> _tribes = new List<string>();
        private DateTime _nextRead = DateTime.MinValue;
        private string _shownKey;
        private static LobbyTribesPanel _current;
        private static bool _hooked;

        private static readonly Brush PanelBg = Frozen(Color.FromArgb(0xDC, 0x0A, 0x0D, 0x14));

        public LobbyTribesPanel(PluginConfig config, Dispatcher ui)
        {
            _config = config; _ui = ui;
            _row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

            var arrow = new Path
            {
                Data = Geometry.Parse("M3 9 L9 3 M9 3 L9 6 M9 3 L6 3"),
                Stroke = Brushes.White, StrokeThickness = 1.6, Stretch = Stretch.Uniform, Margin = new Thickness(4)
            };
            _handle = new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x00, 0x00, 0x00)),
                BorderBrush = UiKit.AccentBrush, BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 2, 0), Visibility = Visibility.Collapsed,
                Cursor = Cursors.SizeNESW, Child = arrow
            };
            var grid = new Grid();
            grid.Children.Add(_row);
            grid.Children.Add(_handle);
            _root = new Border
            {
                Background = PanelBg, BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7), Visibility = Visibility.Collapsed,
                Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.75 },
                Child = grid
            };
            _root.SizeChanged += (s, e) => { if (!_dragging && !_resizing) ClampPosition(); };

            _handleHide = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            _handleHide.Tick += (s, e) => { _handleHide.Stop(); if (!_resizing) _handle.Visibility = Visibility.Collapsed; };
            _handle.MouseLeftButtonDown += (s, e) => { e.Handled = true; BeginGesture(true, e); };
            _root.MouseLeftButtonDown += (s, e) => { e.Handled = true; BeginGesture(false, e); };
            _root.MouseMove += (s, e) => { _handle.Visibility = Visibility.Visible; _handleHide.Stop(); _handleHide.Start(); };
            try { HostServices.Overlay?.SetHitTestVisible(_root); } catch { }

            var p = _config.LobbyTribesHud;
            if (p != null && p.WF > 0) { _xf = p.XF; _yf = p.YF; _scale = p.WF; }
            if (p != null && p.Px > 0) { _scale = Clamp(p.Px, MinScale, MaxScale); _settled = true; }

            PanelDock.Register(this);
            _current = this;
            _active = true;          // started mid-match: the first read decides
            if (!_hooked)
            {
                _hooked = true;
                try { HostServices.Game.GameStarted += () => _current?.OnGame(true); } catch { }
                try { HostServices.Game.GameEnded += () => _current?.OnGame(false); } catch { }
            }
        }

        private void OnGame(bool started)
        {
            _active = started;
            _tribes = new List<string>();
            _nextRead = DateTime.MinValue;
        }

        /// <summary>About every 100 ms, on the UI thread.</summary>
        public void Poll()
        {
            try
            {
                if (!_config.ShowLobbyTribes || ArrangeSession.IsActive || !_active) { Hide(); return; }
                if (_tribes.Count < 5 && DateTime.UtcNow >= _nextRead)
                {
                    _nextRead = DateTime.UtcNow.AddSeconds(1);
                    var races = HostServices.Memory?.LobbyTribes();
                    var names = new List<string>();
                    if (races != null)
                        foreach (int r in races) { string n = TribeTally.RaceName(r); if (n != null) names.Add(n); }
                    if (names.Count > 0) _tribes = names;
                }
                if (_tribes.Count == 0) { Hide(); return; }
                Show();
            }
            catch { }
        }

        public void OnSettingsChanged() { _shownKey = null; }
        public void OnLanguageChanged() { _shownKey = null; }
        public void CloseAll() { _ui.Invoke(new Action(() => { Hide(); RemoveHook(); })); }

        private void Hide()
        {
            if (_root.Visibility != Visibility.Collapsed) _root.Visibility = Visibility.Collapsed;
        }

        private void Show()
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (canvas == null) return;
            if (!_attached)
            {
                canvas.Children.Add(_root);
                canvas.SizeChanged += (s, e) => { if (_root.Visibility == Visibility.Visible) Layout(); };
                _attached = true;
            }
            string key = string.Join(",", _tribes);
            if (key != _shownKey)
            {
                _shownKey = key;
                _row.Children.Clear(); _icons.Clear(); _labels.Clear();
                foreach (string tribe in _tribes)
                {
                    var icon = new Ellipse { Fill = TribeIcons.BrushFor(tribe), Stroke = TribeIcons.Ring, HorizontalAlignment = HorizontalAlignment.Center };
                    string text = tribe;
                    try { text = TribeLabel?.Invoke(tribe) ?? tribe; } catch { }
                    var label = new TextBlock
                    {
                        Text = text, Foreground = UiKit.TextPrimary, FontWeight = FontWeights.SemiBold,
                        TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    };
                    var cell = new StackPanel();
                    cell.Children.Add(icon);
                    cell.Children.Add(new Grid { Children = { label } });   // a row of one height, the name centred in it
                    _row.Children.Add(cell);
                    _icons.Add(icon); _labels.Add(label);
                }
                Layout();
            }
            if (_root.Visibility != Visibility.Visible) { _root.Visibility = Visibility.Visible; Layout(); }
        }

        private void Layout()
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (canvas == null || canvas.ActualHeight <= 0) return;
            if (!_settled)
            {
                _scale = Clamp(_scale * Math.Max(0.6, Math.Min(canvas.ActualHeight / 1080.0, 2.0)), MinScale, MaxScale);
                _settled = true;
                Save(false);
            }
            double s = _scale;
            _root.Padding = new Thickness(5 * s, 5 * s, 5 * s, 4 * s);
            for (int i = 0; i < _icons.Count; i++)
            {
                _icons[i].Width = _icons[i].Height = 34 * s;
                _icons[i].StrokeThickness = 1.4 * s;
                // A name too long for its column is set smaller until it fits, never cut.
                var label = _labels[i];
                double room = (RefCellW - 5) * s;
                label.Width = double.NaN;
                label.FontSize = LabelSize * s;
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double wide = label.DesiredSize.Width;
                if (wide > room) label.FontSize = Math.Max(MinLabelSize * s, LabelSize * s * room / wide);
                label.Width = RefCellW * s;
                var row = (Grid)label.Parent;
                row.Height = 15 * s;
                row.Margin = new Thickness(0, 1 * s, 0, 0);
            }
            ClampPosition();
        }

        private void ClampPosition()
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (canvas == null) return;
            double cw = canvas.ActualWidth, ch = canvas.ActualHeight;
            if (cw <= 0 || ch <= 0 || PanelDock.Holds(this)) return;
            Canvas.SetLeft(_root, Clamp(_xf * cw, 0, Math.Max(0, cw - _root.ActualWidth)));
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
                // Drag right to scale up, by the width the box had when the drag began.
                double w0 = Math.Max(40, _root.ActualWidth / (_scale / _startScale));
                double before = _scale;
                _scale = Clamp(_startScale * (w0 + dx) / w0, MinScale, MaxScale);
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
            if (dragged) PanelDock.Dropped(this);
            _xf = Canvas.GetLeft(_root) / canvas.ActualWidth;
            _yf = Canvas.GetTop(_root) / canvas.ActualHeight;
            Save(true);
        }

        private void Save(bool placed)
        {
            try
            {
                var p = _config.LobbyTribesHud ?? (_config.LobbyTribesHud = new HudPlacement());
                if (placed) { p.Set = true; p.XF = _xf; p.YF = _yf; p.WF = _scale; }
                p.Px = _scale;
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
