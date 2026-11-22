using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace HsbgCardLookup.Game
{
    /// <summary>One of the game client's timed events (UTC; a null end = open).</summary>
    public sealed class SeasonEvent
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("start")] public DateTime? Start;
        [JsonProperty("end")] public DateTime? End;
    }

    /// <summary>
    /// Battlegrounds seasons as the game's own server calendar gives them (read from the client by the host,
    /// kept in <c>seasons.json</c> so they are there without the game). Two series, numbered separately:
    /// the season players count ("battlegrounds_reward_track_season_N": Season 14 began 2026-08-04) and the
    /// rating period ("battlegrounds_ranked_season_N").
    /// </summary>
    public sealed class SeasonCalendar
    {
        public const string SeasonPrefix = "battlegrounds_reward_track_season_", RatingPrefix = "battlegrounds_ranked_season_";

        public sealed class Span { public int Number; public DateTime Start; public DateTime? End; }

        public readonly List<Span> Seasons = new List<Span>(), RatingSeasons = new List<Span>();

        public static SeasonCalendar From(IEnumerable<SeasonEvent> events)
        {
            var c = new SeasonCalendar();
            foreach (var e in events ?? Enumerable.Empty<SeasonEvent>())
            {
                if (e?.Name == null || e.Start == null) continue;
                Add(c.Seasons, SeasonPrefix, e); Add(c.RatingSeasons, RatingPrefix, e);
            }
            c.Seasons.Sort((a, b) => a.Start.CompareTo(b.Start));
            c.RatingSeasons.Sort((a, b) => a.Start.CompareTo(b.Start));
            return c;
        }

        private static void Add(List<Span> list, string prefix, SeasonEvent e)
        {
            int n;
            if (e.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && int.TryParse(e.Name.Substring(prefix.Length), out n))
                list.Add(new Span { Number = n, Start = e.Start.Value, End = e.End });
        }

        public static SeasonCalendar Load(string path)
        {
            try { return File.Exists(path) ? From(JsonConvert.DeserializeObject<List<SeasonEvent>>(File.ReadAllText(path))) : null; }
            catch { return null; }
        }

        /// <summary>{ season, rating season } a moment falls in; 0 where the calendar has none for it.</summary>
        public int[] Of(DateTime utc) => new[] { At(Seasons, utc), At(RatingSeasons, utc) };

        // The latest span that had begun: the running one's end is a placeholder a year out, and a gap
        // between two spans belongs to the earlier one.
        private static int At(List<Span> spans, DateTime utc)
        {
            int n = 0;
            foreach (var s in spans) if (s.Start <= utc) n = s.Number;
            return n;
        }
    }
}
