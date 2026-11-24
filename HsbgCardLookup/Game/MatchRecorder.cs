using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HsbgCardLookup.Host;
using Newtonsoft.Json;

namespace HsbgCardLookup.Game
{
    /// <summary>A minion as it stood on a board.</summary>
    public sealed class RecordedMinion
    {
        [JsonProperty("card")] public string Card;
        [JsonProperty("atk")] public int Attack;
        [JsonProperty("hp")] public int Health;
        [JsonProperty("golden", DefaultValueHandling = DefaultValueHandling.Ignore)] public bool Golden;
        /// <summary>One letter each: T aunt, D ivine shield, W indfury, R eborn, S tealth, P oisonous, V enomous;
        /// lower case for what only changes the look: e lite (legendary), d eathrattle, r ally, t rigger.</summary>
        [JsonProperty("kw", DefaultValueHandling = DefaultValueHandling.Ignore)] public string Keywords;
        [JsonProperty("tier", DefaultValueHandling = DefaultValueHandling.Ignore)] public int Tier;
        [JsonProperty("gift", DefaultValueHandling = DefaultValueHandling.Ignore)] public bool DarkGift;
        /// <summary>The enchantments on it, card id to how many: what names its Dark Gifts (a gift's
        /// enchantment is the gift's id plus "e"), and every other buff it carried.</summary>
        [JsonProperty("ench", NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, int> Enchantments;
    }

    /// <summary>Something of ours in play that is not a minion: a hero power, trinket, quest, reward, buddy
    /// meter, secret, whatever a season adds. Type is the game's CARDTYPE number.</summary>
    public sealed class RecordedCard
    {
        [JsonProperty("card")] public string Card;
        [JsonProperty("type")] public int Type;
    }

    /// <summary>Which of the game's systems the lobby ran with, from the game entity's own switches.</summary>
    public sealed class RecordedMechanics
    {
        [JsonProperty("buddies", DefaultValueHandling = DefaultValueHandling.Ignore)] public bool Buddies;
        [JsonProperty("quests", DefaultValueHandling = DefaultValueHandling.Ignore)] public bool Quests;
        [JsonProperty("trinkets", DefaultValueHandling = DefaultValueHandling.Ignore)] public bool Trinkets;
        [JsonProperty("darkGifts", DefaultValueHandling = DefaultValueHandling.Ignore)] public bool DarkGifts;
        /// <summary>The timewarped tavern (the client's "alt tavern").</summary>
        [JsonProperty("timewarp", DefaultValueHandling = DefaultValueHandling.Ignore)] public bool Timewarp;
        /// <summary>Card numbers (dbf ids): the lobby's anomalies, and its Deity.</summary>
        [JsonProperty("anomalies")] public List<int> Anomalies = new List<int>();
        [JsonProperty("deity", DefaultValueHandling = DefaultValueHandling.Ignore)] public int Deity;
        [JsonProperty("damageCap", DefaultValueHandling = DefaultValueHandling.Ignore)] public int DamageCap;
    }

    /// <summary>One round: the board sent into the fight and how the fight went.</summary>
    public sealed class RecordedTurn
    {
        [JsonProperty("n")] public int Number;
        [JsonProperty("tier")] public int Tier;
        /// <summary>Health with armor, as the round's shop ended.</summary>
        [JsonProperty("health")] public int Health;
        [JsonProperty("triples")] public int Triples;
        [JsonProperty("board")] public List<RecordedMinion> Board = new List<RecordedMinion>();
        /// <summary>The opponent's PLAYER_ID and hero (see <see cref="MatchRecord.Players"/>).</summary>
        [JsonProperty("opp")] public int Opponent;
        [JsonProperty("oppHero")] public string OpponentHero;
        /// <summary>"W", "L" or "T"; null while the fight isn't over.</summary>
        [JsonProperty("result")] public string Result;
        [JsonProperty("dealt")] public int Dealt;
        [JsonProperty("taken")] public int Taken;
        /// <summary>Every player's health (with armor) as the round's shop ended, by PLAYER_ID.</summary>
        [JsonProperty("healths")] public Dictionary<int, int> Healths = new Dictionary<int, int>();
        [JsonProperty("tiers")] public Dictionary<int, int> Tiers = new Dictionary<int, int>();
        /// <summary>Ours in play besides the minions, and the hand's cards, as the shop ended.</summary>
        [JsonProperty("cards")] public List<RecordedCard> Cards = new List<RecordedCard>();
        [JsonProperty("hand")] public List<string> Hand = new List<string>();
        /// <summary>Our Player entity's tags that changed since the round before (the first round: all of them).
        /// Raw game numbers: gold, buddy meter, timewarp visits, rerolls, whatever the season counts there.</summary>
        [JsonProperty("playerTags")] public Dictionary<int, int> PlayerTags = new Dictionary<int, int>();
    }

    public sealed class RecordedPlayer
    {
        [JsonProperty("id")] public int PlayerId;
        /// <summary>The hero as last seen; a hero that transforms in the match ends as its other form.</summary>
        [JsonProperty("hero")] public string Hero;
        /// <summary>The hero as first seen (the pick): the same in every recording of the lobby, so the lobby's
        /// eight picks plus its start time tell one lobby from another across players.</summary>
        [JsonProperty("picked")] public string Picked;
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)] public string Name;
        [JsonProperty("place")] public int Place;
        [JsonProperty("me", DefaultValueHandling = DefaultValueHandling.Ignore)] public bool Me;
        /// <summary>The leaderboard hero's tags as last seen (trinkets, quest rewards, buddy and more live there).</summary>
        [JsonProperty("tags")] public Dictionary<int, int> Tags = new Dictionary<int, int>();
        /// <summary>The minion types on the player's board as their leaderboard tile last showed them
        /// (TAG_RACE id to count: the game's own "4 Beasts"); an empty reading never replaces a filled one.</summary>
        [JsonProperty("types", NullValueHandling = NullValueHandling.Ignore)] public Dictionary<int, int> Types;
    }

