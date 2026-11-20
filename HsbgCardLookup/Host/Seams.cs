using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace HsbgCardLookup.Host
{
    // The four things the toolkit takes from whatever hosts it. Features talk only to these; each host
    // (today: HDT, under Host\Hdt\) implements them. Shaped after the calls the features actually make,
    // nothing more. See docs/standalone/standalone-plan.md.

    /// <summary>One game entity as the host tracks it.</summary>
    public interface IGameEntity
    {
        int Id { get; }
        string CardId { get; }
        /// <summary>The host's own name for the card, used only as a last-resort label.</summary>
        string CardName { get; }
        bool HasTag(int tag);
        int GetTag(int tag);
        bool IsInPlay { get; }
        /// <summary>HEALTH minus DAMAGE.</summary>
        int Health { get; }
    }

    /// <summary>The current match. Getters may throw while the host swaps state; callers guard.</summary>
    public interface IGameState
    {
        bool IsBattlegroundsMatch { get; }
        bool IsBattlegroundsDuosMatch { get; }
        bool IsBattlegroundsCombatPhase { get; }
        int TurnNumber { get; }
        /// <summary>"US", "EU", "AP", "CN", or null when unknown.</summary>
        string Region { get; }
        /// <summary>A snapshot of every entity; empty (never null) when there is no game.</summary>
        IList<IGameEntity> Entities();
        /// <summary>Null when absent.</summary>
        IGameEntity Entity(int id);
        IGameEntity PlayerEntity { get; }
        /// <summary>The local player's PLAYER_ID, 0 when unknown.</summary>
        int PlayerId { get; }
        /// <summary>The current opponent's PLAYER_ID, 0 when unknown.</summary>
        int OpponentId { get; }
        IList<IGameEntity> PlayerMinions();
        IList<IGameEntity> PlayerTrinkets();
        /// <summary>Card ids of the enchantments attached to a minion of the fight's opposing side;
        /// empty (never null) when there are none or the host doesn't track them.</summary>
        IList<string> EnchantmentsOf(int entityId);
        /// <summary>Raised for a new match and for a mid-match reconnect alike.</summary>
        event Action GameStarted;
        event Action GameEnded;
    }

    public sealed class LobbyPlayer
    {
        public string Name;          // display name, no #number
        public string HeroCardId;
    }

    /// <summary>The per-tile minion-type counts walk, with its own per-match log throttles.</summary>
    public interface IRaceReader
    {
        void ResetMatch();
        /// <summary>Hero entity id → (race id → count); null when the walk failed.</summary>
        Dictionary<int, Dictionary<int, int>> Read();
    }

    /// <summary>What only the Hearthstone client's memory holds.</summary>
    public interface IClientMemory
    {
        /// <summary>Null when unavailable.</summary>
        IList<LobbyPlayer> LobbyPlayers();
        /// <summary>This lobby's TAG_RACE ids; null when unavailable.</summary>
        IList<int> LobbyTribes();
        IRaceReader CreateRaceReader(Action<string> log);
        /// <summary>Entity id of the hero whose leaderboard tile the game's own mouse is on; 0 for none,
        /// -1 when the host can't tell.</summary>
        int HoveredLeaderboardHero();
        /// <summary>The client's own record of a card's text; null when the host has none (yet: a first
        /// ask may be answered a moment later).</summary>
        CardTextRecord CardText(string cardId);
    }

    /// <summary>A card's text template as the client holds it, and which of its text builders fills it.</summary>
    public sealed class CardTextRecord
    {
        public int Builder;      // Assets.Card.CardTextBuilderType
        public string Text;      // raw: "[x]", <b>, line breaks, {0} / @ placeholders; the client's own language
        /// <summary>The same text per language, in the order of <see cref="Languages"/>; null when the
        /// client keeps only its own (<see cref="Text"/>). The client was seen holding all of them.</summary>
        public string[] Texts;

        /// <summary>The client's Localization.SupportedLocales, which orders a record's strings.</summary>
        public static readonly string[] Languages = { "enUS", "deDE", "esES", "esMX", "frFR", "itIT", "jaJP", "koKR", "plPL", "ptBR", "ruRU", "thTH", "zhCN", "zhTW" };

        /// <summary>The text in one language ("jaJP"); null when the record doesn't have that language.</summary>
        public string In(string language)
        {
            int i = Array.IndexOf(Languages, language);
            if (Texts != null) return i >= 0 && i < Texts.Length && !string.IsNullOrEmpty(Texts[i]) ? Texts[i] : null;
            return language == "enUS" || language == null ? Text : null;     // one language only: trusted to be English when asked for English
        }
    }

    /// <summary>The surface in-match features draw on, kept over the Hearthstone window.</summary>
    public interface IOverlaySurface
    {
        /// <summary>Null when there is no surface (yet).</summary>
        Canvas Canvas { get; }
        Dispatcher Dispatcher { get; }
        /// <summary>Lets <paramref name="element"/> take real mouse input while the rest stays click-through.</summary>
        void SetHitTestVisible(UIElement element);
        /// <summary>The game's own picture right now (its client area, our overlay not in it), frozen;
        /// null when the host can't take one.</summary>
        System.Windows.Media.Imaging.BitmapSource CaptureGame();
        /// <summary>The Hearthstone window in device-independent pixels; null when there is none.</summary>
        Rect? HearthstoneRect();
    }

    /// <summary>Process-level services of the host.</summary>
    public interface IHostShell
    {
        bool IsHearthstoneRunning { get; }
        void BringHearthstoneToForeground();
        /// <summary>The host's own UI language as a locale code, or null — what <c>Language = ""</c> follows.</summary>
        string HostLanguage();
        /// <summary>Whether the toolkit starts with Windows; null when the host has no such switch (HDT
        /// starts the plugin itself, and has its own "start with Windows").</summary>
        bool? StartWithWindows { get; set; }
        /// <summary>The running game's Data\Win folder (its asset bundles); null when unknown.</summary>
        string HearthstoneDataDirectory();
    }

    /// <summary>The services of the current host, set once by it at start-up.</summary>
    public static class HostServices
    {
        public static IGameState Game { get; private set; }
        public static IClientMemory Memory { get; private set; }
        public static IOverlaySurface Overlay { get; private set; }
        public static IHostShell Shell { get; private set; }

        public static void Use(IGameState game, IClientMemory memory, IOverlaySurface overlay, IHostShell shell)
        {
            Game = game; Memory = memory; Overlay = overlay; Shell = shell;
        }
    }

    /// <summary>GAME_TAG ids the features read — the game protocol's own numbers.</summary>
    public static class Tags
    {
        public const int TAG_SCRIPT_DATA_NUM_1 = 2;
        public const int TAG_SCRIPT_DATA_NUM_2 = 3;
        public const int PREMIUM = 12;
        public const int DAMAGE = 44;
        public const int ATK = 47;
        public const int ZONE = 49;
        public const int CONTROLLER = 50;
        public const int WINDFURY = 189;
        public const int TAUNT = 190;
        public const int STEALTH = 191;
        public const int DIVINE_SHIELD = 194;
        public const int CARDTYPE = 202;
        public const int TRIGGER_VISUAL = 32;
        public const int ELITE = 114;
        public const int DEATHRATTLE = 217;
        public const int TECH_LEVEL = 1440;
        public const int ZONE_POSITION = 263;
        public const int RALLY = 4204;
        public const int POISONOUS = 363;
        public const int REBORN = 1085;
        public const int BACON_TRIPLED_BASE_MINION_ID = 1471;
        public const int COPIED_FROM_ENTITY_ID = 1565;
        public const int BACON_COMBAT_SETUP = 3533;   // unnamed in HearthDb: 1 on the game entity while a fight's boards are set up
        public const int VENOMOUS = 2853;
        public const int HAS_DARK_GIFT = 3880;
        public const int PLAYER_ID = 30;
        public const int EXHAUSTED = 43;
        public const int HEALTH = 45;
        public const int NEXT_OPPONENT_PLAYER_ID = 1360;
        public const int PLAYER_LEADERBOARD_PLACE = 1373;
        public const int PLAYER_TECH_LEVEL = 1377;
        public const int PLAYER_TRIPLES = 1447;
        // The lobby's Deity (a card's number, on the game entity) and one player's Deity stats (on
        // the Player entity; the opposing seat holds the current opponent's for the fight).
        public const int BACON_GLOBAL_OLD_GOD_DBID = 4902;
        public const int BACON_OLD_GOD_ATTACK = 4914;
        public const int BACON_OLD_GOD_HEALTH = 4915;
        public const int TAG_SCRIPT_DATA_NUM_3 = 2889;
        public const int TAG_SCRIPT_DATA_NUM_4 = 2919;
        public const int TAG_SCRIPT_DATA_NUM_5 = 2920;
        public const int TAG_SCRIPT_DATA_NUM_6 = 2921;
        public const int USE_ALTERNATE_CARD_TEXT = 955;
        public const int BACON_DUO_TEAMMATE_PLAYER_ID = 2939;
        public const int BACON_DUO_PLAYER_FIGHTS_FIRST_NEXT_COMBAT = 2975;
        public const int NEXT_OPPONENT_TEAMMATE_PLAYER_ID = 2988;
        public const int LOCK_VISUAL = 4414;
    }
}
