using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HsbgCardLookup.Data;
using HsbgCardLookup.Localization;
using Newtonsoft.Json;

namespace HsbgCardLookup.Ui
{
    // The patch notes on the App & updates page: what each version brought, one card per change with a
    // picture and a "Show me" button that opens the setting it is about. The notes ship with the toolkit
    // (data\patchnotes\notes.json + pictures); their words are catalog keys, so every language has them.
    public partial class SettingsWindow
    {
        private sealed class NotesFile
        {
            [JsonProperty("versions")] public List<NotesVersion> Versions { get; set; }
        }

        private sealed class NotesVersion
        {
            [JsonProperty("version")] public string Version { get; set; }
            [JsonProperty("releaseUrl")] public string ReleaseUrl { get; set; }
            [JsonProperty("entries")] public List<NotesEntry> Entries { get; set; }
        }

        private sealed class NotesEntry
        {
            [JsonProperty("id")] public string Id { get; set; }
            [JsonProperty("image")] public string Image { get; set; }
            [JsonProperty("page")] public string Page { get; set; }
            [JsonProperty("row")] public string Row { get; set; }
            [JsonProperty("text")] public string Text { get; set; }
        }

        private static NotesFile _notes;
        private static bool _notesRead;

        private static NotesFile Notes()
        {
            if (_notesRead) return _notes;
            _notesRead = true;
            try
            {
                var path = CardStore.BundledDataPath(Path.Combine("patchnotes", "notes.json"));
                if (path != null) _notes = JsonConvert.DeserializeObject<NotesFile>(File.ReadAllText(path));
            }
            catch { _notes = null; }
            return _notes;
        }

        /// <summary>The newest version the notes describe (null without notes).</summary>
        private static string NewestNotes() => Notes()?.Versions?.FirstOrDefault()?.Version;

        /// <summary>Notes the player has not opened yet: the App &amp; updates tab says "New".</summary>
        private bool NotesUnseen()
        {
            var newest = NewestNotes();
            return newest != null && !string.Equals(_config.NotesSeen, newest, StringComparison.Ordinal);
        }

        private void MarkNotesSeen()
        {
            var newest = NewestNotes();
            if (newest == null || string.Equals(_config.NotesSeen, newest, StringComparison.Ordinal)) return;
            _config.NotesSeen = newest;
            try { _config.Save(); } catch { }
        }

        // "notes.v110.session.title" for version 1.1.0, entry "session".
        private static string NoteKey(NotesVersion v, NotesEntry e, string part) =>
            "notes.v" + new string((v.Version ?? "").Where(char.IsDigit).ToArray()) + "." + e.Id + "." + part;

        private void AddNotes(StackPanel stack)
        {
            var file = Notes();
            if (file?.Versions == null || file.Versions.Count == 0) return;
            stack.Children.Add(GroupHeader(Strings.T("settings.notes.header")));
            foreach (var v in file.Versions)
            {
                if (v?.Entries == null) continue;
                var head = new DockPanel { Margin = new Thickness(0, 4, 0, 8) };
                if (!string.IsNullOrEmpty(v.ReleaseUrl))
                {
                    var all = LinkText(Strings.T("settings.notes.allNotes"), v.ReleaseUrl);
                    DockPanel.SetDock(all, Dock.Right);
                    head.Children.Add(all);
                }
                head.Children.Add(new TextBlock
                {
                    Text = Strings.F("settings.notes.version", v.Version), Foreground = UiKit.TextPrimary,
                    FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
                });
                stack.Children.Add(head);
                foreach (var e in v.Entries)
                    if (e != null && !string.IsNullOrEmpty(e.Id)) stack.Children.Add(NoteCard(v, e));
            }
        }

