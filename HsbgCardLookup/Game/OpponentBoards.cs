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
        public bool KeepText;            // no entity behind it (the Deity): the card keeps the text it is drawn with
        public List<BgCard> Gifts = new List<BgCard>();          // Dark Gifts on it, repeats included
        public List<string> Enchantments = new List<string>();   // card ids, as the game holds them
    }

    /// <summary>The last board one lobby player brought to a fight against us.</summary>
    public sealed class OpponentBoard
    {
        public int PlayerId;
        public int Turn;
        public List<BoardMinion> Minions = new List<BoardMinion>();
        public BoardMinion Deity;        // the lobby's Deity with this player's stats; null when there is none
        internal DateTime Started;
    }

    /// <summary>
    /// One lobby player's tavern tiers over the match: the turn each tier was reached on and the
    /// triples made while on it (a triple made on tier N discovers from tier N+1).
    /// </summary>
    public sealed class PlayerHistory
    {
        public int Tier;                                                    // the highest tier seen
        public readonly Dictionary<int, int> TierTurn = new Dictionary<int, int>();   // absent = not seen happening
        public readonly Dictionary<int, int> Triples = new Dictionary<int, int>();
        /// <summary>Changes whenever something a drawing shows has changed.</summary>
        public string Signature() =>
            Tier + "|" + string.Join(",", TierTurn.OrderBy(k => k.Key).Select(k => k.Key + ":" + k.Value))
                 + "|" + string.Join(",", Triples.OrderBy(k => k.Key).Select(k => k.Key + ":" + k.Value));
    }

    /// <summary>
    /// Remembers, per lobby player, the board they last fought us with.
    ///
    /// What the client holds (measured on a replay, docs/standalone/opponent-boards.md): the fight's enemy
    /// minions sit in the opposing seat, which also holds Bob's next shop. A fight minion is created with
    /// its card's base stats; its real stats, keywords, Dark Gift flag and enchantments land in the read
    /// where COPIED_FROM_ENTITY_ID appears, 1.0–1.6 s into the fight and spread over a read or two.
    ///
    /// The fight's window is the game entity's BACON_COMBAT_SETUP tag (the one HDT snapshots on): it
    /// rises to 1 when the boards start being laid out, and in all twelve fights of the recorded dumps
    /// the last copies landed no later than the read in which it dropped. So a rise opens a new board,
    /// each minion is taken in the read where its copy tag first shows, and the board closes a moment
    /// after the drop: copies that come later are summons made during the fight. The copy tag stays the
    /// test of membership, because two seconds into a fight the same seat already holds the next shop.
    /// </summary>
    public sealed class OpponentBoards
    {
        private const int CardTypeMinion = 4, ZonePlay = 1;
        // Copies still count for a read or two after the setup ends (a read is not atomic); with no setup
        // seen at all (started mid-fight), for that long after the first copy.
        private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(300);
        private const int CardTypeGame = 1, CardTypePlayer = 2, CardTypeHero = 3;
        private readonly Dictionary<int, PlayerHistory> _history = new Dictionary<int, PlayerHistory>();
        private bool _setup, _sawSetup, _open;
        private DateTime _setupEnded;
        private static readonly Regex EnchantmentSuffix = new Regex(@"e\d*$", RegexOptions.Compiled);

        private readonly CardStore _store;
        private readonly Action<string> _log;
        private bool _noSeat;
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

        /// <summary>This lobby has a Deity: Aberrations are among its minion types. (The game entity
        /// names a Deity card in every lobby, with or without them.)</summary>
        public bool LobbyHasDeity { get; private set; }
        private bool _tribesRead;
        private const int RaceAberration = 126;

        /// <summary>That lobby player's tavern tiers and triples so far; null before they were seen.</summary>
        public PlayerHistory HistoryOf(int playerId)
        {
            PlayerHistory h;
            return _history.TryGetValue(playerId, out h) ? h : null;
        }

        // Each lobby player's hero carries their tavern tier and triple count as the leaderboard knows
        // them. A tier counts from the turn it was first seen on; triples go to the tier they were made
        // on (the total minus what the lower tiers hold). A player first seen past turn 1 (the toolkit
        // started mid-match) has no turn for the tiers reached before that.
        private void Track(int pid, int tier, int triples, int turn)
        {
            PlayerHistory h;
            if (!_history.TryGetValue(pid, out h))
            {
                _history[pid] = h = new PlayerHistory { Tier = tier };
                if (turn <= 1) h.TierTurn[1] = 1;
                _log?.Invoke("[tiers] player " + pid + ": first seen at tier " + tier + ", turn " + turn);
            }
            if (tier > h.Tier)
            {
                // One line per tier-up, with what the read looked like: the trail for "a tier's turn is
                // missing or late" reports (a jump of two tiers leaves the skipped one without a turn).
                _log?.Invoke("[tiers] player " + pid + ": tier " + h.Tier + " -> " + tier + " on turn " + turn + (_setup ? " (fight being set up)" : ""));
                h.Tier = tier;
                if (turn > 0) h.TierTurn[tier] = turn;
            }
            int lower = 0, here;
            foreach (var kv in h.Triples) if (kv.Key < h.Tier) lower += kv.Value;
            h.Triples.TryGetValue(h.Tier, out here);
            if (triples - lower > here) h.Triples[h.Tier] = triples - lower;
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
                if (_reset) { _reset = false; _boards.Clear(); _seen.Clear(); _history.Clear(); _tribesRead = false; LobbyHasDeity = false; _current = null; _setup = _sawSetup = _open = false; }
                var g = HostServices.Game;
                if (g == null || !g.IsBattlegroundsMatch) return;
                int seat = g.OpponentId;
                // Nothing is read while there is no opposing seat; said once each way, since tier-ups in
                // such a stretch would be dated late.
                if (seat <= 0)
                {
                    if (!_noSeat) { _noSeat = true; _log?.Invoke("[tiers] no opposing seat on turn " + g.TurnNumber + ": not reading"); }
                    return;
                }
                if (_noSeat) { _noSeat = false; _log?.Invoke("[tiers] opposing seat " + seat + " on turn " + g.TurnNumber + ": reading again"); }

                List<BoardMinion> found = null;
                int deityCard = 0, deityAttack = 0, deityHealth = 0, turnNow = g.TurnNumber;
                Dictionary<int, int[]> heroes = null;       // PLAYER_ID → { tier, triples }
                foreach (var e in g.Entities())
                {
                    int type = e.GetTag(Tags.CARDTYPE);
                    if (type == CardTypeHero)
                    {
                        // The leaderboard's heroes are the ones with a place (themselves copies of the heroes
                        // offered at the start); a fight's copy of a hero has the tier too, but no place.
                        int pid = e.GetTag(Tags.PLAYER_ID), tier = e.GetTag(Tags.PLAYER_TECH_LEVEL);
                        if (pid > 0 && tier > 0 && e.GetTag(Tags.PLAYER_LEADERBOARD_PLACE) > 0)
                        {
                            if (heroes == null) heroes = new Dictionary<int, int[]>();
                            int[] v;
                            if (!heroes.TryGetValue(pid, out v)) heroes[pid] = v = new int[2];
                            v[0] = Math.Max(v[0], tier); v[1] = Math.Max(v[1], e.GetTag(Tags.PLAYER_TRIPLES));
                        }
                        continue;
                    }
                    if (type == CardTypePlayer)
                    {
                        // The opposing seat's Player holds the current opponent's Deity stats for the fight.
                        if (e.GetTag(Tags.PLAYER_ID) == seat)
                        { deityAttack = e.GetTag(Tags.BACON_OLD_GOD_ATTACK); deityHealth = e.GetTag(Tags.BACON_OLD_GOD_HEALTH); }
                        continue;
                    }
                    if (type == CardTypeGame)
                    {
                        deityCard = e.GetTag(Tags.BACON_GLOBAL_OLD_GOD_DBID);
                        // The game entity holds BACON_COMBAT_SETUP at 1 while the fight's boards are laid
                        // out; the read where it drops is the one in which the last copies land.
                        bool setup = e.GetTag(Tags.BACON_COMBAT_SETUP) == 1;
                        if (_setup && !setup) _setupEnded = DateTime.UtcNow;
                        if (setup && !_setup) { _sawSetup = _open = true; _current = null; }      // a new fight
                        _setup = setup;
                        continue;
                    }
                    if (e.GetTag(Tags.COPIED_FROM_ENTITY_ID) == 0 || e.GetTag(Tags.CARDTYPE) != CardTypeMinion) continue;
                    if (e.GetTag(Tags.CONTROLLER) != seat || e.GetTag(Tags.ZONE) != ZonePlay) continue;
                    if (!_seen.Add(e.Id)) continue;
                    (found ?? (found = new List<BoardMinion>())).Add(Read(g, e));
                }
                if (!_tribesRead && deityCard > 0)
                {
                    var tribes = HostServices.Memory?.LobbyTribes();
                    if (tribes != null && tribes.Count > 0) { _tribesRead = true; LobbyHasDeity = tribes.Contains(RaceAberration); }
                }
                if (heroes != null)
                    foreach (var kv in heroes) Track(kv.Key, kv.Value[0], kv.Value[1], turnNow);
                var now = DateTime.UtcNow;
                if (_open && !_setup && now - _setupEnded > Grace) _open = false;
                if (_open) SetDeity(deityCard, deityAttack, deityHealth);
                if (found == null) return;

                int turn = g.TurnNumber;
                if (_sawSetup && !_open) return;                 // a summon made during the fight
                if (_current == null || (!_sawSetup && _current.Turn != turn))
                {
                    int pid = NextOpponent(g);
                    if (pid <= 0) { _current = null; return; }
                    _current = new OpponentBoard { PlayerId = pid, Turn = turn, Started = now };
                    _boards[pid] = _current;
                    _log?.Invoke("[boards] turn " + turn + ": fight against player " + pid);
                }
                else if (!_sawSetup && now - _current.Started > Grace) return;

                SetDeity(deityCard, deityAttack, deityHealth);
                _current.Minions.AddRange(found);
                _current.Minions.Sort((a, b) => a.Position.CompareTo(b.Position));
                _log?.Invoke("[boards] turn " + turn + ": " + string.Join(", ", _current.Minions.Select(m =>
                    m.CardId + " " + m.Attack + "/" + m.Health + (m.Gifts.Count > 0 ? " +" + m.Gifts.Count + " gift" : ""))));
            }
            catch (Exception ex) { _log?.Invoke("[boards] " + ex.GetType().Name + ": " + ex.Message); }
        }

        // The Deity as the fight's opposing seat shows it: the lobby's card with that player's numbers.
        private void SetDeity(int cardNumber, int attack, int health)
        {
            if (_current == null || cardNumber <= 0 || attack <= 0 || health <= 0) return;
            if (_current.Deity != null && _current.Deity.Attack == attack && _current.Deity.Health == health) return;
            BgCard card;
            if (!_store.ById.TryGetValue(cardNumber, out card) || card == null) return;
            _current.Deity = new BoardMinion
            {
                CardId = card.ExternalId, TextId = card.ExternalId, Card = card,
                Attack = attack, Health = health, Legendary = true, Tier = card.Tier ?? 0, KeepText = true
            };
            _log?.Invoke("[boards] deity " + card.ExternalId + " " + attack + "/" + health);
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
