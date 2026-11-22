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

            stack.Children.Add(new Border
            {
                Background = UiKit.Br(Color.FromArgb(0x26, 0xFF, 0x4D, 0x4D)), BorderBrush = UiKit.Br(Color.FromRgb(0xB8, 0x3A, 0x3A)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 10),
                Margin = new Thickness(0, 0, 0, 6),
                Child = new TextBlock
                {
                    Text = Strings.T("settings.config.warning"), Foreground = UiKit.Br(WarnRed), FontSize = 13.5,
                    FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap
                }
            });

            // ── The file ──
            stack.Children.Add(GroupHeader(Strings.T("settings.config.fileHeader")));
            string file = Path.Combine(PluginConfig.DataDir, "config.xml");
            var fileRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
            var open = SmallButton(Strings.T("settings.config.openFolder"), () => OpenUrl(PluginConfig.DataDir));
            open.Margin = new Thickness(10, 0, 0, 0);
            DockPanel.SetDock(open, Dock.Right);
            fileRow.Children.Add(open);
            fileRow.Children.Add(new TextBlock
            {
                Text = file, ToolTip = file, Foreground = UiKit.TextSecondary, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            stack.Children.Add(fileRow);

            // ── Out to a file, in from one ──
            stack.Children.Add(GroupHeader(Strings.T("settings.config.shareHeader")));
            var share = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            share.Children.Add(SmallButton(Strings.T("settings.config.export"), ExportConfig));
            share.Children.Add(SmallButton(Strings.T("settings.config.import"), ImportConfig));
            stack.Children.Add(share);
            stack.Children.Add(new TextBlock
            {
                Text = Strings.T("settings.config.shareHint"), Foreground = UiKit.TextMuted, FontSize = 12.5, TextWrapping = TextWrapping.Wrap
            });

            // ── Ready-made configs: a list like the mods page, the other trackers pinned first ──
            stack.Children.Add(GroupHeader(Strings.T("settings.config.readyHeader")));
            stack.Children.Add(new TextBlock
            {
                Text = Strings.T("settings.config.readyHint"), Foreground = UiKit.TextMuted, FontSize = 12.5,
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
                stack.Children.Add(new TextBlock { Text = Strings.T("settings.config.presetsNone"), Foreground = UiKit.TextMuted, FontSize = 13 });
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
            public Color Fill, FillDark, Rim, Accent, Text;   // the card in that app's own colours
            public KeyValuePair<string, Action<PluginConfig>>[] Off;   // label key → the switch it turns off
        }

        private static KeyValuePair<string, Action<PluginConfig>> Part(string key, Action<PluginConfig> off) =>
            new KeyValuePair<string, Action<PluginConfig>>(key, off);

        private static readonly ReadyConfig[] ReadyConfigs =
        {
            // Firestone's stylesheet, its common palette.
            new ReadyConfig
            {
                Name = "Firestone", ForKey = "settings.config.ready.firestone", Pinned = true,
                Fill = Color.FromRgb(0x47, 0x24, 0x06), FillDark = Color.FromRgb(0x19, 0x05, 0x05), Rim = Color.FromRgb(0x88, 0x26, 0x09),
                Accent = Color.FromRgb(0xFF, 0xB9, 0x48), Text = Color.FromRgb(0xD9, 0xC3, 0xAB),
                Off = new[]
                {
                    Part("settings.darkGifts.title", c => c.ShowDarkGifts = false),
                    Part("settings.general.turnCounter", c => c.ShowTurnCounter = false),
                    Part("settings.lobbyTribes.label", c => c.ShowLobbyTribes = false),
                    Part("settings.config.ready.lastOpponent", c => c.ShowLastOpponent = false),
                    Part("settings.boards.label", c => c.ShowOpponentBoards = false),
                }
            },
            // HDT's Battlegrounds overlay: its panel greys and the tier buttons' orange.
            new ReadyConfig
            {
                Name = "Hearthstone Deck Tracker", ForKey = "settings.config.ready.hdt", Pinned = true,
                Fill = Color.FromRgb(0x2C, 0x31, 0x35), FillDark = Color.FromRgb(0x14, 0x16, 0x17), Rim = Color.FromRgb(0x3F, 0x43, 0x46),
                Accent = Color.FromRgb(0xFF, 0x9F, 0x1C), Text = Color.FromRgb(0xCF, 0xD3, 0xD6),
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

            var chips = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
            chips.Children.Add(new TextBlock
            {
                Text = Strings.T("settings.config.ready.turnsOff"), FontSize = 12, Foreground = new SolidColorBrush(r.Text), Opacity = 0.8,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 4)
            });
            foreach (var part in r.Off)
                chips.Children.Add(new Border
                {
                    BorderBrush = new SolidColorBrush(r.Rim), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(0, 0, 5, 4),
                    Child = new TextBlock { Text = Strings.T(part.Key), FontSize = 12, Foreground = new SolidColorBrush(r.Text) }
                });
            col.Children.Add(chips);

            Func<byte, Brush> tint = a => new SolidColorBrush(Color.FromArgb(a, r.Accent.R, r.Accent.G, r.Accent.B));
            var apply = new Border
            {
                Background = tint(0x26), BorderBrush = new SolidColorBrush(r.Accent), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7),
                Padding = new Thickness(14, 6, 14, 6), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0),
                Child = new TextBlock { Text = Strings.T("settings.config.ready.apply"), FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(r.Accent) }
            };
            apply.MouseEnter += (s, e) => apply.Background = tint(0x4D);
            apply.MouseLeave += (s, e) => apply.Background = tint(0x26);
            apply.MouseLeftButtonUp += (s, e) => { e.Handled = true; ApplyReady(r); };

            var dock = new DockPanel();
            DockPanel.SetDock(apply, Dock.Right);
            dock.Children.Add(apply);
            dock.Children.Add(col);
            return new Border
            {
                Background = new LinearGradientBrush(r.Fill, r.FillDark, 90), BorderBrush = new SolidColorBrush(r.Rim), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 11, 12, 11), Margin = new Thickness(0, 0, 0, 8), Child = dock
            };
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
