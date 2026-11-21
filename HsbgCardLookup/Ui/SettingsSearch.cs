using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using HsbgCardLookup.Localization;

namespace HsbgCardLookup.Ui
{
    // Search over the settings: type a word and the tab column keeps only the pages that hold it, and on
    // the open page the rows that hold it stay lit while the rest are dimmed.
    //
    // What a page "holds" is the names it actually shows: each page is built once, off screen, and the
    // labels of its settings are collected (per language; built again after a language change). The mods
    // page is known by its title alone: building it scans a folder and asks the site.
    public partial class SettingsWindow
    {
        private Border _searchField;
        private TextBox _searchBox;
        private string _term = "";
        private bool _indexing, _searchTyping;
        private string _indexWeb;
        private readonly Dictionary<Action, string> _pageText = new Dictionary<Action, string>();
        private readonly List<KeyValuePair<Action, FrameworkElement>> _navTabs = new List<KeyValuePair<Action, FrameworkElement>>();
        private TextBlock _navNone;

        // The one search box, moved into each freshly built tab column (it keeps its text and caret).
        private UIElement SearchBox()
        {
            if (_searchField == null)
            {
                _searchField = UiKit.SearchField(Strings.T("settings.search.placeholder"), out _searchBox, 13);
                _searchField.Margin = new Thickness(0, 0, 0, 12);
                _searchField.Padding = new Thickness(8, 5, 8, 5);
                _searchBox.TextChanged += (s, e) => OnSearchChanged();
            }
            (_searchField.Parent as Panel)?.Children.Remove(_searchField);
            return _searchField;
        }

        /// <summary>Test aid: as if typed into the search box.</summary>
        public void TestSearch(string term) { SearchBox(); _searchBox.Text = term ?? ""; if (_searchField.Parent == null) _currentPage?.Invoke(); }

