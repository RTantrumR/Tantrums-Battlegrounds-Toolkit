using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HsbgCardLookup.Data;
using HsbgCardLookup.Host;
using HsbgCardLookup.Search;

namespace HsbgCardLookup.Game
{
    /// <summary>One minion of an opponent's board, as it stood when the fight began.</summary>
    public sealed class BoardMinion
    {
        public string CardId;            // the game's own id (a golden one for a golden minion)
        public BgCard Card;              // our record of it (the base card for a golden); null when unknown
        public int Attack, Health, Position;
        public bool Golden, Taunt, DivineShield, Reborn, Windfury, Stealth, Venomous, Poisonous, Deathrattle, Rally, Trigger;
        public bool Legendary;           // ELITE: the frame carries the dragon
        public int Tier;                 // tavern tier, 0 when unknown
        public int[] Script = new int[6]; // TAG_SCRIPT_DATA_NUM_1..6: the numbers a card's text shows
        public int AltText;              // USE_ALTERNATE_CARD_TEXT: which variant of the text
        public string TextId;            // the entity's own card id (golden ones have their own text record)
        public List<BgCard> Gifts = new List<BgCard>();          // Dark Gifts on it, repeats included
        public List<string> Enchantments = new List<string>();   // card ids, as the game holds them
    }

    /// <summary>The last board one lobby player brought to a fight against us.</summary>
    public sealed class OpponentBoard
    {
        public int PlayerId;
        public int Turn;
        public List<BoardMinion> Minions = new List<BoardMinion>();
        internal DateTime Started;
    }

    /// <summary>
    /// Remembers, per lobby player, the board they last fought us with.
    ///
    /// What the client holds (measured on a replay, docs/standalone/opponent-boards.md): the fight's enemy
    /// minions sit in the opposing seat, which also holds Bob's next shop. A fight minion is created with
    /// its card's base stats; its real stats, keywords, Dark Gift flag and enchantments land in the read
    /// where COPIED_FROM_ENTITY_ID appears, 1.0–1.6 s into the fight and spread over a read or two. So each
    /// minion is taken in the read where that tag first shows, and only for a short window after the
    /// fight's first one: later copies are summons, not the starting board.
    /// </summary>
    public sealed class OpponentBoards
    {
        private const int CardTypeMinion = 4, ZonePlay = 1;
        // Copies still count for one more read after the setup ends (a read is not atomic); with no setup
        // seen (started mid-fight), for that long after the first one.
        private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(600);
        private const int CardTypeGame = 1;
        private bool _setup;
        private DateTime _setupEnded;
        private static readonly Regex EnchantmentSuffix = new Regex(@"e\d*$", RegexOptions.Compiled);

        private readonly CardStore _store;
        private readonly Action<string> _log;
        private readonly Dictionary<int, OpponentBoard> _boards = new Dictionary<int, OpponentBoard>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private Dictionary<string, BgCard> _gifts;
        private OpponentBoard _current;
        private volatile bool _reset;
        private static OpponentBoards _instance;
        private static bool _hooked;

        public OpponentBoards(CardStore store, Action<string> log)
        {
            _store = store; _log = log;
            _instance = this;
            if (_hooked) return;
            _hooked = true;
            try { HostServices.Game.GameStarted += () => { var i = _instance; if (i != null) i._reset = true; }; } catch { }
        }

        /// <summary>The board this lobby player last fought us with; null when they haven't yet.</summary>
        public OpponentBoard For(int playerId)
        {
            OpponentBoard b;
            return _boards.TryGetValue(playerId, out b) && b.Minions.Count > 0 ? b : null;
        }

        /// <summary>The lobby player we fight next (or are fighting): NEXT_OPPONENT_PLAYER_ID on our Player.</summary>
        public static int NextOpponent(IGameState g)
        {
            try
            {
                var me = g?.PlayerEntity;
                return me != null && me.HasTag(Tags.NEXT_OPPONENT_PLAYER_ID) ? me.GetTag(Tags.NEXT_OPPONENT_PLAYER_ID) : 0;
            }
            catch { return 0; }
        }

        /// <summary>About every 100 ms, on the UI thread.</summary>
        public void Poll()
        {
            try
            {
                if (_reset) { _reset = false; _boards.Clear(); _seen.Clear(); _current = null; }
                var g = HostServices.Game;
                if (g == null || !g.IsBattlegroundsMatch) return;
                int seat = g.OpponentId;
                if (seat <= 0) return;

                List<BoardMinion> found = null;
                foreach (var e in g.Entities())
                {
                    if (e.GetTag(Tags.CARDTYPE) == CardTypeGame)
                    {
                        // The game entity holds BACON_COMBAT_SETUP at 1 while the fight's boards are laid
                        // out; the read where it drops is the one in which the last copies land.
                        bool setup = e.GetTag(Tags.BACON_COMBAT_SETUP) == 1;
                        if (_setup && !setup) _setupEnded = DateTime.UtcNow;
                        _setup = setup;
                        continue;
                    }
                    if (e.GetTag(Tags.COPIED_FROM_ENTITY_ID) == 0 || e.GetTag(Tags.CARDTYPE) != CardTypeMinion) continue;
                    if (e.GetTag(Tags.CONTROLLER) != seat || e.GetTag(Tags.ZONE) != ZonePlay) continue;
                    if (!_seen.Add(e.Id)) continue;
                    (found ?? (found = new List<BoardMinion>())).Add(Read(g, e));
                }
                if (found == null) return;

                int turn = g.TurnNumber;
                var now = DateTime.UtcNow;
                if (_current == null || _current.Turn != turn)
                {
                    int pid = NextOpponent(g);
                    if (pid <= 0) { _current = null; return; }
                    _current = new OpponentBoard { PlayerId = pid, Turn = turn, Started = now };
                    _boards[pid] = _current;
                    _log?.Invoke("[boards] turn " + turn + ": fight against player " + pid);
                }
                else if (!_setup && now - (_setupEnded > _current.Started ? _setupEnded : _current.Started) > Grace) return;   // a summon made during the fight

                _current.Minions.AddRange(found);
                _current.Minions.Sort((a, b) => a.Position.CompareTo(b.Position));
                _log?.Invoke("[boards] turn " + turn + ": " + string.Join(", ", _current.Minions.Select(m =>
                    m.CardId + " " + m.Attack + "/" + m.Health + (m.Gifts.Count > 0 ? " +" + m.Gifts.Count + " gift" : ""))));
            }
            catch (Exception ex) { _log?.Invoke("[boards] " + ex.GetType().Name + ": " + ex.Message); }
        }

