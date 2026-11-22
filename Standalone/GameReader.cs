using System;
using System.Collections.Generic;
using System.Diagnostics;
using MemSpike;

namespace Toolkit.Standalone
{
    /// <summary>One entity of the client's own entity map, frozen at read time.</summary>
    internal sealed class EntitySnap
    {
        public int Id;
        public string CardId;
        public Dictionary<int, int> Tags;
    }

    /// <summary>One leaderboard tile, in the order the client draws them.</summary>
    internal sealed class TileSnap
    {
        public int HeroEntityId;
        public Dictionary<int, int> Races = new Dictionary<int, int>();
    }

    /// <summary>What one read of the client returned. Immutable once published.</summary>
    internal sealed class Snapshot
    {
        public string Region;                                    // "US" / "EU" / "AP" / "CN", or null
        public long GameStateAddr;                               // 0 = no game; a new address = a new game object
        public Dictionary<int, EntitySnap> Entities = new Dictionary<int, EntitySnap>();
        public int GameEntityId, LocalPlayerEntityId, OpponentPlayerEntityId;
        public List<TileSnap> Tiles = new List<TileSnap>();      // screen order
        public int TeamsWithTwo;                                 // duos: teams holding two tiles
        public List<KeyValuePair<string, string>> Lobby = new List<KeyValuePair<string, string>>();   // name, hero card id
        public List<int> LobbyTribes = new List<int>();
        public int HoveredHeroEntityId;                          // the leaderboard tile under the game's mouse; 0 = none
        /// <summary>Minion entity id → card ids of the enchantments attached to it (the opposing seat's minions only).</summary>
        public Dictionary<int, List<string>> Enchantments = new Dictionary<int, List<string>>();
        public bool FriendsListOpen;                             // the client's friends list is on screen
        public bool GameMenuOpen;                                // the Esc game menu is on screen
        public bool Spectating;                                  // GameMgr.m_spectator: someone else's game, or a replay
        public int GameType;                                     // GameMgr.m_gameType (PegasusShared.GameType)
        public bool HasRating;                                   // the game sent this match's rating change
        public int RatingNew, RatingChange;
        public long ReadMs;
        public long Reads;                                       // ReadProcessMemory calls this walk took

        /// <summary>The features' view of <see cref="Entities"/>, built once per snapshot (UI thread only).</summary>
        public IList<HsbgCardLookup.Host.IGameEntity> EntityList;

        /// <summary>A Battlegrounds game: it has lobby minion types from its creation, tiles later.</summary>
        public bool IsBattlegrounds => GameStateAddr != 0 && (LobbyTribes.Count > 0 || Tiles.Count > 0);
    }

    /// <summary>
    /// Reads the Hearthstone client with the spike's Mono walker. Keeps one attachment for as long as
    /// the process lives and re-attaches when it changes or a read throws (the game loads its code
    /// for ~20 s after launch, and classes have no static block until first used).
    /// </summary>
    internal sealed class GameReader : IDisposable
    {
        private readonly Action<string> _log;
        private Mem _mem;
        private Mono _mono;
        private int _pid;
        private bool _statics;
        private string _lastError;
        // No string cache by address: the client's GC never moves a string but does reuse a freed
        // address, so a late-game card could inherit a removed card's id. Card ids are read when an
        // entity's pointer changes, which is rare enough to read them fresh.
        private long _gameFor;

        // The entity walk. A late Battlegrounds game holds 500-800 live entities, most of them buff
        // enchantments, combat copies and face-down placeholders that no feature reads. Each is
        // classified once and remembered for the life of the game object: enchantments and move
        // helpers are never read again, placeholders only until their card id shows. Field offsets
        // are resolved once per attach, so a needed entity costs a handful of reads instead of ~20.
        private const int TagAttached = 40, TagController = 50, TagPlayerId = 30;
        private const int SideFriendly = 1;      // Player.Side.FRIENDLY
        private const int TagCardType = 202, CardTypeGame = 1, CardTypePlayer = 2, CardTypeEnchantment = 6, CardTypeHoverTarget = 22;
        private sealed class EntityCache
        {
            public long Obj;              // the Entity object this id had when classified
            public long CardStr = -1;     // its m_cardIdInternal string pointer at the last read
            public string CardId;
            public bool Skip;             // an enchantment or move helper: never read again
            public int Attached;          // an enchantment's ATTACHED, from the one read that classified it
            public bool Hidden;           // a face-down placeholder: only its card id is watched
        }
        private readonly Dictionary<int, EntityCache> _entities = new Dictionary<int, EntityCache>();
        private int _offCardId = -1, _offTags, _offValues, _offEntries, _offCount;

