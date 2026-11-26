using System;
using System.IO;
using System.Windows.Input;
using System.Xml.Serialization;
using HsbgCardLookup.Localization;

namespace HsbgCardLookup.Config
{
    /// <summary>
    /// Persisted plugin settings. Stored as XML under the plugin's data folder so it
    /// survives DLL redeploys (which only replace the bin output).
    /// </summary>
    public class PluginConfig
    {
        // Summon hotkey (WPF Key name). Name kept "BrowserKey" for config back-compat.
        public string BrowserKey { get; set; } = "F3";
        // Toggle golden art; acts only when the search box isn't focused.
        public string GoldenKey { get; set; } = "G";
        // Re-focus the search box; acts only when it isn't already focused.
        public string FocusKey { get; set; } = "S";
        // Whether duos-only cards appear in results/browse.
        public bool ShowDuos { get; set; } = true;
        // The plugin's language — card names, text, art and the game's own words (tribes, keywords,
        // card types) in the search and the filters, AND the plugin's own menus, tooltips and
        // messages. "" = follow HDT's own language setting, "en" = English whatever HDT runs in,
        // or one of the site's card-language codes (Localization.Locales.Cards: de, es, es-mx, fr,
        // it, ja, ko, pl, pt, th, zh-cn, zh-tw). The card data is the site's /api/cards?locale=
        // snapshot, fetched when the language is used; the plugin's texts are bundled catalogs
        // (data\ui\<web>.json). Switching downloads nothing by itself.
        public string Language { get; set; } = "";
        // Magnifying-glass button on the in-game overlay (left of the game's card-list book,
        // bottom-right) that toggles the search — a mouse path for players without the hotkey.
        public bool ShowSearchButton { get; set; } = true;
        // Where the search button and the bell beside it were arranged to: XF/YF = the pill's middle
        // (canvas fractions), WF = a size factor on the game's height scale. Unset = the corner spot;
        // an unarranged bell keeps to the left of the search button, wherever that is.
        public HudPlacement SearchButtonHud { get; set; } = new HudPlacement();
        public HudPlacement BellHud { get; set; } = new HudPlacement();
        // Floating cards dragged onto the screen hide together with the overlay (and reappear when
        // it reopens). When false, they live independently and survive the overlay closing/unfocusing.
        public bool HideDraggedWithApp { get; set; } = true;
        // Allow pulling a floating card out of the detail-pane art (the big portrait). On by default.
        public bool DragFromDetail { get; set; } = true;
        // Allow pulling a floating card straight out of a results-grid cell. Off by default (so a
        // grid click stays a plain select for most users).
        public bool DragFromGrid { get; set; } = false;
        // Last width (in DIPs) a floating card was scaled to, so the next dragged card matches it.
        // 0 = use the card's native size. Persisted for consistency across sessions.
        public double FloatingCardWidth { get; set; } = 0;
        // Dev shortcut: use local public/ PNGs instead of the pack/CDN WebP. Off by default.
        public bool UseLocalDevArt { get; set; } = false;
        // Custom folder for the (large ~200MB) card-art cache. Empty = default (DataDir\art-cache).
        // Lets users move the art off the system drive. Other small files stay in DataDir.
        public string ArtCacheDir { get; set; } = "";

        // Always-on HUD: read the player's current trinkets / lobby anomaly from HDT's live state and
        // show each as a floating card. Off by default (opt-in). Each slot's placement+size persists.
        public bool ShowTrinkets { get; set; } = false;
        public bool ShowAnomaly { get; set; } = false;
        public HudPlacement LesserTrinketHud { get; set; } = new HudPlacement();
        public HudPlacement GreaterTrinketHud { get; set; } = new HudPlacement();
        // Boxes 3 and 4: transforms and anomalies can leave a player holding more than the usual pair,
        // so the HUD supports up to four independently-placed trinket boxes. Added after the original
        // two → old configs deserialize the first two and default these (XML back-compat).
        public HudPlacement Trinket3Hud { get; set; } = new HudPlacement();
        public HudPlacement Trinket4Hud { get; set; } = new HudPlacement();
        public HudPlacement AnomalyHud { get; set; } = new HudPlacement();