        private static TextBlock LinkText(string text, string url)
        {
            var link = new TextBlock
            {
                Text = text + "  ↗", Foreground = UiKit.AccentBrush, FontSize = 13, Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            link.MouseEnter += (s, a) => link.TextDecorations = TextDecorations.Underline;
            link.MouseLeave += (s, a) => link.TextDecorations = null;
            link.MouseLeftButtonUp += (s, a) => { try { System.Diagnostics.Process.Start(url); } catch { } };
            return link;
        }

        // One change as a card. "Show me" sits in the card's top-right corner. A wide picture takes the
        // card's whole width with the words under it; a tall one stands at the left with the words beside it.
        private UIElement NoteCard(NotesVersion v, NotesEntry e)
        {
            System.Windows.Media.Imaging.BitmapImage bmp = null;
            try
            {
                var path = string.IsNullOrEmpty(e.Image) ? null : CardStore.BundledDataPath(Path.Combine("patchnotes", e.Image));
                if (path != null) bmp = ImageCache.Load(path, 0);
            }
            catch { }
            bool wide = bmp != null && bmp.PixelWidth >= 1.3 * bmp.PixelHeight;

            // The title, with "Show me" at its right.
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(new TextBlock
            {
                Text = Strings.T(NoteKey(v, e, "title")), Foreground = UiKit.TextPrimary, FontSize = 15, FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
            });
            var open = PageFor(e.Page);
            if (open != null)
            {
                var show = SmallButton(Strings.T("settings.notes.showMe"), () => GoToSetting(open, e.Row != null ? Strings.T(e.Row) : e.Text));
                show.VerticalAlignment = VerticalAlignment.Top;
                show.Margin = new Thickness(10, 0, 0, 0);
                Grid.SetColumn(show, 1);
                head.Children.Add(show);
            }
            var text = new TextBlock
            {
                Text = Strings.T(NoteKey(v, e, "text")), Foreground = UiKit.TextSecondary, FontSize = 13, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            };

            UIElement body;
            if (bmp == null || wide)
            {
                var col = new StackPanel();
                col.Children.Add(head);
                if (bmp != null)
                {
                    // As wide as the card, but never blown up past half again its own size.
                    var img = new Image { Source = bmp, Stretch = Stretch.Uniform, MaxWidth = bmp.PixelWidth * 1.5, HorizontalAlignment = HorizontalAlignment.Left };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    col.Children.Add(new Border { Child = img, CornerRadius = new CornerRadius(6), ClipToBounds = true, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left });
                }
                col.Children.Add(text);
                body = col;
            }
            else
            {
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var img = new Image { Source = bmp, Stretch = Stretch.Uniform, MaxWidth = 170, MaxHeight = 280, VerticalAlignment = VerticalAlignment.Top };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                grid.Children.Add(new Border { Child = img, CornerRadius = new CornerRadius(6), ClipToBounds = true, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top });
                var col = new StackPanel();
                Grid.SetColumn(col, 1);
                col.Children.Add(head);
                col.Children.Add(text);
                grid.Children.Add(col);
                body = grid;
            }

            return new Border
            {
                Child = body, Background = UiKit.Br(UiKit.RowBg), BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8)
            };
        }

        /// <summary>Test aid: the n-th (0-based, all versions) entry's "Show me".</summary>
        public void TestNote(int n)
        {
            var all = (Notes()?.Versions ?? new List<NotesVersion>()).SelectMany(v => v.Entries ?? new List<NotesEntry>()).ToList();
            if (n < 0 || n >= all.Count) return;
            var e = all[n]; var open = PageFor(e.Page);
            if (open != null) GoToSetting(open, e.Row != null ? Strings.T(e.Row) : e.Text);
        }

        private Action PageFor(string page)
        {
            switch (page)
            {
                case "panels": return BuildPanels;
                case "mmr": return BuildMmr;
                case "boards": return BuildBoards;
                case "gifts": return BuildDarkGifts;
                case "search": return BuildCardSearch;
                case "trinkets": return BuildTrinkets;
                case "anomaly": return BuildAnomaly;
                case "tools": return BuildTools;
                case "config": return BuildConfig;
                case "data": return BuildMatchData;
                case "app": return BuildUpdates;
                case "language": return BuildLanguage;
                default: return null;
            }
        }

        // Opens a page and points at one setting on it: scrolled into view, the rest of the page dimmed
        // for a moment, as a search for it would.
        private void GoToSetting(Action page, string label)
        {
            page();
            if (string.IsNullOrEmpty(label)) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (_pageRoot == null) return;
                var rows = new List<FrameworkElement>();
                for (int i = 1; i < _pageRoot.Children.Count; i++)
                {
                    var child = _pageRoot.Children[i] as FrameworkElement;
                    if (child != null && !ReferenceEquals(child, _status)) Flatten(child, rows);
                }
                var target = rows.FirstOrDefault(r => AllText(r).IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0);
                if (target == null) return;
                foreach (var r in rows) r.Opacity = ReferenceEquals(r, target) ? 1 : 0.22;
                try { target.BringIntoView(); } catch { }
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
                timer.Tick += (s, a) => { timer.Stop(); foreach (var r in rows) r.Opacity = 1; };
                timer.Start();
            }));
        }

        private static void Flatten(FrameworkElement el, List<FrameworkElement> into)
        {
            var inner = RowsOf(el);
            if (inner != null) { foreach (var c in inner.Children.OfType<FrameworkElement>()) Flatten(c, into); return; }
            into.Add(el);
        }

        // Every word a row shows, whatever its colour (a ready-made config's name is drawn in its own).
        private static string AllText(DependencyObject node)
        {
            var sb = new System.Text.StringBuilder();
            Action<DependencyObject> walk = null;
            walk = d =>
            {
                if (d is TextBlock t) { sb.Append(t.Text).Append('\n'); return; }
                foreach (object c in LogicalTreeHelper.GetChildren(d)) if (c is DependencyObject o) walk(o);
            };
            walk(node);
            return sb.ToString();
        }
    }
}