        public GameReader(Action<string> log) { _log = log; }

        private long _gameMgr;
        private DateTime _seasonsTried;
        /// <summary>The client's Battlegrounds season events, once read (the server's calendar: it doesn't change in a session).</summary>
        public static volatile List<ClientEvent> Seasons;

        public void Dispose() { Detach(); }

        private void Detach()
        {
            _mem?.Dispose(); _mem = null; _mono = null; _pid = 0; _statics = false; _entities.Clear(); _offCardId = -1;
            _gameMgr = 0;
            _cardRecords = null; CardTexts.Clear();
        }

        // ── Card text records ────────────────────────────────────────────────────────────────────
        // GameDbf.s_index.m_cardsByCardId: card id → CardDbfRecord, for every card the client knows.
        // Asked for by card id from the UI thread, answered here on the reader's thread: the first ask
        // walks the map once (its ids only, about 0.2 s) and keeps each record's address; the client
        // loads the records once and its collector never moves them.
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, HsbgCardLookup.Host.CardTextRecord> CardTexts
            = new System.Collections.Concurrent.ConcurrentDictionary<string, HsbgCardLookup.Host.CardTextRecord>();
        public static readonly System.Collections.Concurrent.ConcurrentQueue<string> CardTextsWanted = new System.Collections.Concurrent.ConcurrentQueue<string>();
        private Dictionary<string, long> _cardRecords;

        private void AnswerCardTexts()
        {
            string id;
            while (CardTextsWanted.TryDequeue(out id))
            {
                if (CardTexts.ContainsKey(id)) continue;
                var m = _mono;
                if (_cardRecords == null)
                {
                    var sw = Stopwatch.StartNew();
                    var found = new Dictionary<string, long>();
                    long map = m.Obj(m.StaticObj("GameDbf", "s_index"), "m_cardsByCardId");
                    foreach (var kv in m.RefMap(map)) { string key = m.Str(kv.Key); if (key != null) found[key] = kv.Value; }
                    if (found.Count == 0) return;                 // not loaded yet: asked again later
                    _cardRecords = found;
                    _log("card records: " + found.Count + " in " + sw.ElapsedMilliseconds + " ms");
                }
                var rec = new HsbgCardLookup.Host.CardTextRecord();
                long r;
                if (_cardRecords.TryGetValue(id, out r))
                {
                    rec.Builder = m.Int(r, "m_cardTextBuilderType");
                    long loc = m.Obj(r, "m_textInHand");
                    rec.Text = LocString(loc);
                    long arr = loc == 0 ? 0 : m.Obj(loc, "m_locValues");
                    int n = Math.Min(m.ArrayLen(arr), HsbgCardLookup.Host.CardTextRecord.Languages.Length);
                    if (n > 0)
                    {
                        rec.Texts = new string[n];
                        for (int i = 0; i < n; i++) rec.Texts[i] = m.Str(_mem.Ptr(arr + 0x20 + i * 8));
                    }
                }
                CardTexts[id] = rec;
            }
        }

        // DbfLocValue: the current locale's string once the client has dropped the others, else the
        // first locale that has one.
        private string LocString(long loc)
        {
            if (loc == 0) return null;
            string cur = _mono.Str(loc, "m_currentLocaleValue");
            if (!string.IsNullOrEmpty(cur)) return cur;
            long arr = _mono.Obj(loc, "m_locValues");
            int n = Math.Min(_mono.ArrayLen(arr), 32);
            for (int i = 0; i < n; i++) { string v = _mono.Str(_mem.Ptr(arr + 0x20 + i * 8)); if (!string.IsNullOrEmpty(v)) return v; }
            return null;
        }