        // Opt-in master toggle: opponents' Battlegrounds MMR / tiers during a match — read from the
        // lobby roster (HearthMirror) and matched against the hsbg.cards leaderboard (only ~8000+
        // players are listed; others show 8000↓). On for a first run (see FirstRun). Two display
        // surfaces (the leaderboard portraits and a draggable sidebar) that have no switches of their
        // own: each part says where it goes, and a surface is drawn when something is assigned to it
        // (see PanelInUse) — any combination goes (e.g. tavern tiers only, names-only sidebar, …).
        public bool ShowOpponentMmr { get; set; } = false;
        // Where each opponent's rating (+ today's delta) shows: "Off", "Portraits", "Panel", "Both" —
        // the same location axis as TavernTierMode. Empty means "a config that predates this setting",
        // resolved in Migrate() from the three LEGACY bools below.
        public string OpponentMmrMode { get; set; } = "";
        // LEGACY elements (≤0.8.0): the surface switches and the rating toggle OpponentMmrMode replaced.
        // Kept only so an existing config.xml still parses and migrates; Save() keeps them in sync so
        // rolling the DLL back still reads a sensible value. Do not read them directly.
        public bool ShowMmrLabels { get; set; } = true;
        public bool ShowMmrPanel { get; set; } = false;
        // Where the opponents' names show: "Off", "Portraits", "Panel", "Both" — the same location axis
        // as OpponentMmrMode. Up to 0.8.0 this element was a CONTENT axis ("Players" = battletags,
        // "Heroes" = hero names, "Off"), with the place implied; Migrate() turns those two values into a
        // location here plus OpponentNameType. Empty means "a config that predates the setting".
        public string OpponentNameMode { get; set; } = "";
        // What a name is: "Battletags" or "Heroes" (the hero's card name — nothing identifies the
        // player). Only read where OpponentNameMode puts a name. Empty means "a config that predates
        // the setting", resolved in Migrate(): Heroes where no name showed, as before (streamers
        // usually don't want opponent battletags on screen), else what the old value meant.
        public string OpponentNameType { get; set; } = "";
        // LEGACY element (pre-0.5): the bool this replaced. Kept only so an existing config.xml still
        // parses and migrates; Load() folds it into OpponentNameMode. Save() keeps it in sync so that
        // rolling the DLL back to an older build still reads a sensible value. Do not read it directly.
        public bool ShowOpponentNames { get; set; } = false;
        // LEGACY (≤0.8.0), see OpponentMmrMode.
        public bool ShowMmrRating { get; set; } = true;
        // Content: today's ▲/▼ rating change next to the rating, wherever the rating shows.
        public bool ShowMmrDeltas { get; set; } = true;
        // Where tavern-tier icons show: "Off", "Portraits" (right of each leaderboard portrait),
        // "Panel" (inside the separate list only), "Both" (default). Its own location axis so e.g.
        // a panel-only setup doesn't force icons onto the portraits.
        public string TavernTierMode { get; set; } = "Both";
        // Content: the ⚔ marker on the previously-fought opponent.
        public bool ShowLastOpponent { get; set; } = true;
        // Content: gray out players who are already dead.
        public bool DimDeadPlayers { get; set; } = true;
        // Where each opponent's most common minion type shows (an icon under the tavern-tier icon by
        // the portrait / a column in the panel): "Off", "Portraits", "Panel", "Both" (default) — the
        // same location axis as TavernTierMode. The type comes from HDT's last-known board snapshot
        // of that player, so it is blank until they've been fought once.
        public string OpponentTribeMode { get; set; } = "Both";
        // The player's own sizes for what is drawn by the portraits, in percent of the size the game
        // window gives them: one for the name / MMR plate, one for the tier and type icons. The
        // setters clamp.
        public const int MinPortraitSize = 70, MaxPortraitSize = 135;
        private int _portraitTextSize = 100, _portraitIconSize = 100;
        public int PortraitTextSize
        {
            get { return _portraitTextSize; }
            set { _portraitTextSize = ClampPortraitSize(value); }
        }
        public int PortraitIconSize
        {
            get { return _portraitIconSize; }
            set { _portraitIconSize = ClampPortraitSize(value); }
        }
        private static int ClampPortraitSize(int v) => v < MinPortraitSize ? MinPortraitSize : (v > MaxPortraitSize ? MaxPortraitSize : v);
        // The standings panel's placement (canvas fractions, like the trinket boxes).
        public HudPlacement MmrPanelHud { get; set; } = new HudPlacement();

