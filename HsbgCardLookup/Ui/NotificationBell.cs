using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using HsbgCardLookup.Config;
using HsbgCardLookup.Host;
using HsbgCardLookup.Localization;
using HsbgCardLookup.Net;
using HsbgCardLookup.Update;

namespace HsbgCardLookup.Ui
{
    /// <summary>
    /// The notifications bell on the overlay canvas, just left of the search button in the bottom-right
    /// corner during a BG match. Two counts: green = a newer toolkit version, yellow = unread patch notes
    /// (the newest page from /api/v1/patches/latest, plus the site's hand-written notices). Clicking it
    /// opens a small menu drawn on the canvas — Download / Skip for the update, the patch-notes page in
    /// the toolkit's language. What the menu showed counts as read when it closes; read items stay listed,
    /// so a link can be reopened after the match.
    ///
    /// The menu is a canvas element, not a WPF Popup: the overlay window never takes focus, so a Popup's
    /// click-outside dismissal can't work here. It closes on the bell, its ✕, an action, or when the
    /// bell itself hides.
    /// </summary>
    public sealed class NotificationBell
    {
        // Same reference frame and scaling law as SearchButton (1920×1080, height-only scale): a round
        // pill as tall as the game's, RefGap to the left of the search button.
        private const double RefSize = 43;
        private const double RefGap = 12;
        private const double RefMenuW = 330;
        private const int MaxNotices = 4;

        private static readonly Color FaceColor = Color.FromArgb(0xE6, 0x22, 0x1C, 0x18);
        private static readonly Color RimColor = Color.FromRgb(0x8F, 0x88, 0x7E);
        private static readonly Color GlyphColor = Color.FromRgb(0xC9, 0xC4, 0xBC);
        private static readonly Color UpdateColor = Color.FromRgb(0x4E, 0xCB, 0x8D);   // the site's accent green
        private static readonly Color PatchColor = Color.FromRgb(0xFF, 0xD7, 0x00);    // the site's wordmark yellow
        private static readonly Color BadgeInk = Color.FromRgb(0x1A, 0x0E, 0x04);

        private readonly PluginConfig _config;
        private readonly Action<string> _openDownload;   // the release's Setup in the browser
        private readonly Action<string> _skip;           // skip this update version
        private readonly Action<string> _log;

        private UpdateNotice _update;
        private PatchRow _patch;
        private List<PluginNotice> _notices = new List<PluginNotice>();

        private Border _root, _menu, _greenBadge, _yellowBadge;
        private TextBlock _greenCount, _yellowCount;
        private bool _attached, _menuOpen;
        private double _scale = 1;
        private DateTime _lastPoll = DateTime.MinValue;
        private string _lastSig;

        public NotificationBell(PluginConfig config, Action<string> openDownload, Action<string> skip, Action<string> log = null)
        {
            _config = config;
            _openDownload = openDownload;
            _skip = skip;
            _log = log;
        }

        // ── Any thread: state in, marshalled to the canvas thread ────────────────────────────────