        /// <summary>Null when Hearthstone isn't running or can't be read yet.</summary>
        public Snapshot Read()
        {
            int pid;
            HsWindow.Find(out pid);      // never a process list: see HsWindow
            if (pid == 0) { if (_pid != 0) _log("Hearthstone exited"); Detach(); return null; }
            try
            {
                if (_pid != pid)
                {
                    Detach();
                    _mem = new Mem(pid);
                    _mono = new Mono(_mem);
                    using (var p = Process.GetProcessById(pid)) _mono.Attach(p, "Assembly-CSharp");
                    _pid = pid;
                    _log("attached: " + _mono.Describe().Replace("\n", " | "));
                }
                if (!_statics)
                {
                    _statics = _mono.CalibrateStatics("BnetPresenceMgr", "s_instance");
                    if (!_statics) return null;
                    _log("statics calibrated");
                }
                var sw = Stopwatch.StartNew();
                long reads0 = _mem.Reads;
                var snap = Walk();
                if (!CardTextsWanted.IsEmpty) AnswerCardTexts();
                snap.ReadMs = sw.ElapsedMilliseconds;
                snap.Reads = _mem.Reads - reads0;
                _lastError = null;
                return snap;
            }
            catch (Exception ex)
            {
                string msg = ex.GetType().Name + ": " + ex.Message;
                if (msg != _lastError) { _log("read failed: " + msg); _lastError = msg; }
                if (_mono == null || _mono.Image == 0) Detach();      // attach itself failed: start over next time
                return null;
            }
        }

        private Dictionary<int, int> TagsOf(long entity)
        {
            var d = new Dictionary<int, int>();
            foreach (var t in _mono.IntIntDict(_mono.Obj(_mono.Obj(entity, "m_tags"), "m_values"))) d[t.Key] = t.Value;
            return d;
        }

