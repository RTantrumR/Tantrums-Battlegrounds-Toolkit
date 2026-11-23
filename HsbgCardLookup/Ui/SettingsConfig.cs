using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HsbgCardLookup.Config;
using HsbgCardLookup.Data;
using HsbgCardLookup.Localization;

namespace HsbgCardLookup.Ui
{
    // The Config page: where config.xml lives, settings out to a file and in from one, and named
    // presets. Everything read here goes through ConfigShare.Read (see there for the checks).
    public partial class SettingsWindow
    {
        /// <summary>Set by the app: the settings changed wholesale (an import, a preset), so every
        /// box re-reads its place and size as well as its switches.</summary>
        public Action ReloadLayout { get; set; }

        private static readonly Color WarnRed = Color.FromRgb(0xFF, 0x6B, 0x6B);
        private string _presetDraft = "";

        private void BuildConfig()
        {
            _currentPage = BuildConfig;
            var stack = NewPage(Strings.T("settings.config.title"), sub: true);

            // The trust warning and the config folder sit by the title as icons: both are worth a look
            // once, neither needs a row of the page every time.
            string file = Path.Combine(PluginConfig.DataDir, "config.xml");
            if (stack.Children.Count > 0 && stack.Children[0] is DockPanel head)
            {
                var folder = IconButton("", Strings.T("settings.config.openFolder") + "\n" + file, () => OpenUrl(PluginConfig.DataDir));
                // A drawn folder: the icon font's folder glyphs read as a door or a page at this size.
                var shape = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M1.5,3.5 L6,3.5 L7.5,5 L14.5,5 L14.5,12.5 L1.5,12.5 Z"), Stroke = UiKit.TextSecondary, StrokeThickness = 1.3,
                    StrokeLineJoin = PenLineJoin.Round, Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
                folder.Child = shape;
                folder.MouseEnter += (s, e) => shape.Stroke = UiKit.AccentBrush;
                folder.MouseLeave += (s, e) => shape.Stroke = UiKit.TextSecondary;
                folder.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(folder, Dock.Right);
                head.Children.Insert(0, folder);
                var hazard = new Border
                {
                    Width = 32, Height = 32, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 0, 0), ToolTip = Strings.T("settings.config.warning"),
                    Child = new TextBlock
                    {
                        Text = "\uE7BA", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 18, Foreground = UiKit.Br(WarnRed),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    }
                };
                ToolTipService.SetInitialShowDelay(hazard, 150);
                DockPanel.SetDock(hazard, Dock.Right);
                head.Children.Insert(0, hazard);
            }

            // ── Out to a file, in from one ──
            var shareHead = new StackPanel { Orientation = Orientation.Horizontal };
            var shareLabel = GroupHeader(Strings.T("settings.config.shareHeader"), first: true);
            shareHead.Children.Add(shareLabel);
            shareHead.Children.Add(HelpDot(Strings.T("settings.config.shareHint"), shareLabel.Margin));
            stack.Children.Add(shareHead);
            var share = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            share.Children.Add(SmallButton(Strings.T("settings.config.export"), ExportConfig));
            share.Children.Add(SmallButton(Strings.T("settings.config.import"), ImportConfig));
            stack.Children.Add(share);

            // ── Ready-made configs: a list like the mods page, the other trackers pinned first ──
            stack.Children.Add(GroupHeader(Strings.T("settings.config.readyHeader")));
            stack.Children.Add(new TextBlock
            {
                Text = Strings.T("settings.config.readyHint"), Foreground = UiKit.TextPrimary, FontSize = 13,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            });
            foreach (var r in ReadyConfigs.OrderByDescending(r => r.Pinned)) stack.Children.Add(ReadyCard(r));