        // The lobby's minion types as a small box for the whole match; WF holds its scale against a
        // 1080-high window, Px the scale it settled at.
        public bool ShowLobbyTribes { get; set; } = true;
        // How the box lays its five types out, rows x columns: "1x5" (one row), "5x1" (one column),
        // "2x3" (three and two) or "3x2" (two, two and one). Anything else reads as "1x5".
        public string LobbyTribesLayout { get; set; } = "1x5";
        // What a type is drawn as: "IconsText" (icon with its name under it), "Icons" or "Text" (the
        // name alone, in the type's own colour). Anything else reads as "IconsText".
        public string LobbyTribesStyle { get; set; } = "IconsText";
        // The season's types that are NOT in this lobby, as a second, crossed-out block.
        public bool ShowBannedTribes { get; set; }
        public HudPlacement LobbyTribesHud { get; set; } = new HudPlacement();

        // Floating boxes assemble into one when dragged together (lobby types, sidebar, turn counter).
        // PanelLinks holds the joins, "child>parent:Side" (see Ui\PanelDock.cs).
        public bool AssemblePanels { get; set; } = true;
        public System.Collections.Generic.List<string> PanelLinks { get; set; } = new System.Collections.Generic.List<string>();

        // The match's turn number in a small box, top right; XF = its RIGHT edge, WF = its scale
        // in pixels terms (settled from the window's height at the first showing, then kept).
        public bool ShowTurnCounter { get; set; } = true;
        public HudPlacement TurnCounterHud { get; set; } = new HudPlacement();

        // The session box (Ui\SessionPanel.cs): rating at its start and now, and its latest matches,
        // SessionGames of them. XF = its left edge, WF = its scale. SessionResetTicks: matches begun
        // before that moment (UTC ticks) are not of the session ("Start a new session"); 0 = never asked.
        public bool ShowSessionPanel { get; set; } = true;
        public int SessionGames { get; set; } = 5;
        public HudPlacement SessionHud { get; set; } = new HudPlacement();
        public long SessionResetTicks { get; set; }

        // Opponents' last boards: hover a leaderboard portrait to see one; a click pins it only when
        // BoardPinOnClick is on (off by default, owner 2026-11-23).
        // One JSON record per Battlegrounds match in the data folder's match-records\ (Game\MatchRecorder.cs).
        // Local only; the stats pages read them.
        public bool RecordMatches { get; set; } = true;
        // A report of each finished ranked match goes to the site's community stats, with no names in it
        // (Game\MatchUpload.cs). The Setup's page hands its answer over in setup-share.txt.
        public bool ShareMatchData { get; set; } = true;

        public bool ShowOpponentBoards { get; set; } = true;
        public bool BoardPinOnClick { get; set; }
        // XF = the board's middle, YF = its top (canvas fractions), WF = its scale against a
        // 1080-high window, Px the scale it settled at; unset = top-middle.
        public HudPlacement OpponentBoardsHud { get; set; } = new HudPlacement();

        // Location axes ("Off"/"Portraits"/"Panel"/"Both"; unknown/empty = Both) → per-surface bools.
        internal static bool OnPortraits(string mode) =>
            string.IsNullOrEmpty(mode)
            || string.Equals(mode, "Both", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "Portraits", StringComparison.OrdinalIgnoreCase);

        internal static bool InPanel(string mode) =>
            string.IsNullOrEmpty(mode)
            || string.Equals(mode, "Both", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "Panel", StringComparison.OrdinalIgnoreCase);

        private bool NameModeIs(string mode) => string.Equals(OpponentNameMode, mode, StringComparison.OrdinalIgnoreCase);

