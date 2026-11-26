using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace HsbgCardLookup.Game
{
    /// <summary>One finished match of a session, as much of its record as the session panel shows.</summary>
    public sealed class SessionGame
    {
        [JsonProperty("startUtc")] public DateTime StartUtc;
        [JsonProperty("endUtc")] public DateTime? EndUtc;
        [JsonProperty("finished")] public bool Finished;
        [JsonProperty("duos")] public bool DuosFlag;
        [JsonProperty("gameType")] public int GameType;
        [JsonProperty("hero")] public string Hero;
        [JsonProperty("place")] public int Place;
        [JsonProperty("rating")] public int? Rating;
        [JsonProperty("ratingChange")] public int? Change;

        [JsonIgnore] public bool Duos => DuosFlag || GameType == 37;
        // Custom lobbies (24 solo, 39 duos) move no rating.
        [JsonIgnore] public bool Ranked => GameType != 24 && GameType != 39;
        [JsonIgnore] public DateTime End => EndUtc ?? StartUtc;
    }

    /// <summary>A session in one mode: its matches newest first, the rating it began at and the rating now.</summary>
    public sealed class SessionView
    {
        public List<SessionGame> Games = new List<SessionGame>();
        public int? Start, Current;
    }

    /// <summary>
    /// The player's current session, read from the match recorder's files: the latest run of finished
    /// ranked matches with no break longer than <see cref="GapHours"/> between one's end and the next
    /// one's start, Solos and Duos together (one sitting), each shown with its own rating. The files
    /// are read off the UI thread; everything else here is the UI thread's.
    /// </summary>
    public sealed class MatchSession
    {
        public const double GapHours = 4;
        private const int ReadNewest = 60;        // more matches than one sitting holds

        private readonly string _folder;
        private readonly Action<Action> _toUi;
        private readonly Action<string> _log;
        private readonly Dictionary<string, KeyValuePair<DateTime, SessionGame>> _parsed = new Dictionary<string, KeyValuePair<DateTime, SessionGame>>();   // reader thread only
        private List<SessionGame> _games = new List<SessionGame>();   // finished ranked matches, newest first
        private bool _reading, _again;

        /// <summary>Counts up whenever the matches were read again.</summary>
        public int Version { get; private set; }

        public MatchSession(string folder, Action<Action> toUi, Action<string> log)
        {
            _folder = folder; _toUi = toUi; _log = log;
        }

        /// <summary>Read the newest records again, in the background; <see cref="Version"/> moves when done.</summary>
        public void Reload()
        {
            if (_reading) { _again = true; return; }
            _reading = true;
            Task.Run(() =>
            {
                List<SessionGame> games = null;
                try { games = Read(); }
                catch (Exception e) { try { _log?.Invoke("[session] " + e.GetType().Name + ": " + e.Message); } catch { } }
                _toUi(() =>
                {
                    _reading = false;
                    if (games != null) { _games = games; Version++; }
                    if (_again) { _again = false; Reload(); }
                });
            });
        }

        private List<SessionGame> Read()
        {
            var list = new List<SessionGame>();
            if (!Directory.Exists(_folder)) return list;
            // The files are named by their start, yyyyMMdd_HHmmss: the name's order is time's order.
            var files = new DirectoryInfo(_folder).GetFiles("*.json").OrderByDescending(f => f.Name, StringComparer.Ordinal).Take(ReadNewest).ToList();
            var keep = new HashSet<string>();
            foreach (var f in files)
            {
                keep.Add(f.Name);
                KeyValuePair<DateTime, SessionGame> hit;
                if (!_parsed.TryGetValue(f.Name, out hit) || hit.Key != f.LastWriteTimeUtc)
                {
                    SessionGame g = null;
                    try { g = JsonConvert.DeserializeObject<SessionGame>(File.ReadAllText(f.FullName)); } catch { }
                    _parsed[f.Name] = hit = new KeyValuePair<DateTime, SessionGame>(f.LastWriteTimeUtc, g);
                }
                if (hit.Value != null && hit.Value.Finished && hit.Value.Ranked && hit.Value.Place > 0) list.Add(hit.Value);
            }
            foreach (string gone in _parsed.Keys.Where(k => !keep.Contains(k)).ToList()) _parsed.Remove(gone);
            return list.OrderByDescending(g => g.StartUtc).ToList();
        }

        /// <summary>The mode last played, for when no match is on to say which one to show.</summary>
        public bool LastWasDuos => _games.Count > 0 && _games[0].Duos;

        /// <summary>
        /// The session as of <paramref name="asOf"/> (now, or the start of the match being played: a long
        /// match never ends the session it belongs to). Matches begun before <paramref name="resetUtc"/>
        /// are left out. <paramref name="clientRating"/> is the game's own word on the rating now.
        /// </summary>
        public SessionView View(bool duos, DateTime asOf, DateTime resetUtc, int? clientRating)
        {
            var view = new SessionView();
            DateTime edge = asOf;
            foreach (var g in _games)
            {
                if (g.StartUtc < resetUtc || (edge - g.End).TotalHours > GapHours) break;
                if (g.Duos == duos) view.Games.Add(g);
                edge = g.StartUtc;
            }
            var newest = view.Games.FirstOrDefault(g => g.Rating.HasValue);
            var oldest = view.Games.LastOrDefault(g => g.Rating.HasValue && g.Change.HasValue);
            // A match of this session is newer than anything the client's cache holds; with none, the
            // cache is (it also knows of matches played on another device), then the last record.
            view.Current = newest?.Rating ?? clientRating ?? _games.FirstOrDefault(g => g.Duos == duos && g.Rating.HasValue)?.Rating;
            view.Start = oldest != null ? oldest.Rating - oldest.Change : view.Current;
            return view;
        }
    }
}