    /// <summary>One match as the recorder saw it: <c>match-records\&lt;yyyyMMdd_HHmmss&gt;.json</c> in the data folder.</summary>
    public sealed class MatchRecord
    {
        public const int CurrentVersion = 2;
        [JsonProperty("v")] public int Version = CurrentVersion;
        [JsonProperty("id")] public string Id;
        [JsonProperty("startUtc")] public DateTime StartUtc;
        [JsonProperty("endUtc")] public DateTime? EndUtc;
        /// <summary>False while the match runs (the file is rewritten every round) and for one cut short.</summary>
        [JsonProperty("finished")] public bool Finished;
        /// <summary>How many times the match was left and rejoined (a lost connection, a restarted game); the
        /// rounds played meanwhile are missing from <see cref="Turns"/>.</summary>
        [JsonProperty("reconnects", DefaultValueHandling = DefaultValueHandling.Ignore)] public int Reconnects;
        [JsonProperty("build", NullValueHandling = NullValueHandling.Ignore)] public string Build;
        [JsonProperty("region", NullValueHandling = NullValueHandling.Ignore)] public string Region;
        [JsonProperty("duos", DefaultValueHandling = DefaultValueHandling.Ignore)] public bool Duos;
        /// <summary>The client's game type number (ranked, custom lobby, ...); 0 = the host didn't say.</summary>
        [JsonProperty("gameType", DefaultValueHandling = DefaultValueHandling.Ignore)] public int GameType;
        /// <summary>The numbered season (the client's battlegrounds_reward_track_season_N) and the rating
        /// period (battlegrounds_ranked_season_N) the match began in; 0 = unknown.</summary>
        [JsonProperty("season", DefaultValueHandling = DefaultValueHandling.Ignore)] public int Season;
        [JsonProperty("ratingSeason", DefaultValueHandling = DefaultValueHandling.Ignore)] public int RatingSeason;
        [JsonProperty("hero")] public string Hero;
        /// <summary>The heroes we were offered, one list per offer slot (left to right); a slot rerolled shows
        /// each hero it held, in order, so its last one was on offer at the pick.</summary>
        [JsonProperty("heroOffers")] public List<List<string>> HeroOffers = new List<List<string>>();
        [JsonProperty("heroPowers")] public List<string> HeroPowers = new List<string>();
        [JsonProperty("trinkets")] public List<string> Trinkets = new List<string>();
        [JsonProperty("place")] public int Place;
        [JsonProperty("rating", NullValueHandling = NullValueHandling.Ignore)] public int? RatingAfter;
        [JsonProperty("ratingChange", NullValueHandling = NullValueHandling.Ignore)] public int? RatingChange;
        /// <summary>TAG_RACE ids in the lobby.</summary>
        [JsonProperty("types")] public List<int> Types = new List<int>();
        [JsonProperty("anomaly", DefaultValueHandling = DefaultValueHandling.Ignore)] public int AnomalyDbId;
        [JsonProperty("mechanics")] public RecordedMechanics Mechanics = new RecordedMechanics();
        /// <summary>The game entity's tags as the match began and as it ended, raw: the lobby's rules. Kept whole
        /// so a mechanic nobody named yet can still be read out of old records.</summary>
        [JsonProperty("gameTagsStart")] public Dictionary<int, int> GameTagsStart = new Dictionary<int, int>();
        [JsonProperty("gameTags")] public Dictionary<int, int> GameTags = new Dictionary<int, int>();
        [JsonProperty("players")] public List<RecordedPlayer> Players = new List<RecordedPlayer>();
        [JsonProperty("turns")] public List<RecordedTurn> Turns = new List<RecordedTurn>();
        /// <summary>The board as the match ended, with what the last fight left on it for good (the game's own
        /// end screen shows this one); a round's <see cref="RecordedTurn.Board"/> is the board sent INTO its fight.</summary>
        [JsonProperty("finalBoard")] public List<RecordedMinion> FinalBoard = new List<RecordedMinion>();
    }