        [XmlIgnore] public bool MmrOnPortraits => OnPortraits(OpponentMmrMode);
        [XmlIgnore] public bool MmrInPanel => InPanel(OpponentMmrMode);
        [XmlIgnore] public bool NamesOnPortraits => OnPortraits(OpponentNameMode);
        [XmlIgnore] public bool NamesInPanel => InPanel(OpponentNameMode);
        /// <summary>Hero names rather than battletags, wherever a name shows.</summary>
        [XmlIgnore] public bool HeroNames => string.Equals(OpponentNameType, "Heroes", StringComparison.OrdinalIgnoreCase);
        /// <summary>What fills the sidebar's name column: "Battletags", "Heroes" or "Off".</summary>
        [XmlIgnore] public string PanelNameMode => NamesInPanel ? (HeroNames ? "Heroes" : "Battletags") : "Off";
        /// <summary>The sidebar is drawn exactly when something is assigned to it.</summary>
        [XmlIgnore] public bool PanelInUse =>
            MmrInPanel || InPanel(TavernTierMode) || InPanel(OpponentTribeMode) || NamesInPanel;

        // Opt-in: the Dark Gift list panel, shown while hovering the in-game Dark Discovery button
        // (hover detected via HearthMirror's big-card/tooltip state — no screen geometry). Gifts
        // offerable this turn glow; future ones dim; expired ones hide. Off by default.
        public bool ShowDarkGifts { get; set; } = false;
        // Panel display mode: "Both" (gift list + minion pool), "Gifts" (list only), "Minions"
        // (pool only — panel hidden entirely when no pool applies). Right-click the panel cycles;
        // also settable in the Dark Gifts settings sub-menu.
        public string DarkGiftMode { get; set; } = "Both";
        /// <summary>How many minions from the guaranteed-tribe pool the panel renders. 0 names the
        /// tribe and draws none of them — the pool is a wall of card art, and a player who only wants
        /// to know WHICH tribe is coming shouldn't have to give up half the screen for it. The setter
        /// clamps, so a hand-edited config can't put an absurd number here and no caller has to
        /// re-check.</summary>
        public const int MaxDarkGiftPool = 12;
        private int _darkGiftPoolCount = 10;
        public int DarkGiftPoolCount
        {
            get { return _darkGiftPoolCount; }
            set { _darkGiftPoolCount = value < 0 ? 0 : (value > MaxDarkGiftPool ? MaxDarkGiftPool : value); }
        }
        public HudPlacement DarkGiftHud { get; set; } = new HudPlacement();

        // The match recap (0.7.0–0.8.1) is gone: a saved <ShowMatchRecap>, <RecapAutoDismiss>,
        // <RecapHud>, <RecapSeason> or <RecapResetAt> in an older config.xml is ignored, and its
        // records in DataDir\recaps\ are left where they are.

        [XmlIgnore] public Key BrowserKeyParsed => Enum.TryParse(BrowserKey, out Key k) ? k : Key.F3;
        [XmlIgnore] public Key GoldenKeyParsed => Enum.TryParse(GoldenKey, out Key k) ? k : Key.G;
        [XmlIgnore] public Key FocusKeyParsed => Enum.TryParse(FocusKey, out Key k) ? k : Key.S;

        // Content hash (patch-independent) of the last loaded card snapshot — drives the data refresh.
        public string LastDataHash { get; set; } = "";
        // Last GitHub update check (UTC "o"); throttles the check.
        public string LastUpdateCheckUtc { get; set; } = "";
        // Highest notification id already seen (bell); larger ids are unread.
        public int PatchNoticeLastSeenId { get; set; } = 0;
        // The bell's menu: the newest patch-notes version and the toolkit update already seen there.
        public string PatchSeenVersion { get; set; } = "";
        public string UpdateSeenVersion { get; set; } = "";
        // Aggregate manifest hash of the art pack already applied; a change triggers re-sync.
        public string ArtPackHash { get; set; } = "";
        // Version the user explicitly chose "Skip" for (background checks won't re-offer it or older;
        // the manual "Check for updates" button always reports regardless).
        public string SkippedUpdateVersion { get; set; } = "";
        // Plugin version of the previous run — a bump means the user just updated, driving the
        // one-time "Updated to vN" notice (replaced the retired staging-marker mechanism).
        public string LastRunVersion { get; set; } = "";

        /// <summary>The HDT plugin's data folder in %APPDATA% (config, caches) — survives DLL redeploys.</summary>
        public static string PluginDataDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HearthstoneDeckTracker", "HsbgCardLookup");