        private Snapshot Walk()
        {
            var m = _mono;
            var s = new Snapshot();

            long presence = m.StaticObj("BnetPresenceMgr", "s_instance");
            s.Region = Region(m.Obj(m.Obj(presence, "m_myGameAccountId"), "<EntityId>k__BackingField"));

            // Client panels our overlay must not draw over. ChatMgr creates its FriendListFrame when the list
            // opens and destroys it (field back to null) when it closes; GameMenu's m_isShown is a plain bool
            // on its ButtonListMenu base. Both read 0 until the game first uses the class.
            s.FriendsListOpen = m.Obj(m.StaticObj("ChatMgr", "s_instance"), "m_friendListFrame") != 0;
            long menuShown = m.FieldAddr(m.StaticObj("GameMenu", "s_instance"), "m_isShown");
            s.GameMenuOpen = menuShown != 0 && (m.M.I32(menuShown) & 0xFF) != 0;

            // GameMgr is a service: found once per attach. The season calendar is read once, too.
            if (_gameMgr == 0) _gameMgr = ClientEvents.Service(m, "GameMgr");
            if (_gameMgr != 0)
            {
                long spectator = m.FieldAddr(_gameMgr, "m_spectator");
                s.Spectating = spectator != 0 && (m.M.I32(spectator) & 0xFF) != 0;
                s.GameType = m.Int(_gameMgr, "m_gameType");
            }
            if (Seasons == null && (DateTime.UtcNow - _seasonsTried).TotalSeconds > 30)
            {
                _seasonsTried = DateTime.UtcNow;
                try { var events = ClientEvents.Read(m, "battlegrounds_r"); if (events != null && events.Count > 0) { Seasons = events; _log("season calendar: " + events.Count + " events"); } }
                catch (Exception e) { _log("season calendar: " + e.Message); }
            }

            long gs = m.StaticObj("GameState", "s_instance");
            s.GameStateAddr = gs;
            if (gs == 0) return s;
            if (_gameFor != gs) { _entities.Clear(); _gameFor = gs; }

            // Lobby: display name + hero per player, from Battle.net presence.
            foreach (var kv in m.IntMap(m.Obj(gs, "m_playerInfoMap")))
            {
                long hero = m.Obj(kv.Value, "m_playerHero");
                s.Lobby.Add(new KeyValuePair<string, string>(m.Str(kv.Value, "m_name"), m.Str(hero, "m_cardIdInternal")));
            }

            foreach (int race in m.IntList(m.Obj(gs, "m_availableRacesInBattlegroundsExcludingAmalgam")))
                if (race > 0) s.LobbyTribes.Add(race);

            long mgr = m.StaticObj("PlayerLeaderboardManager", "s_instance");
            if (mgr != 0)
            {
                // The tile the game itself says the mouse is on.
                long over = m.Obj(mgr, "m_currentlyMousedOverTile");
                int overId;
                if (over != 0 && TagsOf(m.Obj(over, "m_playerHeroEntity")).TryGetValue(53, out overId)) s.HoveredHeroEntityId = overId;
            }
            if (mgr != 0)
                foreach (long team in m.List(m.Obj(mgr, "m_teams")))
                {
                    int cards = 0;
                    foreach (long card in m.List(m.Obj(team, "m_playerLeaderboardCards")))
                    {
                        cards++;
                        var t = new TileSnap();
                        int id;
                        if (TagsOf(m.Obj(card, "m_playerHeroEntity")).TryGetValue(53, out id)) t.HeroEntityId = id;
                        foreach (var r in m.IntIntMap(m.Obj(m.Obj(card, "m_overlay"), "m_raceCounts"))) t.Races[r.Key] = r.Value;
                        s.Tiles.Add(t);
                    }
                    if (cards >= 2) s.TeamsWithTwo++;
                }

            // Standard, Arena, Tavern Brawl...: nothing reads their entities, so don't walk them. Only a
            // Battlegrounds game has lobby minion types (from its creation) or leaderboard tiles.
            if (!s.IsBattlegrounds) return s;

            // The entities a feature can need: id -> card id + tags (see EntityCache above).
            var live = new HashSet<int>();
            foreach (var kv in m.IntMap(m.Obj(gs, "m_entityMap")))
            {
                if (kv.Value == 0) continue;
                live.Add(kv.Key);
                var e = ReadEntity(kv.Key, kv.Value);
                if (e != null) s.Entities[kv.Key] = e;
            }
            if (_entities.Count > live.Count + 256)                 // the client drops removed entities: so do we
                foreach (var id in new List<int>(_entities.Keys)) if (!live.Contains(id)) _entities.Remove(id);
            if (DumpPath != null) Dump(m, gs);
            long gameEntity = m.Obj(gs, "m_gameEntity");
            if (gameEntity != 0)
            {
                int id;
                if (TagsOf(gameEntity).TryGetValue(53, out id)) s.GameEntityId = id;      // ENTITY_ID
                // TB_BaconShop keeps the BattlegroundsRatingChange packet the server sends as the match ends.
                long rating = m.Obj(gameEntity, "<RatingChangeData>k__BackingField");
                if (rating != 0) { s.HasRating = true; s.RatingNew = m.Int(rating, "_NewRating"); s.RatingChange = m.Int(rating, "_RatingChange"); }
            }

            // The two Player objects: the local one, and whoever it faces now.
            foreach (var kv in m.IntMap(m.Obj(gs, "m_playerMap")))
            {
                long local = m.FieldAddr(kv.Value, "m_local");
                // "Ours" is the local user, or the watched side when spectating (a replay has no local user).
                long side = m.FieldAddr(kv.Value, "m_side");
                bool isLocal = (local != 0 && (m.M.I32(local) & 0xFF) != 0) || (side != 0 && m.M.I32(side) == SideFriendly);
                // The map is keyed by PLAYER_ID; the features want the Player's own entity id.
                int entityId;
                if (!TagsOf(kv.Value).TryGetValue(53, out entityId)) continue;
                if (isLocal) s.LocalPlayerEntityId = entityId; else s.OpponentPlayerEntityId = entityId;
            }

            // Enchantments on the opposing seat's minions (the fight's enemy board; Bob's shop has few).
            // An enchantment is created blank and gets its card id and target when the fight's copy is
            // revealed, which is when it was classified, so the remembered target is the real one.
            EntitySnap opp;
            int seat;
            if (s.Entities.TryGetValue(s.OpponentPlayerEntityId, out opp) && opp.Tags.TryGetValue(TagPlayerId, out seat))
                foreach (var kv in _entities)
                {
                    var c = kv.Value;
                    EntitySnap target; int ctrl, type;
                    if (c.Attached == 0 || string.IsNullOrEmpty(c.CardId) || !live.Contains(kv.Key)) continue;
                    if (!s.Entities.TryGetValue(c.Attached, out target) || !target.Tags.TryGetValue(TagController, out ctrl) || ctrl != seat) continue;
                    if (!target.Tags.TryGetValue(TagCardType, out type) || type != 4) continue;
                    List<string> l;
                    if (!s.Enchantments.TryGetValue(c.Attached, out l)) s.Enchantments[c.Attached] = l = new List<string>();
                    l.Add(c.CardId);
                }
            return s;
        }