    /// <summary>
    /// Writes one JSON record per Battlegrounds match, from what the host's game state shows: the board and
    /// everyone's health as each shop ends, and what the fight cost. Nothing leaves the PC.
    ///
    /// A round is closed when the next shop turn begins (or the game ends), not on the combat flag: the
    /// combat can be shorter than a poll. The board of round N is the last one seen in N's shop; the fight's
    /// damage is the two heroes' health across that boundary.
    /// </summary>
    public sealed class MatchRecorder
    {
        /// <summary>Under the data folder. Not "matches": that one holds the removed match recap's files
        /// (v0.7-v0.8.0, another shape), which players who had it still have.</summary>
        public const string FolderName = "match-records";

        private const int TagHeroEntity = 27, TagArmor = 292, TagAnomalyDbId = 2897;
        private const int CardTypeGame = 1, CardTypePlayer = 2, CardTypeHero = 3, CardTypeMinion = 4, CardTypeEnchantment = 6, CardTypeHeroPower = 10;
        private const int ZoneHand = 3;
        private const int TagState = 204, StateComplete = 3;   // the game entity's STATE: 3 once the match is over for us
        private const int ResumeHours = 3;                     // an unfinished record older than this is never continued
        private const int TagBuddyEnabled = 2518, TagQuestsActive = 2468, TagTrinketsActive = 3740, TagDarkGiftsActive = 4845,
            TagAltTavernActive = 4519, TagAnomaly1 = 3182, TagAnomaly2 = 3183, TagDamageCap = 2089, TagDamageCapEnabled = 3403;
        private const int RaceMs = 2000;                        // the tiles' minion types, read at most this often
        private Dictionary<int, int> _playerNow, _playerBase;   // our Player's tags: at the last shop read, and as the round before ended

        private readonly IGameState _game;
        private readonly IClientMemory _memory;
        private readonly string _folder;
        private readonly Action<string> _log;

        /// <summary>The clock (the replay feed supplies the recording's own).</summary>
        public Func<DateTime> UtcNow = () => DateTime.UtcNow;
        /// <summary>The game build to stamp records with (asked as a match begins).</summary>
        public Func<string> BuildSource;
        /// <summary>Dev: record watched games (replays) as if they were played.</summary>
        public bool RecordSpectated;
        private int[] _ratingSeen;         // what the game last reported, so one report is taken once
        /// <summary>Match start → { season, rating season }; null when the host has no season calendar.</summary>
        public Func<DateTime, int[]> SeasonOf;
        /// <summary>A match was closed and written, and again when its rating arrives after that.</summary>
        public Action<MatchRecord> Saved;
        private const int KeepRecords = 200;   // the newest stay; a record is ~26 KB

        private MatchRecord _rec;          // the match in progress
        private RecordedTurn _turn;        // its round in progress (the shop is open or the fight is on)
        private string _path;
        private bool _started, _ended;
        private bool _armed;               // a game began and has not ended: a record may start
        private bool _rejoinCheck;         // a game began while a record was open: the same match, or a new one?
        private bool _over;                // the game itself said the match is over (not just gone from memory)
        private MatchRecord _last;         // the match just written, for a rating that arrives after it
        private string _lastPath;
        private readonly IRaceReader _races;   // the leaderboard tiles' minion types, as the portraits show them
        private DateTime _lastRaceRead;
        // The hero choice: offer entity id -> (slot position, every hero it showed). Gathered before the match's
        // record opens (the pick comes first) and handed to it when it does.
        private readonly Dictionary<int, KeyValuePair<int, List<string>>> _offers = new Dictionary<int, KeyValuePair<int, List<string>>>();