        /// <summary>Where a host other than HDT keeps its data (the standalone exe:
        /// %APPDATA%\TantrumsBattlegroundsToolkit). Set first thing, before any other part of the
        /// toolkit runs: the art cache path is fixed the first time it is read.</summary>
        public static string DataDirOverride;

        /// <summary>Writable data folder (config, caches): the host's own, else the HDT plugin's.</summary>
        public static string DataDir => DataDirOverride ?? PluginDataDir;

        private static string Dir => DataDir;

        private static string FilePath => Path.Combine(Dir, "config.xml");

        public static PluginConfig Load()
        {
            PluginConfig config = null;
            try
            {
                if (File.Exists(FilePath))
                {
                    using (var fs = File.OpenRead(FilePath))
                    {
                        config = (PluginConfig)new XmlSerializer(typeof(PluginConfig)).Deserialize(fs);
                        config.Migrate();
                    }
                }
            }
            catch { config = null; /* fall through to defaults */ }
            if (config == null)
            {
                config = FirstRun();
                config.Migrate();
            }
            config.ApplySetupLanguage();
            config.ApplySetupShare();
            config.Welcome = TakeWelcome();
            return config;
        }

        /// <summary>True for the one start that follows a fresh install: Setup leaves setup-welcome.txt
        /// next to where config.xml will be, and the toolkit then opens its settings once.</summary>
        [XmlIgnore] public bool Welcome { get; private set; }

        private static bool TakeWelcome()
        {
            try
            {
                var file = Path.Combine(Dir, "setup-welcome.txt");
                if (!File.Exists(file)) return false;
                File.Delete(file);
                return true;
            }
            catch { return false; }
        }

        /// <summary>The Setup wizard's "Toolkit language" page hands its choice over in
        /// setup-language.txt next to config.xml ("" = follow the host, "en", or one of the site's
        /// codes): applied once and deleted. The wizard never writes config.xml itself — an absent
        /// file is how a first run is recognised, by this class and by the wizard.</summary>
        private void ApplySetupLanguage()
        {
            try
            {
                var file = Path.Combine(Dir, "setup-language.txt");
                if (!File.Exists(file)) return;
                string code = File.ReadAllText(file).Trim();
                File.Delete(file);
                if (code.Length == 0 || string.Equals(code, Locales.English, StringComparison.OrdinalIgnoreCase)
                    || Locales.ByWeb(code) != null)
                {
                    Language = code.ToLowerInvariant();
                    Save();
                }
            }
            catch { /* never throw from a plugin */ }
        }

        /// <summary>The Setup wizard's "Match data" page: setup-share.txt holds "1" or "0", applied once and
        /// deleted, like the language.</summary>
        private void ApplySetupShare()
        {
            try
            {
                var file = Path.Combine(Dir, "setup-share.txt");
                if (!File.Exists(file)) return;
                string v = File.ReadAllText(file).Trim();
                File.Delete(file);
                if (v != "0" && v != "1") return;
                ShareMatchData = v == "1";
                Save();
            }
            catch { /* never throw from a plugin */ }
        }

        /// <summary>What a PC with no config.xml starts with: opponents' tavern tiers and minion types
        /// by the portraits, their names and MMR in the standings panel, Dark Gifts on. The property
        /// initializers of settings that older versions already saved are deliberately NOT changed to
        /// this — they are also what an element missing from an older config.xml resolves to, so
        /// changing them would re-arrange an existing user's display.</summary>
        private static PluginConfig FirstRun()
        {
            return new PluginConfig
            {
                ShowOpponentMmr = true,
                OpponentMmrMode = "Panel",
                OpponentNameMode = "Panel",
                OpponentNameType = "Battletags",
                TavernTierMode = "Portraits",
                OpponentTribeMode = "Portraits",
                ShowDarkGifts = true,
            };
        }