        // Test aid (--dump-entities): every entity of the client's map, unfiltered, as JSON lines of what
        // changed since the last read. Read back offline to learn what the client holds and when.
        public string DumpPath;
        private readonly Dictionary<int, KeyValuePair<string, Dictionary<int, int>>> _dumped = new Dictionary<int, KeyValuePair<string, Dictionary<int, int>>>();
        private long _dumpGame;
        private readonly Stopwatch _dumpClock = Stopwatch.StartNew();

        private void Dump(Mono m, long gs)
        {
            var sb = new System.Text.StringBuilder();
            long t = _dumpClock.ElapsedMilliseconds;
            if (_dumpGame != gs)
            {
                _dumpGame = gs; _dumped.Clear();
                sb.Append("{\"t\":").Append(t).Append(",\"game\":\"0x").Append(gs.ToString("X")).Append("\"}\n");
            }
            var live = new HashSet<int>();
            foreach (var kv in m.IntMap(m.Obj(gs, "m_entityMap")))
            {
                if (kv.Value == 0) continue;
                if (_offCardId < 0 && !ResolveEntityOffsets(kv.Value)) return;
                live.Add(kv.Key);
                string card = m.Str(m.M.Ptr(kv.Value + _offCardId)) ?? "";
                var tags = FastTags(kv.Value);
                KeyValuePair<string, Dictionary<int, int>> old;
                bool known = _dumped.TryGetValue(kv.Key, out old);
                var diff = new List<KeyValuePair<int, int>>();
                foreach (var tg in tags) { int ov; if (!known || !old.Value.TryGetValue(tg.Key, out ov) || ov != tg.Value) diff.Add(tg); }
                if (known) foreach (var tg in old.Value) if (!tags.ContainsKey(tg.Key)) diff.Add(new KeyValuePair<int, int>(tg.Key, int.MinValue));
                if (known && old.Key == card && diff.Count == 0) continue;
                sb.Append("{\"t\":").Append(t).Append(",\"id\":").Append(kv.Key);
                if (!known) sb.Append(",\"new\":true");
                if (!known || old.Key != card) sb.Append(",\"card\":\"").Append(card.Replace("\\", "").Replace("\"", "")).Append('"');
                sb.Append(",\"tags\":{");
                for (int i = 0; i < diff.Count; i++)
                    sb.Append(i > 0 ? "," : "").Append('"').Append(diff[i].Key).Append("\":").Append(diff[i].Value == int.MinValue ? "null" : diff[i].Value.ToString());
                sb.Append("}}\n");
                _dumped[kv.Key] = new KeyValuePair<string, Dictionary<int, int>>(card, tags);
            }
            foreach (var id in new List<int>(_dumped.Keys))
                if (!live.Contains(id)) { _dumped.Remove(id); sb.Append("{\"t\":").Append(t).Append(",\"id\":").Append(id).Append(",\"gone\":true}\n"); }
            if (sb.Length > 0) System.IO.File.AppendAllText(DumpPath, sb.ToString());
        }