        public void Poll()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastPoll).TotalMilliseconds < 500) return;
            _lastPoll = now;

            bool show = false;
            try { show = ArrangeSession.AllowsSearchButton && HostServices.Game != null && HostServices.Game.IsBattlegroundsMatch; }
            catch { }

            string sig = show ? "1" : "0";
            if (sig == _lastSig) return;
            _lastSig = sig;
            OnCanvas(() => Apply(show));
        }

        public void OnSettingsChanged() => _lastSig = null;

        internal void SetUpdateState(UpdateNotice notice) => OnCanvas(() => { _update = notice; Refresh(); });
        public void SetPatch(PatchRow patch) => OnCanvas(() => { _patch = patch; Refresh(); });
        public void SetNotices(List<PluginNotice> notices) => OnCanvas(() => { _notices = notices ?? new List<PluginNotice>(); Refresh(); });

        /// <summary>The plugin language changed: re-set the tooltip and redraw an open menu.</summary>
        public void Relabel() => OnCanvas(() =>
        {
            if (_root != null) Tip.Set(_root, Strings.T("overlay.bell.tooltip"));
            if (_menuOpen) { BuildMenuContent(); Layout(); }
        });

        public void CloseAll()
        {
            try
            {
                HostServices.Overlay?.Canvas?.Dispatcher?.Invoke(new Action(() =>
                {
                    var canvas = HostServices.Overlay?.Canvas;
                    if (canvas == null || !_attached) return;
                    canvas.SizeChanged -= OnCanvasSizeChanged;
                    canvas.Children.Remove(_root);
                    if (_menu != null) canvas.Children.Remove(_menu);
                    _attached = false;
                }));
            }
            catch { /* the host may be tearing down */ }
        }

        private static void OnCanvas(Action a)
        {
            try { HostServices.Overlay?.Canvas?.Dispatcher?.BeginInvoke(a); } catch { }
        }

        // ── Canvas thread from here down ─────────────────────────────────────────────────────────

        private void Apply(bool show)
        {
            if (!show)
            {
                if (_menuOpen) CloseMenu();
                if (_root != null) _root.Visibility = Visibility.Collapsed;
                return;
            }
            var canvas = HostServices.Overlay?.Canvas;
            if (canvas == null) return;
            if (!_attached)
            {
                Build();
                canvas.Children.Add(_root);
                canvas.Children.Add(_menu);
                canvas.SizeChanged += OnCanvasSizeChanged;
                _attached = true;
            }
            _root.Visibility = Visibility.Visible;
            Refresh();
            Layout();
        }

        private void OnCanvasSizeChanged(object s, SizeChangedEventArgs e) => Layout();

        private void Build()
        {
            var glyph = new Path
            {
                // Feather "bell": body + clapper.
                Data = Geometry.Parse("M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9 M13.73 21a2 2 0 0 1-3.46 0"),
                Stroke = new SolidColorBrush(GlyphColor),
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(11),
            };
            _greenBadge = Badge(UpdateColor, out _greenCount, HorizontalAlignment.Left);
            _yellowBadge = Badge(PatchColor, out _yellowCount, HorizontalAlignment.Right);

            var face = new Grid();
            face.Children.Add(glyph);
            face.Children.Add(_greenBadge);
            face.Children.Add(_yellowBadge);

            _root = new Border
            {
                Background = new SolidColorBrush(FaceColor),
                BorderBrush = new SolidColorBrush(RimColor),
                Child = face,
                Cursor = Cursors.Hand,
            };
            Tip.Set(_root, Strings.T("overlay.bell.tooltip"));
            _root.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                if (_menuOpen) CloseMenu(); else OpenMenu();
            };
            try { HostServices.Overlay?.SetHitTestVisible(_root); } catch { }

            _menu = new Border
            {
                Background = new LinearGradientBrush(UiKit.PanelBg2, UiKit.PanelBg, 90),
                BorderBrush = UiKit.AccentBrush,
                Visibility = Visibility.Collapsed,
            };
            try { HostServices.Overlay?.SetHitTestVisible(_menu); } catch { }
        }

        private static Border Badge(Color fill, out TextBlock count, HorizontalAlignment side)
        {
            count = new TextBlock
            {
                Foreground = new SolidColorBrush(BadgeInk), FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            return new Border
            {
                Background = new SolidColorBrush(fill),
                HorizontalAlignment = side, VerticalAlignment = VerticalAlignment.Top,
                Child = count, Visibility = Visibility.Collapsed, IsHitTestVisible = false,
            };
        }

        // ── What counts ──────────────────────────────────────────────────────────────────────────

        private bool HasUpdate => _update != null && _update.AvailableForDownload && !string.IsNullOrEmpty(_update.AvailableVersion);
        private bool UpdateUnread => HasUpdate && _update.AvailableVersion != _config.UpdateSeenVersion;
        private bool HasPatch => _patch != null && !string.IsNullOrEmpty(_patch.Version);
        // A first run does not announce a patch that is already old news.
        private bool PatchUnread => HasPatch && _patch.Version != _config.PatchSeenVersion && IsFresh(_patch.PublishedAt);

        private List<PluginNotice> ShownNotices() =>
            _notices.Where(n => n != null && n.Active && IsFresh(n.Date)).OrderByDescending(n => n.Id).Take(MaxNotices).ToList();

        private static bool IsFresh(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return true;
            if (DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d))
                return (DateTime.UtcNow - d.ToUniversalTime()).TotalDays <= 7;
            return true;
        }

        private void Refresh()
        {
            if (_root == null) return;
            int green = UpdateUnread ? 1 : 0;
            int yellow = (PatchUnread ? 1 : 0) + ShownNotices().Count(n => n.Id > _config.PatchNoticeLastSeenId);
            SetBadge(_greenBadge, _greenCount, green);
            SetBadge(_yellowBadge, _yellowCount, yellow);
            if (_menuOpen) { BuildMenuContent(); Layout(); }
        }

        private static void SetBadge(Border badge, TextBlock count, int n)
        {
            badge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            count.Text = n > 9 ? "9+" : n.ToString();
        }

        // ── The menu ─────────────────────────────────────────────────────────────────────────────

        private void OpenMenu()
        {
            _menuOpen = true;
            BuildMenuContent();
            _menu.Visibility = Visibility.Visible;
            Layout();
        }

        private void CloseMenu()
        {
            _menuOpen = false;
            if (_menu != null) _menu.Visibility = Visibility.Collapsed;
            MarkSeen();
            Refresh();
        }

        // Everything the menu showed is now read.
        private void MarkSeen()
        {
            bool dirty = false;
            if (HasUpdate && _config.UpdateSeenVersion != _update.AvailableVersion) { _config.UpdateSeenVersion = _update.AvailableVersion; dirty = true; }
            if (HasPatch && _config.PatchSeenVersion != _patch.Version) { _config.PatchSeenVersion = _patch.Version; dirty = true; }
            var shown = ShownNotices();
            if (shown.Count > 0 && shown.Max(n => n.Id) > _config.PatchNoticeLastSeenId) { _config.PatchNoticeLastSeenId = shown.Max(n => n.Id); dirty = true; }
            if (dirty) _config.Save();
        }

        private void BuildMenuContent()
        {
            double s = _scale;
            var list = new StackPanel();

            var close = new TextBlock { Text = "✕", Foreground = UiKit.TextMuted, FontSize = Font(13), Cursor = Cursors.Hand, Padding = new Thickness(6 * s, 0, 0, 0) };
            close.MouseLeftButtonUp += (o, e) => { e.Handled = true; CloseMenu(); };
            DockPanel.SetDock(close, Dock.Right);
            var header = new DockPanel { Margin = new Thickness(14 * s, 10 * s, 12 * s, 6 * s) };
            header.Children.Add(close);
            header.Children.Add(new TextBlock { Text = Strings.T("overlay.bell.title"), Foreground = UiKit.AccentBrush, FontSize = Font(11), FontWeight = FontWeights.SemiBold });
            list.Children.Add(header);

            bool any = false;
            if (HasUpdate) { list.Children.Add(UpdateRow()); any = true; }
            if (HasPatch) { list.Children.Add(PatchRowView()); any = true; }
            foreach (var n in ShownNotices()) { list.Children.Add(NoticeRow(n)); any = true; }
            if (!any)
                list.Children.Add(new TextBlock
                {
                    Text = Strings.T("overlay.bell.empty"), Foreground = UiKit.TextMuted, FontSize = Font(13),
                    Margin = new Thickness(14 * s, 2 * s, 14 * s, 12 * s)
                });

            _menu.Child = new Border { Padding = new Thickness(0, 0, 0, 4 * s), Child = list };
        }

        private FrameworkElement UpdateRow()
        {
            var col = new StackPanel();
            col.Children.Add(Title(Strings.F("bell.update.title", _update.AvailableVersion), UpdateUnread ? UpdateColor : (Color?)null));
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6 * _scale, 0, 0) };
            string version = _update.AvailableVersion, url = _update.DownloadUrl ?? _update.Url;
            actions.Children.Add(ActionLink(Strings.T("bell.update.download"), new SolidColorBrush(UpdateColor), () => { _openDownload?.Invoke(url); CloseMenu(); }));
            actions.Children.Add(ActionLink(Strings.T("bell.update.skip"), UiKit.TextMuted, () => { CloseMenu(); _skip?.Invoke(version); }));
            col.Children.Add(actions);
            return Row(col, null);
        }

        private FrameworkElement PatchRowView()
        {
            var col = new StackPanel();
            col.Children.Add(Title(Strings.F("bell.patch.title", _patch.Version), PatchUnread ? PatchColor : (Color?)null));
            string when = FormatDate(_patch.PublishedAt);
            if (when != null) col.Children.Add(Meta(when));
            string url = _patch.UrlFor(Strings.Web);
            return Row(col, () => OpenUrl(url + "?utm_source=toolkit&utm_medium=bell&utm_campaign=patch-notes"));
        }

        private FrameworkElement NoticeRow(PluginNotice n)
        {
            var col = new StackPanel();
            col.Children.Add(Title(n.TitleFor(Strings.Web) ?? Strings.T("overlay.bell.untitled"), n.Id > _config.PatchNoticeLastSeenId ? PatchColor : (Color?)null));
            string description = n.DescriptionFor(Strings.Web);
            if (!string.IsNullOrEmpty(description)) col.Children.Add(Meta(description));
            string when = FormatDate(n.Date);
            if (when != null) col.Children.Add(Meta(when));
            string url = n.UrlFor(Strings.Web);
            if (string.IsNullOrEmpty(url))
                url = AssetClient.SiteBase + Locales.SitePath(Strings.Web, "/patch-notes") + "?utm_source=toolkit&utm_medium=bell&utm_campaign=patch-notes";
            return Row(col, () => OpenUrl(url));
        }

        // A title line; an unread item gets a dot in its own colour, a read one is drawn plainer.
        private FrameworkElement Title(string text, Color? unreadDot)
        {
            var row = new DockPanel();
            if (unreadDot.HasValue)
            {
                var dot = new Ellipse { Width = 7 * _scale, Height = 7 * _scale, Fill = new SolidColorBrush(unreadDot.Value), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8 * _scale, 0) };
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);
            }
            row.Children.Add(new TextBlock
            {
                Text = text, FontSize = Font(14), TextWrapping = TextWrapping.Wrap,
                Foreground = unreadDot.HasValue ? UiKit.TextPrimary : UiKit.TextSecondary,
                FontWeight = unreadDot.HasValue ? FontWeights.SemiBold : FontWeights.Normal,
            });
            return row;
        }

        private TextBlock Meta(string text) => new TextBlock
        {
            Text = text, Foreground = UiKit.TextMuted, FontSize = Font(11.5), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3 * _scale, 0, 0)
        };

        private TextBlock ActionLink(string text, Brush color, Action onClick)
        {
            var t = new TextBlock { Text = text, Foreground = color, FontSize = Font(12.5), FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 18 * _scale, 0) };
            t.MouseLeftButtonUp += (o, e) => { e.Handled = true; onClick(); };
            return t;
        }

        private Border Row(UIElement content, Action onClick)
        {
            var row = new Border
            {
                Background = Brushes.Transparent, Padding = new Thickness(14 * _scale, 7 * _scale, 14 * _scale, 7 * _scale),
                BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(0, 1, 0, 0), Child = content,
            };
            if (onClick != null)
            {
                row.Cursor = Cursors.Hand;
                row.MouseLeftButtonUp += (o, e) => { e.Handled = true; onClick(); };
            }
            return row;
        }

        private void OpenUrl(string url)
        {
            try { if (!string.IsNullOrEmpty(url)) Process.Start(url); }
            catch (Exception ex) { _log?.Invoke("Bell: could not open " + url + ": " + ex.Message); }
            CloseMenu();
        }

        private double Font(double size) => Math.Max(10, size * _scale);

        // The date in the plugin language: English keeps "Oct 30, 2026"; a language gets its own
        // culture's short date (the registry's tag), since "MMM d, yyyy" is an English word order.
        private static string FormatDate(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return null;
            if (!DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d)) return null;
            var local = d.ToLocalTime();
            string tag = Locales.ByWeb(Strings.Web)?.Tag;
            if (tag != null)
                try { return local.ToString("d", System.Globalization.CultureInfo.GetCultureInfo(tag)); } catch { }
            return local.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);
        }

        // ── Geometry ─────────────────────────────────────────────────────────────────────────────

        private void Layout()
        {
            var canvas = HostServices.Overlay?.Canvas;
            if (canvas == null || _root == null) return;
            double cw = canvas.ActualWidth, ch = canvas.ActualHeight;
            if (cw <= 0 || ch <= 0) return;

            double scale = SearchButton.ScaleFor(ch);
            bool rescaled = Math.Abs(scale - _scale) > 0.001;
            _scale = scale;

            double size = RefSize * scale;
            double searchLeft = cw - (SearchButton.RefRightInset + SearchButton.RefBtnW) * scale;
            double left = searchLeft - RefGap * scale - size;
            double top = ch - SearchButton.RefBottomInset * scale - size;
            _root.Width = _root.Height = size;
            _root.CornerRadius = new CornerRadius(size / 2);
            _root.BorderThickness = new Thickness(Math.Max(1.0, 2.2 * scale));
            Canvas.SetLeft(_root, left);
            Canvas.SetTop(_root, top);

            double badge = Math.Max(14, 17 * scale);
            foreach (var (b, t) in new[] { (_greenBadge, _greenCount), (_yellowBadge, _yellowCount) })
            {
                b.MinWidth = b.Height = badge;
                b.CornerRadius = new CornerRadius(badge / 2);
                b.Padding = new Thickness(3 * scale, 0, 3 * scale, 0);
                b.Margin = new Thickness(-badge * 0.3, -badge * 0.3, -badge * 0.3, 0);
                t.FontSize = Math.Max(9, 11 * scale);
            }

            if (_menu != null && _menuOpen)
            {
                if (rescaled) BuildMenuContent();
                double w = RefMenuW * scale;
                _menu.Width = w;
                _menu.CornerRadius = new CornerRadius(8 * scale);
                _menu.BorderThickness = new Thickness(Math.Max(1.0, 1.2 * scale));
                _menu.Measure(new Size(w, double.PositiveInfinity));
                double h = _menu.DesiredSize.Height;
                // Right edge on the search button's right edge, just above the corner pills.
                double menuLeft = Math.Max(0, searchLeft + SearchButton.RefBtnW * scale - w);
                Canvas.SetLeft(_menu, menuLeft);
                Canvas.SetTop(_menu, Math.Max(0, top - 8 * scale - h));
            }
        }
    }
}