        /// <summary>Fold retired settings into their replacements after loading. A config written by an
        /// older build has no OpponentNameMode element, so the mode comes from the bool it replaced:
        /// names on → "Players"; names off → "Heroes" rather than "Off", because hiding the battletag
        /// was only ever a way to avoid showing it, not a wish for an empty column.
        ///
        /// A config without OpponentMmrMode had surface switches, and with those gone a saved value that
        /// mentions the sidebar would draw it. So when the old panel switch was off, nothing may be left
        /// assigned to the sidebar: "Both" and an unset axis become "Portraits", "Panel" becomes "Off",
        /// and the name setting — which only the panel ever showed, bar battletags on the portrait
        /// labels — becomes "Portraits" or "Off". The display stays exactly what it was.
        ///
        /// Last, the name CONTENT values of ≤0.8.0 become a location plus OpponentNameType: "Players"
        /// put battletags in the sidebar, and by the portraits only when the MMR was there too; "Heroes"
        /// put hero names in the sidebar. A location value ("Portraits"/"Off" from a 0.8.1 build, or
        /// anything already migrated) is left alone, type included, so this runs once.</summary>
        private void Migrate()
        {
            if (string.IsNullOrEmpty(OpponentNameMode))
                OpponentNameMode = ShowOpponentNames ? "Players" : "Heroes";
            if (string.IsNullOrEmpty(OpponentMmrMode))
            {
                bool portraits = ShowMmrLabels && ShowMmrRating, panel = ShowMmrPanel && ShowMmrRating;
                OpponentMmrMode = portraits ? (panel ? "Both" : "Portraits") : (panel ? "Panel" : "Off");
                if (!ShowMmrPanel)
                {
                    TavernTierMode = PanelLessMode(TavernTierMode);
                    OpponentTribeMode = PanelLessMode(OpponentTribeMode);
                    // The type keeps what was chosen even where nothing is left to show it.
                    OpponentNameType = NameModeIs("Players") ? "Battletags" : "Heroes";
                    OpponentNameMode = ShowMmrLabels && NameModeIs("Players") ? "Portraits" : "Off";
                }
            }
            if (NameModeIs("Players"))
            {
                OpponentNameType = "Battletags";
                OpponentNameMode = MmrOnPortraits ? "Both" : "Panel";
            }
            else if (NameModeIs("Heroes"))
            {
                OpponentNameType = "Heroes";
                OpponentNameMode = "Panel";
            }
            // A location saved before the type existed (the 0.8.1 builds before it) meant battletags.
            if (string.IsNullOrEmpty(OpponentNameType))
                OpponentNameType = NameModeIs("Off") ? "Heroes" : "Battletags";
        }

        private static string PanelLessMode(string m)
        {
            if (string.IsNullOrEmpty(m) || string.Equals(m, "Both", StringComparison.OrdinalIgnoreCase)) return "Portraits";
            if (string.Equals(m, "Panel", StringComparison.OrdinalIgnoreCase)) return "Off";
            return m;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                // Keep the retired bools consistent so an older build reading this file still behaves.
                // (A rolled-back build reads OpponentNameMode as its content axis: "Panel"/"Both"/
                // "Portraits" all mean battletags to it, so hero names come back as battletags there.)
                ShowOpponentNames = !HeroNames && (NamesOnPortraits || NamesInPanel);
                ShowMmrLabels = MmrOnPortraits || NamesOnPortraits;
                ShowMmrPanel = PanelInUse;
                ShowMmrRating = MmrOnPortraits || MmrInPanel;
                using (var fs = File.Create(FilePath))
                    new XmlSerializer(typeof(PluginConfig)).Serialize(fs, this);
            }
            catch { /* never throw from a plugin */ }
        }
    }

    /// <summary>Persisted placement + size for one always-on HUD card (a trinket or the anomaly).
    /// <see cref="Set"/> = the user has moved/sized it at least once; until then a computed default
    /// position is used. Since the HUD moved onto HDT's overlay canvas (0.3.2), the authoritative
    /// placement is the CANVAS FRACTIONS <see cref="XF"/>/<see cref="YF"/> (top-left) +
    /// <see cref="WF"/> (art width), all relative to the canvas size — WF &gt; 0 marks them valid.
    /// X/Y/W are the legacy screen-DIP window placement, kept so an old config converts once
    /// (BgHud → HudCanvasCard.LegacyToFrac) on the first show with a live canvas.</summary>
    public class HudPlacement
    {
        public bool Set { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double XF { get; set; }
        public double YF { get; set; }
        public double WF { get; set; }
        // A floating box's size as settled at its first showing (a width in pixels, or a scale):
        // 0 = not settled yet. From then on the game window's size no longer changes the box.
        public double Px { get; set; }
    }
}