        /// <summary>One entity for the snapshot, or null when it is one the features never read.</summary>
        private EntitySnap ReadEntity(int id, long obj)
        {
            if (_offCardId < 0 && !ResolveEntityOffsets(obj)) return null;
            EntityCache c;
            if (!_entities.TryGetValue(id, out c) || c.Obj != obj) _entities[id] = c = new EntityCache { Obj = obj };
            if (c.Skip) return null;

            long str = _mono.M.Ptr(obj + _offCardId);
            if (str != c.CardStr) { c.CardStr = str; c.CardId = _mono.Str(str); c.Hidden = false; }
            if (c.Hidden) return null;

            var tags = FastTags(obj);
            int type;
            tags.TryGetValue(TagCardType, out type);
            if (type == CardTypeEnchantment || type == CardTypeHoverTarget)
            {
                c.Skip = true;
                if (type == CardTypeEnchantment) tags.TryGetValue(TagAttached, out c.Attached);
                return null;
            }
            // No card id and not the game or a player: a face-down placeholder (other players' things).
            if (string.IsNullOrEmpty(c.CardId) && type != CardTypeGame && type != CardTypePlayer) { c.Hidden = true; return null; }
            return new EntitySnap { Id = id, CardId = c.CardId, Tags = tags };
        }

        // Offsets of Entity.m_cardIdInternal, Entity.m_tags, EntityTags.m_values and Dictionary's
        // _entries/_count, from the first entity seen. Player and GameEntity derive from Entity, and a
        // derived class keeps its base fields at the same offsets.
        private bool ResolveEntityOffsets(long entity)
        {
            var m = _mono;
            long card = m.FieldAddr(entity, "m_cardIdInternal"), tagsField = m.FieldAddr(entity, "m_tags");
            long tags = tagsField == 0 ? 0 : m.M.Ptr(tagsField);
            long valuesField = m.FieldAddr(tags, "m_values");
            long dict = valuesField == 0 ? 0 : m.M.Ptr(valuesField);
            long entries = m.FieldAddr(dict, "_entries"), count = m.FieldAddr(dict, "_count");
            if (card == 0 || tagsField == 0 || valuesField == 0 || entries == 0 || count == 0) return false;
            _offCardId = (int)(card - entity); _offTags = (int)(tagsField - entity); _offValues = (int)(valuesField - tags);
            _offEntries = (int)(entries - dict); _offCount = (int)(count - dict);
            _log("entity offsets: cardId 0x" + _offCardId.ToString("X") + " tags 0x" + _offTags.ToString("X") + " values 0x" + _offValues.ToString("X")
                + " entries 0x" + _offEntries.ToString("X") + " count 0x" + _offCount.ToString("X"));
            return true;
        }

        /// <summary>An entity's tags through the resolved offsets: tags object, dictionary, its header, its entries.</summary>
        private Dictionary<int, int> FastTags(long entity)
        {
            var mem = _mono.M;
            var d = new Dictionary<int, int>();
            long dict = mem.Ptr(mem.Ptr(entity + _offTags) + _offValues);
            if (dict == 0) return d;
            int lo = Math.Min(_offEntries, _offCount), hi = Math.Max(_offEntries + 8, _offCount + 4);
            var head = mem.Bytes(dict + lo, hi - lo);
            if (head == null) return d;
            long arr = BitConverter.ToInt64(head, _offEntries - lo);
            int n = BitConverter.ToInt32(head, _offCount - lo);
            if (arr == 0 || n <= 0 || n > 4096) return d;
            var e = mem.Bytes(arr + 0x20, n * 16);                  // Entry { hashCode, next, key, value }; _count <= length
            if (e == null) return d;
            for (int i = 0; i < n; i++)
                if (BitConverter.ToInt32(e, i * 16) >= 0) d[BitConverter.ToInt32(e, i * 16 + 8)] = BitConverter.ToInt32(e, i * 16 + 12);
            return d;
        }

        // Battle.net EntityId { high_, low_ }: the region is byte 4 of the high half of the game
        // account logged in now (1 = US, 2 = EU, 3 = Asia, 5 = China).
        private string Region(long entityId)
        {
            if (entityId == 0) return null;
            long hiAddr = _mono.FieldAddr(entityId, "high_");
            if (hiAddr == 0) return null;
            ulong hi = (ulong)_mono.M.Ptr(hiAddr);
            switch ((hi >> 32) & 0xFF)
            {
                case 1: return "US";
                case 2: return "EU";
                case 3: return "AP";
                case 5: return "CN";
                default: return null;
            }
        }
    }
}