        /// <summary>Test aid: open a page by name ("panels", "mmr", "boards", "gifts", "search", "app") and save all of it, the
        /// part below the window's edge too, as a PNG. "name@2.5" waits that many seconds first, so a
        /// looping picture is caught mid-pass.</summary>
        public void TestSnap(string page, string path)
        {
            double wait = 0;
            int at = page.IndexOf('@');
            if (at > 0)
            {
                double.TryParse(page.Substring(at + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out wait);
                page = page.Substring(0, at);
            }
            if (page == "mmr") BuildMmr(); else if (page == "boards") BuildBoards(); else if (page == "search") BuildCardSearch();
            else if (page == "app") BuildUpdates(); else if (page == "gifts") BuildDarkGifts(); else BuildPanels();
            Action shoot = null;
            if (wait > 0)
            {
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(wait) };
                timer.Tick += (s, e) => { timer.Stop(); shoot(); };
                timer.Start();
            }
            else Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => shoot()));
            shoot = () =>
            {
                try
                {
                    var el = _pageRoot as FrameworkElement;
                    int w = (int)Math.Ceiling(el.ActualWidth), h = (int)Math.Ceiling(el.ActualHeight);
                    var shot = new DrawingVisual();
                    using (var dc = shot.RenderOpen())
                    {
                        dc.DrawRectangle(Background ?? Brushes.Black, null, new Rect(0, 0, w, h));
                        dc.DrawRectangle(new VisualBrush(el) { Stretch = Stretch.None }, null, new Rect(0, 0, w, h));
                    }
                    var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                    bmp.Render(shot);
                    var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                    using (var fs = System.IO.File.Create(path)) png.Save(fs);
                }
                catch { }
            };
        }

        private void OnSearchChanged()
        {
            string term = (_searchBox.Text ?? "").Trim().ToLowerInvariant();
            if (term == _term) return;
            bool cleared = term.Length == 0 && _term.Length > 0;
            _term = term;
            _searchTyping = true;
            try
            {
                if (cleared) { _currentPage?.Invoke(); return; }       // a fresh page: nothing dimmed
                EnsureIndex();
                FilterNav();
                // The open page has nothing to show for it: step to the first page that has.
                var hit = _navTabs.FirstOrDefault(t => Holds(t.Key));
                if (!Holds(_currentPage) && hit.Key != null) hit.Key();
                else MarkPage();
            }
            finally
            {
                _searchTyping = false;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    try { _searchBox.Focus(); _searchBox.CaretIndex = _searchBox.Text.Length; } catch { }
                }));
            }
        }

        private bool Holds(Action page)
        {
            if (_term.Length == 0) return true;
            string text;
            if (page == null || !_pageText.TryGetValue(page, out text)) return false;
            foreach (string word in _term.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (text.IndexOf(word, StringComparison.Ordinal) < 0) return false;
            return true;
        }

        private static bool HoldsText(string text, string term)
        {
            foreach (string word in term.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (text.IndexOf(word, StringComparison.Ordinal) < 0) return false;
            return true;
        }

        // Every page's text, read off the page itself.
        private void EnsureIndex()
        {
            if (_indexWeb == Strings.Web && _pageText.Count > 0) return;
            var open = _currentPage;
            _pageText.Clear();
            _indexing = true;
            try
            {
                foreach (var tab in _navTabs.ToList())
                {
                    if (tab.Key == (Action)BuildTools) { _pageText[tab.Key] = Strings.T("settings.tools.title").ToLowerInvariant(); continue; }
                    try
                    {
                        tab.Key();
                        var sb = new StringBuilder();
                        Collect(_pageRoot, sb);
                        _pageText[tab.Key] = sb.ToString().ToLowerInvariant();
                    }
                    catch { _pageText[tab.Key] = ""; }
                }
                _indexWeb = Strings.Web;
            }
            finally
            {
                _indexing = false;
                _status.Text = "";
                open?.Invoke();           // the page that was open, built for real again
            }
        }

        private static void Collect(DependencyObject node, StringBuilder into)
        {
            if (node == null) return;
            var text = node as TextBlock;
            if (text != null)
            {
                // Names only: a setting's label and a page's title are drawn in the primary text colour;
                // descriptions, hints, group headings and the values on the switches are not, and a
                // search that read those found "turn" on pages with no turn setting at all.
                if (!ReferenceEquals(text.Foreground, UiKit.TextPrimary)) return;
                if (!string.IsNullOrEmpty(text.Text)) into.Append(text.Text).Append('\n');
                else foreach (var run in text.Inlines.OfType<Run>()) into.Append(run.Text).Append(' ');
                return;
            }
            foreach (object child in LogicalTreeHelper.GetChildren(node))
                Collect(child as DependencyObject, into);
        }

        private void FilterNav()
        {
            bool any = false;
            foreach (var tab in _navTabs)
            {
                bool show = Holds(tab.Key);
                any |= show;
                tab.Value.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
            if (_navNone != null) _navNone.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        }

        // On the open page: a row that holds the term stays lit, the others step back.
        private void MarkPage()
        {
            if (_term.Length == 0 || _pageRoot == null) return;
            FrameworkElement first = null;
            for (int i = 0; i < _pageRoot.Children.Count; i++)
            {
                var child = _pageRoot.Children[i] as FrameworkElement;
                if (child == null || i == 0 || ReferenceEquals(child, _status)) continue;     // the page's title and the notice line
                Mark(child, ref first);
            }
            try { first?.BringIntoView(); } catch { }
        }

        private void Mark(FrameworkElement el, ref FrameworkElement first)
        {
            var inner = RowsOf(el);
            if (inner != null)
            {
                foreach (var child in inner.Children.OfType<FrameworkElement>()) Mark(child, ref first);
                return;
            }
            var sb = new StringBuilder();
            Collect(el, sb);
            bool hit = sb.Length > 0 && HoldsText(sb.ToString().ToLowerInvariant(), _term);
            el.Opacity = hit ? 1 : 0.22;
            if (hit && first == null) first = el;
        }

        // A stack of rows (the page's body, or the frame a feature page wraps it in), as opposed to one row.
        private static StackPanel RowsOf(FrameworkElement el)
        {
            var stack = el as StackPanel;
            if (stack != null && stack.Orientation == Orientation.Vertical && stack.Children.Count > 1) return stack;
            var grid = el as Grid;
            if (grid != null && grid.Children.Count > 0)
            {
                stack = grid.Children[0] as StackPanel;
                if (stack != null && stack.Orientation == Orientation.Vertical && stack.Children.Count > 1) return stack;
            }
            return null;
        }
    }
}
