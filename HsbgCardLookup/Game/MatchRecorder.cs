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
        /// <summary>One letter each: T aunt, D ivine shield, W indfury, R eborn, S tealth, P oisonous, V enomous.</summary>
        [JsonProperty("kw", DefaultValueHandling = DefaultValueHandling.Ignore)] public string Keywords;
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
    }

    public sealed class RecordedPlayer
    {
        [JsonProperty("id")] public int PlayerId;
        [JsonProperty("hero")] public string Hero;
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)] public string Name;
        [JsonProperty("place")] public int Place;
        [JsonProperty("me", DefaultValueHandling = DefaultValueHandling.Ignore)] public bool Me;
    }

    /// <summary>One match as the recorder saw it: <c>matches\&lt;yyyyMMdd_HHmmss&gt;.json</c> in the data folder.</summary>
    public sealed class MatchRecord
    {
        public const int CurrentVersion = 1;
        [JsonProperty("v")] public int Version = CurrentVersion;
        [JsonProperty("id")] public string Id;
        [JsonProperty("startUtc")] public DateTime StartUtc;
        [JsonProperty("endUtc")] public DateTime? EndUtc;
        /// <summary>False while the match runs (the file is rewritten every round) and for one cut short.</summary>
        [JsonProperty("finished")] public bool Finished;
        [JsonProperty("build", NullValueHandling = NullValueHandling.Ignore)] public string Build;
        [JsonProperty("region", NullValueHandling = NullValueHandling.Ignore)] public string Region;
        [JsonProperty("duos", DefaultValueHandling = DefaultValueHandling.Ignore)] public bool Duos;
        /// <summary>The numbered season (the client's battlegrounds_reward_track_season_N) and the rating
        /// period (battlegrounds_ranked_season_N) the match began in; 0 = unknown.</summary>
        [JsonProperty("season", DefaultValueHandling = DefaultValueHandling.Ignore)] public int Season;
        [JsonProperty("ratingSeason", DefaultValueHandling = DefaultValueHandling.Ignore)] public int RatingSeason;
        [JsonProperty("hero")] public string Hero;
        [JsonProperty("heroPowers")] public List<string> HeroPowers = new List<string>();
        [JsonProperty("trinkets")] public List<string> Trinkets = new List<string>();
        [JsonProperty("place")] public int Place;
        [JsonProperty("rating", NullValueHandling = NullValueHandling.Ignore)] public int? RatingAfter;
        [JsonProperty("ratingChange", NullValueHandling = NullValueHandling.Ignore)] public int? RatingChange;
        /// <summary>TAG_RACE ids in the lobby.</summary>
        [JsonProperty("types")] public List<int> Types = new List<int>();
        [JsonProperty("anomaly", DefaultValueHandling = DefaultValueHandling.Ignore)] public int AnomalyDbId;
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
        private const int TagHeroEntity = 27, TagArmor = 292, TagAnomalyDbId = 2897;
        private const int CardTypeGame = 1, CardTypeHero = 3, CardTypeHeroPower = 10;

        private readonly IGameState _game;
        private readonly IClientMemory _memory;
        private readonly string _folder;
        private readonly Action<string> _log;

        /// <summary>The clock (the replay feed supplies the recording's own).</summary>
        public Func<DateTime> UtcNow = () => DateTime.UtcNow;
        /// <summary>The game build to stamp records with.</summary>
        public string Build;
        /// <summary>Match start → { season, rating season }; null when the host has no season calendar.</summary>
        public Func<DateTime, int[]> SeasonOf;

        private MatchRecord _rec;          // the match in progress
        private RecordedTurn _turn;        // its round in progress (the shop is open or the fight is on)
        private string _path;
        private bool _started, _ended;
        private bool _armed;               // a game began and has not ended: a record may start
        private bool _rejoinCheck;         // a game began while a record was open: the same match, or a new one?
        private MatchRecord _last;         // the match just written, for a rating that arrives after it
        private string _lastPath;

        public MatchRecorder(IGameState game, IClientMemory memory, string folder, Action<string> log)
        {
            _game = game; _memory = memory; _folder = folder; _log = log;
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
                    _started = false; _armed = true;
                    // A rejoin raises the start again; which it is shows once the board is readable.
                    _rejoinCheck = _rec != null;
                }
                // The game object outlives the match (the end screen): nothing more is recorded from it.
                if (_ended) { _ended = false; _armed = false; if (_rec != null) { if (!_rejoinCheck) Observe(); Close(true); } return; }
                if (_armed) Observe();
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
        }

        private void Observe()
        {
            if (!_game.IsBattlegroundsMatch) return;
            var me = _game.PlayerEntity;
            int pid = _game.PlayerId;
            if (me == null || pid == 0) return;
            int turn = _game.TurnNumber;
            if (turn <= 0) return;                       // heroes are still being picked

            IGameEntity gameEntity;
            var heroes = Heroes(me, pid, out gameEntity);
            IGameEntity mine;
            if (!heroes.TryGetValue(pid, out mine)) return;

            if (_rejoinCheck)
            {
                _rejoinCheck = false;
                // The same match shows the same hero again, no earlier than the round it was left in.
                bool same = _rec != null && mine.CardId == _rec.Hero && _turn != null && turn >= _turn.Number;
                if (_rec != null && !same) Close(false);
            }

            if (_rec == null)
            {
                var now = UtcNow();
                _rec = new MatchRecord { StartUtc = now, Id = now.ToLocalTime().ToString("yyyyMMdd_HHmmss"), Build = Build };
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
            if (gameEntity != null && gameEntity.GetTag(TagAnomalyDbId) > 0) _rec.AnomalyDbId = gameEntity.GetTag(TagAnomalyDbId);
            var types = _memory?.LobbyTribes();
            if (types != null && types.Count > 0 && _rec.Types.Count == 0) _rec.Types = types.OrderBy(t => t).ToList();
            foreach (var t in _game.PlayerTrinkets())
                if (!string.IsNullOrEmpty(t.CardId) && !_rec.Trinkets.Contains(t.CardId)) _rec.Trinkets.Add(t.CardId);
            UpdatePlayers(heroes, pid);

            if (_turn != null && turn > _turn.Number) EndFight(heroes, mine);
            if (_turn == null || turn > _turn.Number)
            {
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

            _rec.HeroPowers = _game.Entities()
                .Where(e => e.GetTag(Tags.CARDTYPE) == CardTypeHeroPower && e.GetTag(Tags.CONTROLLER) == pid && e.IsInPlay && !string.IsNullOrEmpty(e.CardId))
                .OrderBy(e => e.Id).Select(e => e.CardId).Distinct().ToList();
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
                p.Place = kv.Value.GetTag(Tags.PLAYER_LEADERBOARD_PLACE);
                if (p.Name == null && p.Hero != null)
                {
                    if (lobby == null) lobby = _memory?.LobbyPlayers() ?? new List<LobbyPlayer>();
                    p.Name = lobby.FirstOrDefault(l => l.HeroCardId == p.Hero)?.Name;
                }
            }
        }

        private static RecordedMinion Minion(IGameEntity m)
        {
            string kw = "";
            if (m.GetTag(Tags.TAUNT) > 0) kw += "T";
            if (m.GetTag(Tags.DIVINE_SHIELD) > 0) kw += "D";
            if (m.GetTag(Tags.WINDFURY) > 0) kw += "W";
            if (m.GetTag(Tags.REBORN) > 0) kw += "R";
            if (m.GetTag(Tags.STEALTH) > 0) kw += "S";
            if (m.GetTag(Tags.POISONOUS) > 0) kw += "P";
            if (m.GetTag(Tags.VENOMOUS) > 0) kw += "V";
            return new RecordedMinion
            {
                Card = m.CardId, Attack = m.GetTag(Tags.ATK), Health = m.Health,
                Golden = m.GetTag(Tags.PREMIUM) > 0, Keywords = kw.Length > 0 ? kw : null,
            };
        }

        private void Close(bool finished)
        {
            var rec = _rec; string path = _path;
            _rec = null; _turn = null;
            if (rec == null) return;
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
            _log?.Invoke("[recorder] match " + rec.Id + " " + (rec.Finished ? "finished" : "cut short") + ": place " + rec.Place + ", " + rec.Turns.Count + " rounds");
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
