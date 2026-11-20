using System;
using System.Diagnostics;
using System.IO;
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