        private BoardMinion Read(IGameState g, IGameEntity e)
        {
            var m = new BoardMinion
            {
                CardId = e.CardId,
                Attack = e.GetTag(Tags.ATK),
                Health = e.Health,
                Position = e.GetTag(Tags.ZONE_POSITION),
                Golden = e.GetTag(Tags.PREMIUM) == 1,
                Taunt = e.GetTag(Tags.TAUNT) > 0,
                DivineShield = e.GetTag(Tags.DIVINE_SHIELD) > 0,
                Reborn = e.GetTag(Tags.REBORN) > 0,
                Windfury = e.GetTag(Tags.WINDFURY) > 0,
                Stealth = e.GetTag(Tags.STEALTH) > 0,
                Venomous = e.GetTag(Tags.VENOMOUS) > 0 || e.GetTag(Tags.POISONOUS) > 0,
                Poisonous = e.GetTag(Tags.POISONOUS) > 0,
                Deathrattle = e.GetTag(Tags.DEATHRATTLE) > 0,
                Rally = e.GetTag(Tags.RALLY) > 0,
                Trigger = e.GetTag(Tags.TRIGGER_VISUAL) > 0,
                Legendary = e.GetTag(Tags.ELITE) > 0,
                Tier = e.GetTag(Tags.TECH_LEVEL),
                AltText = e.GetTag(Tags.USE_ALTERNATE_CARD_TEXT),
                TextId = e.CardId,
            };
            int[] scriptTags = { Tags.TAG_SCRIPT_DATA_NUM_1, Tags.TAG_SCRIPT_DATA_NUM_2, Tags.TAG_SCRIPT_DATA_NUM_3, Tags.TAG_SCRIPT_DATA_NUM_4, Tags.TAG_SCRIPT_DATA_NUM_5, Tags.TAG_SCRIPT_DATA_NUM_6 };
            for (int i = 0; i < scriptTags.Length; i++) m.Script[i] = e.GetTag(scriptTags[i]);
            try { HostServices.Memory?.CardText(e.CardId); } catch { }      // asked now, so it is there when the card is opened
            m.Card = Resolve(e);
            foreach (string id in g.EnchantmentsOf(e.Id)) m.Enchantments.Add(id);
            if (e.GetTag(Tags.HAS_DARK_GIFT) > 0) m.Gifts = GiftsOf(m.Enchantments);
            return m;
        }

        // Golden ids have no record of their own: "<id>_G" is the base id plus a suffix, an older golden
        // (TB_BaconUps_…) names its base card by number.
        private BgCard Resolve(IGameEntity e)
        {
            string id = e.CardId;
            var c = _store.Lookup(id);
            if (c == null && id != null && id.EndsWith("_G", StringComparison.Ordinal)) c = _store.Lookup(id.Substring(0, id.Length - 2));
            if (c == null)
            {
                int baseId = e.GetTag(Tags.BACON_TRIPLED_BASE_MINION_ID);
                if (baseId > 0) _store.ById.TryGetValue(baseId, out c);
            }
            return c;
        }

        // An opponent's gift is named by its enchantment: the enchantment's id minus its "e<digits>"
        // suffix is the gift card. One gift can leave several enchantments of its own, so it counts as
        // often as its most frequent one.
        private List<BgCard> GiftsOf(List<string> enchantments)
        {
            if (_gifts == null)
                _gifts = _store.All.Where(c => c.Categories != null && c.Categories.Contains("darkgift") && !string.IsNullOrEmpty(c.ExternalId))
                    .GroupBy(c => c.ExternalId).ToDictionary(x => x.Key, x => x.First());
            var perGift = new Dictionary<BgCard, Dictionary<string, int>>();
            foreach (string id in enchantments)
            {
                BgCard gift;
                if (!_gifts.TryGetValue(EnchantmentSuffix.Replace(id, ""), out gift)) continue;
                Dictionary<string, int> ids;
                if (!perGift.TryGetValue(gift, out ids)) perGift[gift] = ids = new Dictionary<string, int>();
                int n; ids.TryGetValue(id, out n); ids[id] = n + 1;
            }
            var list = new List<BgCard>();
            foreach (var kv in perGift)
                for (int i = 0, n = kv.Value.Values.Max(); i < n; i++) list.Add(kv.Key);
            return list;
        }
    }
}
