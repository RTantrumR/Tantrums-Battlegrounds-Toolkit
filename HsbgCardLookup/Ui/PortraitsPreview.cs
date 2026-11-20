using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HsbgCardLookup.Config;
using HsbgCardLookup.Data;

namespace HsbgCardLookup.Ui
{
    /// <summary>
    /// What the settings put by the leaderboard portraits, for the settings page: the REAL
    /// <see cref="LeaderboardOverlay"/> drawn over a picture of the game's own leaderboard
    /// (data\board\leaderboard-demo.png, a 1920×1080 capture's left edge), pixel for pixel as in a
    /// 1080-high game. Four of the eight portraits are in view, the next opponent's shifted tile
    /// among them; all eight would be taller than the window.
    /// </summary>
    internal sealed class PortraitsPreview
    {
        // Where the picture's top-left corner lies in the 1920×1080 frame it was cut from: found by
        // matching it against a capture of the same replay moment (mean difference 0.6 of 255 there).
        private const double PicX = 233, PicY = 117, PicW = 194, PicH = 814;
        // The part of the picture in view: portraits four to seven.
        private const double ViewTop = 304, ViewH = 386;

        private readonly PluginConfig _config;
        private readonly Border _viewport;
        private readonly Canvas _stage;
        private readonly LeaderboardOverlay _overlay;

        public FrameworkElement Root => _viewport;

        public PortraitsPreview(PluginConfig config, double viewW)
        {
            _config = config;
            _stage = new Canvas { Width = PreviewStage.FallbackW, Height = PreviewStage.FallbackH, IsHitTestVisible = false };

            var picture = new Image { Width = PicW, Height = PicH, Stretch = Stretch.Fill };
            try { picture.Source = ImageCache.Load(CardStore.BoardAssetPath("leaderboard-demo"), 0); } catch { }
            // The picture ends where the capture was cut: let that edge fade into the backdrop.
            picture.OpacityMask = new LinearGradientBrush(
                new GradientStopCollection { new GradientStop(Colors.Black, 0), new GradientStop(Colors.Black, 0.82), new GradientStop(Colors.Transparent, 1) },
                new Point(0, 0), new Point(1, 0));
            Canvas.SetLeft(picture, PicX); Canvas.SetTop(picture, PicY);
            _stage.Children.Add(picture);
            Canvas.SetLeft(_stage, -PicX); Canvas.SetTop(_stage, -(PicY + ViewTop));

            var clip = new Canvas { ClipToBounds = true, IsHitTestVisible = false };
            clip.Children.Add(_stage);
            _viewport = new Border
            {
                Width = viewW, Height = ViewH,
                BorderBrush = UiKit.StrokeBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
                Background = new LinearGradientBrush(
                    Color.FromRgb(0x18, 0x1F, 0x2C), Color.FromRgb(0x0D, 0x11, 0x19), new Point(0.35, 0), new Point(0.65, 1)),
                Margin = new Thickness(0, 2, 0, 10),
                Child = clip
            };

            _overlay = new LeaderboardOverlay(_stage);
            // The overlay lays out against its canvas's measured size, which exists once the page does.
            _viewport.Loaded += (s, e) => Refresh();
        }

        /// <summary>Re-read the settings and draw again. Called on every settings change.</summary>
        public void Refresh()
        {
            try
            {
                _overlay.ShowNames = _config.NamesOnPortraits;
                _overlay.HeroNames = _config.HeroNames;
                _overlay.ShowRating = _config.MmrOnPortraits;
                _overlay.ShowDeltas = _config.MmrOnPortraits && _config.ShowMmrDeltas;
                _overlay.ShowTiers = PluginConfig.OnPortraits(_config.TavernTierMode);
                _overlay.ShowTribes = PluginConfig.OnPortraits(_config.OpponentTribeMode);
                _overlay.ShowLastOpp = _config.ShowLastOpponent;
                _overlay.DimDead = _config.DimDeadPlayers;
                _overlay.UserScale = _config.PortraitSize / 100.0;
                _overlay.SetStandings(Rows());
            }
            catch { }
        }

        public void Close()
        {
            try { _overlay.Detach(); } catch { }
        }

        // The sidebar preview's players, with what the picture shows: its sixth tile is the next
        // opponent's (drawn further right), and every player in view has a type and a count.
        private static List<LeaderboardOverlay.Row> Rows()
        {
            var rows = MmrSidePanel.SampleRows(false);
            rows[4].Tribe = "Mech"; rows[4].TribeCount = 3;
            rows[5].Tribe = "Undead"; rows[5].TribeCount = 6;
            rows[5].IsCurrentOpponent = true;
            rows[6].IsLastOpponent = true;
            rows[7].IsDead = false;
            return rows;
        }
    }
}