        public MatchRecorder(IGameState game, IClientMemory memory, string folder, Action<string> log)
        {
            _game = game; _memory = memory; _folder = folder; _log = log;
            try { _races = memory?.CreateRaceReader(_ => { }); } catch { }
            game.GameStarted += () => _started = true;
            game.GameEnded += () => _ended = true;
        }

        /// <summary>About every 100 ms, on the thread the game state is published on.</summary>
        public void Poll()
        {
            try
            {
                if (_started)
                {
                    _started = false; _armed = true; _ratingSeen = null; _over = false;
                    _offers.Clear(); _lastRaceRead = DateTime.MinValue;
                    try { _races?.ResetMatch(); } catch { }
                    // A rejoin raises the start again; which it is shows once the board is readable.
                    _rejoinCheck = _rec != null;
                }
                // The game object outlives the match (the end screen): nothing more is recorded from it.
                // A game that is gone WITHOUT having said it was over is a lost connection or a closed
                // client: the record stays open (on disk as unfinished) for the rejoin.
                if (_ended)
                {
                    _ended = false; _armed = false;
                    if (_rec != null)
                    {
                        bool over = _over || GameOver();
                        if (!_rejoinCheck) Observe();
                        if (over || _over) Close(true);
                        else { Write(_rec, _path); _log?.Invoke("[recorder] match " + _rec.Id + " left unfinished at round " + (_turn?.Number ?? 0) + ": waiting for a rejoin"); }
                    }
                    return;
                }
                if (_armed) Observe();
                var rating = _memory?.MatchRatingChange();
                if (rating != null && rating.Length == 2 && (_ratingSeen == null || _ratingSeen[0] != rating[0] || _ratingSeen[1] != rating[1]))
                {
                    _ratingSeen = rating;
                    RatingChanged(rating[0], rating[1]);
                }
            }
            catch (Exception e) { _log?.Invoke("[recorder] " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>The rating the game reported after a match (new value and change).</summary>
        public void RatingChanged(int rating, int change)
        {
            var rec = _rec ?? _last; string path = _rec != null ? _path : _lastPath;
            if (rec == null) return;
            rec.RatingAfter = rating; rec.RatingChange = change;
            Write(rec, path);
            if (_rec == null) { try { Saved?.Invoke(rec); } catch { } }
        }

        private void Observe()
        {
            if (!_game.IsBattlegroundsMatch) return;
            if (!RecordSpectated && _memory != null && _memory.IsSpectating) return;
            var me = _game.PlayerEntity;
            int pid = _game.PlayerId;
            if (me == null || pid == 0) return;
            int turn = _game.TurnNumber;
            if (turn <= 1) GatherOffers(pid);
            if (turn <= 0) return;                       // heroes are still being picked

            IGameEntity gameEntity;
            var heroes = Heroes(me, pid, out gameEntity);
            IGameEntity mine;
            if (!heroes.TryGetValue(pid, out mine)) return;

            if (gameEntity != null && gameEntity.GetTag(TagState) == StateComplete) _over = true;

            if (_rejoinCheck)
            {
                _rejoinCheck = false;
                // The same match shows the same hero again, no earlier than the round it was left in.
                bool same = _rec != null && mine.CardId == _rec.Hero && _turn != null && turn >= _turn.Number && SameLobby(_rec, heroes);
                if (_rec != null && !same) Close(false);
                else if (_rec != null) Rejoined(turn);
            }

            // A match already under way with no record open: the client (or the toolkit) was restarted in
            // it. Its record so far is on disk.
            if (_rec == null && turn > 1) Resume(mine.CardId, turn, heroes);

            if (_rec == null)
            {
                var now = UtcNow();
                string build = null;
                try { build = BuildSource?.Invoke(); } catch { }
                _rec = new MatchRecord { StartUtc = now, Id = now.ToLocalTime().ToString("yyyyMMdd_HHmmss"), Build = build, GameType = _memory?.GameType ?? 0 };
                var s = SeasonOf?.Invoke(now);
                if (s != null && s.Length == 2) { _rec.Season = s[0]; _rec.RatingSeason = s[1]; }
                _path = Path.Combine(_folder, _rec.Id + ".json");
                _turn = null;
                _log?.Invoke("[recorder] match " + _rec.Id);
            }

            // What stays true for the match, refreshed while it can still change (hero picked, trinkets taken).
            _rec.Region = _game.Region ?? _rec.Region;
            _rec.Duos = _rec.Duos || _game.IsBattlegroundsDuosMatch;
            _rec.Hero = mine.CardId ?? _rec.Hero;
            if (gameEntity != null)
            {
                if (gameEntity.GetTag(TagAnomalyDbId) > 0) _rec.AnomalyDbId = gameEntity.GetTag(TagAnomalyDbId);
                if (_rec.GameTagsStart.Count == 0) _rec.GameTagsStart = gameEntity.AllTags();
                ReadMechanics(gameEntity);
            }
            var types = _memory?.LobbyTribes();
            if (types != null && types.Count > 0 && _rec.Types.Count == 0) _rec.Types = types.OrderBy(t => t).ToList();
            foreach (var t in _game.PlayerTrinkets())
                if (!string.IsNullOrEmpty(t.CardId) && !_rec.Trinkets.Contains(t.CardId)) _rec.Trinkets.Add(t.CardId);
            UpdatePlayers(heroes, pid);
            if (_rec.HeroOffers.Count == 0 && _offers.Count > 0)
                _rec.HeroOffers = _offers.Values.OrderBy(o => o.Key).Select(o => o.Value.ToList()).ToList();
            ReadTypes(heroes);

            if (_turn != null && turn > _turn.Number) EndFight(heroes, mine);
            if (_turn == null || turn > _turn.Number)
            {
                CloseTurnTags();
                _turn = new RecordedTurn { Number = turn };
                _rec.Turns.Add(_turn);
                if (_turn.Number > 1) Write(_rec, _path);      // the round before it is whole now
            }
            if (_game.IsBattlegroundsCombatPhase) return;     // the fight plays on copies: the shop's last look stands

            _turn.Tier = Math.Max(1, mine.GetTag(Tags.PLAYER_TECH_LEVEL));
            _turn.Triples = mine.GetTag(Tags.PLAYER_TRIPLES);
            _turn.Health = HealthOf(mine);
            _turn.Opponent = me.GetTag(Tags.NEXT_OPPONENT_PLAYER_ID);
            IGameEntity opp;
            _turn.OpponentHero = heroes.TryGetValue(_turn.Opponent, out opp) ? opp.CardId : null;
            foreach (var kv in heroes)
            {
                _turn.Healths[kv.Key] = HealthOf(kv.Value);
                _turn.Tiers[kv.Key] = Math.Max(1, kv.Value.GetTag(Tags.PLAYER_TECH_LEVEL));
            }
            _turn.Board = _game.PlayerMinions().OrderBy(m => m.GetTag(Tags.ZONE_POSITION)).Take(7).Select(Minion).ToList();
            _playerNow = me.AllTags();
            var cards = new List<RecordedCard>(); var hand = new List<IGameEntity>();
            foreach (var e in _game.Entities())
            {
                if (e.GetTag(Tags.CONTROLLER) != pid || string.IsNullOrEmpty(e.CardId)) continue;
                int type = e.GetTag(Tags.CARDTYPE);
                if (e.GetTag(Tags.ZONE) == ZoneHand) hand.Add(e);
                else if (e.IsInPlay && !IsShopControl(e.CardId, type) && type != CardTypeMinion && type != CardTypeEnchantment && type != CardTypeHero && type != CardTypePlayer && type != CardTypeGame)
                    cards.Add(new RecordedCard { Card = e.CardId, Type = type });
            }
            _turn.Cards = cards.OrderBy(c => c.Type).ThenBy(c => c.Card).ToList();
            _turn.Hand = hand.OrderBy(e => e.GetTag(Tags.ZONE_POSITION)).Select(e => e.CardId).ToList();

            _rec.HeroPowers = _game.Entities()
                .Where(e => e.GetTag(Tags.CARDTYPE) == CardTypeHeroPower && e.GetTag(Tags.CONTROLLER) == pid && e.IsInPlay && !string.IsNullOrEmpty(e.CardId))
                .OrderBy(e => e.Id).Select(e => e.CardId).Distinct().ToList();
        }

        private bool GameOver()
        {
            try
            {
                var g = _game.Entities().FirstOrDefault(e => e.GetTag(Tags.CARDTYPE) == CardTypeGame);
                return g != null && g.GetTag(TagState) == StateComplete;
            }
            catch { return false; }
        }

        // The seats of a lobby keep their heroes: every player the record knows sits where it did.
        private static bool SameLobby(MatchRecord rec, Dictionary<int, IGameEntity> heroes)
        {
            int known = 0;
            foreach (var p in rec.Players)
            {
                IGameEntity h;
                if (!heroes.TryGetValue(p.PlayerId, out h)) continue;
                if (h.CardId != p.Hero && h.CardId != p.Picked) return false;
                known++;
            }
            return known >= Math.Min(4, rec.Players.Count);
        }

        // Back in the match after a gap. Back in the next round: one fight happened meanwhile, the one of
        // the round we left, and its cost still reads from the healths. Later than that: rounds were not
        // seen, and the round left open keeps no result (its fight's cost can't be told from theirs).
        private void Rejoined(int turn)
        {
            _rec.Reconnects++;
            if (_turn != null && turn > _turn.Number + 1) { CloseTurnTags(); _turn = null; }
            _log?.Invoke("[recorder] match " + _rec.Id + " rejoined at round " + turn);
        }

        // The newest unfinished record of this very lobby, picked up from disk.
        private void Resume(string hero, int turn, Dictionary<int, IGameEntity> heroes)
        {
            try
            {
                if (!Directory.Exists(_folder)) return;
                string file = Directory.GetFiles(_folder, "*.json").OrderByDescending(f => f, StringComparer.Ordinal).FirstOrDefault();
                if (file == null) return;
                var rec = JsonConvert.DeserializeObject<MatchRecord>(File.ReadAllText(file));
                if (rec == null || rec.Finished || rec.Version != MatchRecord.CurrentVersion || rec.Hero != hero || rec.Turns.Count == 0) return;
                var now = UtcNow();
                if (now < rec.StartUtc || (now - rec.StartUtc).TotalHours > ResumeHours) return;
                if (rec.Turns[rec.Turns.Count - 1].Number > turn || !SameLobby(rec, heroes)) return;
                _rec = rec; _path = file;
                _turn = rec.Turns[rec.Turns.Count - 1];
                rec.EndUtc = null;
                _playerNow = _playerBase = null;
                Rejoined(turn);
            }
            catch (Exception e) { _log?.Invoke("[recorder] resume failed: " + e.Message); }
        }

        // The hero choice is our hero cards in hand; a reroll changes the card of the same entity.
        private void GatherOffers(int pid)
        {
            foreach (var e in _game.Entities())
            {
                if (e.GetTag(Tags.CARDTYPE) != CardTypeHero || e.GetTag(Tags.CONTROLLER) != pid || e.GetTag(Tags.ZONE) != ZoneHand) continue;
                if (string.IsNullOrEmpty(e.CardId)) continue;
                KeyValuePair<int, List<string>> o;
                if (!_offers.TryGetValue(e.Id, out o)) _offers[e.Id] = o = new KeyValuePair<int, List<string>>(e.GetTag(Tags.ZONE_POSITION), new List<string>());
                if (o.Value.Count == 0 || o.Value[o.Value.Count - 1] != e.CardId) o.Value.Add(e.CardId);
            }
        }

        // Each player's minion types from the leaderboard tiles: the counts the portraits and the panel draw
        // (the standalone reads them with every game read anyway). Keyed by the tile's hero entity.
        private void ReadTypes(Dictionary<int, IGameEntity> heroes)
        {
            if (_races == null) return;
            var now = UtcNow();
            if ((now - _lastRaceRead).TotalMilliseconds < RaceMs && now >= _lastRaceRead) return;
            _lastRaceRead = now;
            Dictionary<int, Dictionary<int, int>> read;
            try { read = _races.Read(); } catch { return; }
            if (read == null) return;
            foreach (var kv in read)
            {
                if (kv.Value == null || kv.Value.Count == 0) continue;
                int pid = _game.Entity(kv.Key)?.GetTag(Tags.PLAYER_ID) ?? 0;
                if (pid <= 0) pid = heroes.FirstOrDefault(h => h.Value.Id == kv.Key).Key;
                var p = pid > 0 ? _rec.Players.FirstOrDefault(x => x.PlayerId == pid) : null;
                if (p != null) p.Types = new Dictionary<int, int>(kv.Value);
            }
        }

        // The shop's own controls are entities of ours in play too: the refresh, freeze and tier-up buttons,
        // and the drag targets (card type 22).
        private static bool IsShopControl(string card, int type) =>
            type == 22 || card.IndexOf("_Button", StringComparison.Ordinal) >= 0 || card.StartsWith("TB_BaconShop_Drag", StringComparison.Ordinal);

        private void ReadMechanics(IGameEntity g)
        {
            var m = _rec.Mechanics;
            m.Buddies |= g.GetTag(TagBuddyEnabled) > 0;
            m.Quests |= g.GetTag(TagQuestsActive) > 0;
            m.Trinkets |= g.GetTag(TagTrinketsActive) > 0;
            m.DarkGifts |= g.GetTag(TagDarkGiftsActive) > 0;
            m.Timewarp |= g.GetTag(TagAltTavernActive) > 0;
            foreach (int tag in new[] { TagAnomalyDbId, TagAnomaly1, TagAnomaly2 })
            {
                int id = g.GetTag(tag);
                if (id > 0 && !m.Anomalies.Contains(id)) m.Anomalies.Add(id);
            }
            if (g.GetTag(Tags.BACON_GLOBAL_OLD_GOD_DBID) > 0) m.Deity = g.GetTag(Tags.BACON_GLOBAL_OLD_GOD_DBID);
            if (g.GetTag(TagDamageCapEnabled) > 0) m.DamageCap = g.GetTag(TagDamageCap);
        }

        // The round in progress gets what changed on our Player since the round before.
        private void CloseTurnTags()
        {
            if (_turn == null || _playerNow == null) return;
            var diff = new Dictionary<int, int>();
            foreach (var kv in _playerNow)
            {
                int was;
                if (_playerBase == null || !_playerBase.TryGetValue(kv.Key, out was) || was != kv.Value) diff[kv.Key] = kv.Value;
            }
            _turn.PlayerTags = diff;
            _playerBase = _playerNow; _playerNow = null;
        }

        // The leaderboard's heroes by PLAYER_ID, and the game entity. A fight plays on copies of the two heroes,
        // which carry a place as well: the leaderboard's own is the one made first (ours: our Player's hero).
        private Dictionary<int, IGameEntity> Heroes(IGameEntity me, int pid, out IGameEntity gameEntity)
        {
            var heroes = new Dictionary<int, IGameEntity>();
            gameEntity = null;
            foreach (var e in _game.Entities())
            {
                int type = e.GetTag(Tags.CARDTYPE);
                if (type == CardTypeGame) { gameEntity = e; continue; }
                if (type != CardTypeHero) continue;
                int id = e.GetTag(Tags.PLAYER_ID);
                if (id <= 0 || e.GetTag(Tags.PLAYER_LEADERBOARD_PLACE) <= 0) continue;
                IGameEntity have;
                if (!heroes.TryGetValue(id, out have) || e.Id < have.Id) heroes[id] = e;
            }
            var own = me == null ? null : _game.Entity(me.GetTag(TagHeroEntity));
            if (own != null && own.GetTag(Tags.PLAYER_LEADERBOARD_PLACE) > 0) heroes[pid] = own;
            return heroes;
        }

        private static int HealthOf(IGameEntity hero) => hero.Health + hero.GetTag(TagArmor);

        // The fight of the round that just ended: what each hero lost across it.
        private void EndFight(Dictionary<int, IGameEntity> heroes, IGameEntity mine)
        {
            int taken = Math.Max(0, _turn.Health - HealthOf(mine)), dealt = 0;
            IGameEntity opp; int before;
            if (heroes.TryGetValue(_turn.Opponent, out opp) && _turn.Healths.TryGetValue(_turn.Opponent, out before) && before > 0)
                dealt = Math.Max(0, before - HealthOf(opp));
            _turn.Taken = taken; _turn.Dealt = dealt;
            _turn.Result = taken > 0 ? "L" : dealt > 0 ? "W" : "T";
        }

        private void UpdatePlayers(Dictionary<int, IGameEntity> heroes, int pid)
        {
            IList<LobbyPlayer> lobby = null;
            foreach (var kv in heroes)
            {
                var p = _rec.Players.FirstOrDefault(x => x.PlayerId == kv.Key);
                if (p == null) _rec.Players.Add(p = new RecordedPlayer { PlayerId = kv.Key, Me = kv.Key == pid });
                p.Hero = kv.Value.CardId ?? p.Hero;
                if (p.Picked == null) p.Picked = kv.Value.CardId;
                p.Place = kv.Value.GetTag(Tags.PLAYER_LEADERBOARD_PLACE);
                p.Tags = kv.Value.AllTags();
                if (p.Name == null && p.Hero != null)
                {
                    if (lobby == null) lobby = _memory?.LobbyPlayers() ?? new List<LobbyPlayer>();
                    p.Name = lobby.FirstOrDefault(l => l.HeroCardId == p.Hero)?.Name;
                }
            }
        }

        private RecordedMinion Minion(IGameEntity m)
        {
            Dictionary<string, int> ench = null;
            try
            {
                foreach (string id in _game.EnchantmentsOf(m.Id))
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    if (ench == null) ench = new Dictionary<string, int>();
                    int n; ench.TryGetValue(id, out n); ench[id] = n + 1;
                }
            }
            catch { }
            string kw = "";
            if (m.GetTag(Tags.TAUNT) > 0) kw += "T";
            if (m.GetTag(Tags.DIVINE_SHIELD) > 0) kw += "D";
            if (m.GetTag(Tags.WINDFURY) > 0) kw += "W";
            if (m.GetTag(Tags.REBORN) > 0) kw += "R";
            if (m.GetTag(Tags.STEALTH) > 0) kw += "S";
            if (m.GetTag(Tags.POISONOUS) > 0) kw += "P";
            if (m.GetTag(Tags.VENOMOUS) > 0) kw += "V";
            if (m.GetTag(Tags.ELITE) > 0) kw += "e";
            if (m.GetTag(Tags.DEATHRATTLE) > 0) kw += "d";
            if (m.GetTag(Tags.RALLY) > 0) kw += "r";
            if (m.GetTag(Tags.TRIGGER_VISUAL) > 0) kw += "t";
            return new RecordedMinion
            {
                Card = m.CardId, Attack = m.GetTag(Tags.ATK), Health = m.Health,
                Golden = m.GetTag(Tags.PREMIUM) > 0, Keywords = kw.Length > 0 ? kw : null,
                Tier = m.GetTag(Tags.TECH_LEVEL), DarkGift = m.GetTag(Tags.HAS_DARK_GIFT) > 0, Enchantments = ench,
            };
        }

        private void Close(bool finished)
        {
            CloseTurnTags();
            var rec = _rec; string path = _path;
            _rec = null; _turn = null; _playerNow = _playerBase = null;
            if (rec == null) return;
            try
            {
                var g = _game.Entities().FirstOrDefault(e => e.GetTag(Tags.CARDTYPE) == CardTypeGame);
                if (g != null) rec.GameTags = g.AllTags();
            }
            catch { }
            // The last round has no next shop to close it: its fight is read from the final healths.
            var last = rec.Turns.LastOrDefault();
            var me = rec.Players.FirstOrDefault(p => p.Me);
            if (finished && last != null && last.Result == null)
            {
                try
                {
                    IGameEntity ge;
                    var heroes = Heroes(_game.PlayerEntity, me?.PlayerId ?? 0, out ge);
                    IGameEntity mine;
                    if (me != null && heroes.TryGetValue(me.PlayerId, out mine)) { var keep = _turn; _turn = last; EndFight(heroes, mine); _turn = keep; }
                }
                catch { }
            }
            if (finished)
            {
                try { rec.FinalBoard = _game.PlayerMinions().OrderBy(m => m.GetTag(Tags.ZONE_POSITION)).Take(7).Select(Minion).ToList(); }
                catch { }
            }
            if (rec.FinalBoard.Count == 0 && last != null) rec.FinalBoard = last.Board;
            rec.Place = me?.Place ?? 0;
            rec.EndUtc = UtcNow();
            rec.Finished = finished && rec.Place > 0;
            if (rec.Turns.Count == 0) { _log?.Invoke("[recorder] match " + rec.Id + " had no rounds: not kept"); return; }
            Write(rec, path);
            _last = rec; _lastPath = path;
            Prune();
            try { Saved?.Invoke(rec); } catch { }
            _log?.Invoke("[recorder] match " + rec.Id + " " + (rec.Finished ? "finished" : "cut short") + ": place " + rec.Place + ", " + rec.Turns.Count + " rounds");
        }

        private void Prune()
        {
            try
            {
                var files = Directory.GetFiles(_folder, "*.json").OrderBy(f => f, StringComparer.Ordinal).ToList();
                for (int i = 0; i < files.Count - KeepRecords; i++) File.Delete(files[i]);
            }
            catch { }
        }

        private void Write(MatchRecord rec, string path)
        {
            try
            {
                Directory.CreateDirectory(_folder);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(rec, Formatting.None));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e) { _log?.Invoke("[recorder] write failed: " + e.Message); }
        }
    }
}