            // ── Presets ──
            stack.Children.Add(GroupHeader(Strings.T("settings.config.presetsHeader")));
            var nameBox = new TextBox
            {
                Text = _presetDraft, MaxLength = 32, FontSize = 13.5, Background = Brushes.Transparent, Foreground = UiKit.TextPrimary,
                CaretBrush = UiKit.TextPrimary, BorderThickness = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center
            };
            var hint = new TextBlock
            {
                Text = Strings.T("settings.config.presetName"), Foreground = UiKit.TextMuted, FontSize = 13.5, IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0),
                Visibility = _presetDraft.Length == 0 ? Visibility.Visible : Visibility.Collapsed
            };
            nameBox.TextChanged += (s, e) =>
            {
                _presetDraft = nameBox.Text ?? "";
                hint.Visibility = _presetDraft.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            };
            var field = new Grid();
            field.Children.Add(hint);
            field.Children.Add(nameBox);
            var save = SmallButton(Strings.T("settings.config.presetSave"), () => SavePreset((_presetDraft ?? "").Trim()));
            save.Margin = new Thickness(10, 0, 0, 0);
            nameBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; SavePreset((_presetDraft ?? "").Trim()); } };
            var saveRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(save, Dock.Right);
            saveRow.Children.Add(save);
            saveRow.Children.Add(new Border
            {
                Background = UiKit.Br(UiKit.FieldBg), BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 5, 9, 5), Child = field
            });
            stack.Children.Add(saveRow);

            var presets = ConfigShare.Presets();
            if (presets.Count == 0)
                stack.Children.Add(new TextBlock { Text = Strings.T("settings.config.presetsNone"), Foreground = UiKit.TextPrimary, FontSize = 13 });
            foreach (string name in presets)
            {
                string preset = name;
                var row = new DockPanel { LastChildFill = true };
                var delete = SmallButton(Strings.T("settings.config.presetDelete"), () => DeletePreset(preset));
                delete.Margin = new Thickness(8, 0, 0, 0);
                var load = SmallButton(Strings.T("settings.config.presetLoad"), () => LoadPreset(preset));
                load.Margin = new Thickness(8, 0, 0, 0);
                DockPanel.SetDock(delete, Dock.Right);
                DockPanel.SetDock(load, Dock.Right);
                row.Children.Add(delete);
                row.Children.Add(load);
                row.Children.Add(new TextBlock
                {
                    Text = preset, Foreground = UiKit.TextPrimary, FontSize = 14, VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                stack.Children.Add(new Border
                {
                    Background = UiKit.Br(UiKit.RowBg), BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 8, 7), Margin = new Thickness(0, 0, 0, 6), Child = row
                });
            }

            ShowPage();
        }

        // ── Ready-made configs ──
        // Each one changes only the switches it lists. The other trackers' entries turn off what that
        // app draws itself out of the box (docs/research/compat-firestone-hdt.md): Firestone 19.4.7's
        // settings defaults, HDT 1.60.7's overlay code.

        private sealed class ReadyConfig
        {
            public string Name;        // a product name, shown as it is
            public string ForKey;      // who it is for, one line
            public bool Pinned;
            public string Logo;        // data\logos\<Logo>.png, the app's own mark
            public Color Fill, FillDark, Rim, Accent, Text;   // the card in that app's own colours
            public KeyValuePair<string, Action<PluginConfig>>[] Off;   // label key → the switch it turns off
        }

        private static KeyValuePair<string, Action<PluginConfig>> Part(string key, Action<PluginConfig> off) =>
            new KeyValuePair<string, Action<PluginConfig>>(key, off);

        private static readonly ReadyConfig[] ReadyConfigs =
        {
            // Firestone's launcher icon: the flame's amber on its brown, framed in its gold-brown.
            new ReadyConfig
            {
                Name = "Firestone", ForKey = "settings.config.ready.firestone", Pinned = true, Logo = "firestone",
                Fill = Color.FromRgb(0x43, 0x29, 0x1D), FillDark = Color.FromRgb(0x28, 0x15, 0x10), Rim = Color.FromRgb(0x8E, 0x5B, 0x23),
                Accent = Color.FromRgb(0xFF, 0xB9, 0x48), Text = Color.FromRgb(0xF0, 0xE2, 0xD0),
                Off = new[]
                {
                    Part("settings.darkGifts.title", c => c.ShowDarkGifts = false),
                    Part("settings.general.turnCounter", c => c.ShowTurnCounter = false),
                    Part("settings.lobbyTribes.label", c => c.ShowLobbyTribes = false),
                    Part("settings.config.ready.lastOpponent", c => c.ShowLastOpponent = false),
                    Part("settings.boards.label", c => c.ShowOpponentBoards = false),
                }
            },
            // HDT: its black-and-white mark, on its overlay's panel greys.
            new ReadyConfig
            {
                Name = "Hearthstone Deck Tracker", ForKey = "settings.config.ready.hdt", Pinned = true, Logo = "hdt",
                Fill = Color.FromRgb(0x3A, 0x40, 0x45), FillDark = Color.FromRgb(0x23, 0x27, 0x2A), Rim = Color.FromRgb(0x58, 0x5F, 0x64),
                Accent = Color.FromRgb(0xEE, 0xF1, 0xF4), Text = Color.FromRgb(0xE2, 0xE6, 0xEA),
                Off = new[]
                {
                    Part("settings.general.turnCounter", c => c.ShowTurnCounter = false),
                    Part("settings.lobbyTribes.label", c => c.ShowLobbyTribes = false),
                    Part("settings.boards.label", c => c.ShowOpponentBoards = false),
                }
            },
        };

        private UIElement ReadyCard(ReadyConfig r)
        {
            var col = new StackPanel();
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            if (r.Pinned)
                name.Children.Add(new TextBlock
                {
                    Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13, Foreground = new SolidColorBrush(r.Accent),
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 7, 0)
                });
            name.Children.Add(new TextBlock { Text = r.Name, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(r.Accent) });
            col.Children.Add(name);
            col.Children.Add(new TextBlock
            {
                Text = Strings.T(r.ForKey), FontSize = 12.5, Foreground = new SolidColorBrush(r.Text), TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            });

            Func<byte, Brush> tint = a => new SolidColorBrush(Color.FromArgb(a, r.Accent.R, r.Accent.G, r.Accent.B));
            var apply = new Border
            {
                Background = tint(0x26), BorderBrush = new SolidColorBrush(r.Accent), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7),
                Padding = new Thickness(14, 6, 14, 6), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
                Child = new TextBlock { Text = Strings.T("settings.config.ready.apply"), FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(r.Accent) }
            };
            apply.ToolTip = Strings.T("settings.config.ready.turnsOff") + " " + string.Join(", ", r.Off.Select(part => Strings.T(part.Key)));
            apply.MouseEnter += (s, e) => apply.Background = tint(0x4D);
            apply.MouseLeave += (s, e) => apply.Background = tint(0x26);
            apply.MouseLeftButtonUp += (s, e) => { e.Handled = true; ApplyReady(r); };

            var dock = new DockPanel();
            DockPanel.SetDock(apply, Dock.Right);
            dock.Children.Add(apply);
            ImageSource logo = null;
            try { if (r.Logo != null) logo = ImageCache.Load(CardStore.LogoPath(r.Logo), 0); } catch { }
            if (logo != null)
            {
                var mark = new Image { Source = logo, Width = 34, Height = 34, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                RenderOptions.SetBitmapScalingMode(mark, BitmapScalingMode.HighQuality);
                DockPanel.SetDock(mark, Dock.Left);
                dock.Children.Add(mark);
            }
            dock.Children.Add(col);
            return new Border
            {
                Background = new LinearGradientBrush(r.Fill, r.FillDark, 90), BorderBrush = new SolidColorBrush(r.Rim), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 11, 12, 11), Margin = new Thickness(0, 0, 0, 8), Child = dock
            };
        }

        // A small round "?" beside a group header; what the group does is in its tooltip.
        private static Border HelpDot(string tip, Thickness besideHeader)
        {
            var dot = new Border
            {
                Width = 16, Height = 16, CornerRadius = new CornerRadius(8), BorderBrush = UiKit.TextSecondary, BorderThickness = new Thickness(1),
                Background = Brushes.Transparent, Cursor = Cursors.Help, ToolTip = tip, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(7, besideHeader.Top - 1, 0, 0),
                Child = new TextBlock
                {
                    Text = "?", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = UiKit.TextSecondary,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            ToolTipService.SetInitialShowDelay(dot, 150);
            ToolTipService.SetShowDuration(dot, 20000);
            return dot;
        }

        private void ApplyReady(ReadyConfig r)
        {
            FlushPending();
            foreach (var part in r.Off) part.Value(_config);
            Changed();
            _status.Text = Strings.F("settings.config.readyApplied", r.Name);
        }

        private void ExportConfig()
        {
            FlushPending();
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = Strings.T("settings.config.fileType") + "|*" + ConfigShare.Extension, DefaultExt = ConfigShare.Extension,
                AddExtension = true, FileName = "toolkit" + ConfigShare.Extension, OverwritePrompt = true
            };
            if (dlg.ShowDialog(this) != true) return;
            string path = dlg.FileName;
            if (!string.Equals(Path.GetExtension(path), ConfigShare.Extension, StringComparison.OrdinalIgnoreCase)) path += ConfigShare.Extension;
            _status.Text = Strings.T(ConfigShare.WriteFile(path, _config) ? "settings.config.exported" : "settings.config.exportFailed");
        }

        private void ImportConfig()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = Strings.T("settings.config.fileType") + "|*" + ConfigShare.Extension, CheckFileExists = true, Multiselect = false
            };
            if (dlg.ShowDialog(this) != true) return;
            var read = ConfigShare.ReadFile(dlg.FileName);
            if (!Take(read)) return;
            _status.Text = Strings.F("settings.config.imported", read.Count);
        }

        private void SavePreset(string name)
        {
            FlushPending();
            if (!ConfigShare.NameOk(name)) { _status.Text = Strings.T("settings.config.presetNameBad"); return; }
            bool ok = ConfigShare.SavePreset(name, _config);
            if (ok) _presetDraft = "";
            BuildConfig();
            _status.Text = ok ? Strings.F("settings.config.presetSaved", name) : Strings.T("settings.config.presetFull");
        }

        private void LoadPreset(string name)
        {
            if (!Take(ConfigShare.ReadPreset(name))) return;
            _status.Text = Strings.F("settings.config.presetLoaded", name);
        }

        private void DeletePreset(string name)
        {
            ConfigShare.DeletePreset(name);
            BuildConfig();
            _status.Text = Strings.F("settings.config.presetDeleted", name);
        }

        // A file that passed every check becomes the settings; one that did not changes nothing.
        private bool Take(ConfigShare.Result read)
        {
            if (!read.Ok)
            {
                string key = "settings.config.err." + read.Problem.ToString().ToLowerInvariant();
                _status.Text = read.Problem == ConfigShare.Problem.Content ? Strings.F(key, read.Detail) : Strings.T(key);
                return false;
            }
            FlushPending();
            read.ApplyTo(_config);
            if (ReloadLayout != null) ReloadLayout(); else _onChanged();
            BuildConfig();
            return true;
        }
    }
}
