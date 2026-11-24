using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HsbgCardLookup.Host;
using HsbgCardLookup.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HsbgCardLookup.Game
{
    /// <summary>
    /// The report of one finished match for the site's community stats, cut from the local record: the
    /// lobby, our pick and result, the final board and all eight players without names. No rounds.
    /// Shape and rules: docs/branches/match-stats/upload-spec.md.
    /// </summary>
    public static class MatchUpload
    {
        public const int PayloadVersion = 1;
        private const int GameTypeRanked = 23, TagArmor = 292;

        /// <summary>Ranked solo matches played to their end. Duos and custom lobbies are not sent.</summary>
        public static bool Wanted(MatchRecord r) =>
            r != null && r.Finished && !r.Duos && r.GameType == GameTypeRanked && r.Place > 0 && r.Turns.Count > 0;

        public static JObject Build(MatchRecord r, string client, string install)
        {
            var o = new JObject
            {
                ["v"] = PayloadVersion,
                ["client"] = client,
                ["install"] = install,
                ["startUtc"] = r.StartUtc,
            };
            if (r.EndUtc != null) o["endUtc"] = r.EndUtc.Value;
            if (r.Build != null) o["build"] = r.Build;
            if (r.Region != null) o["region"] = r.Region;
            o["duos"] = r.Duos;
            o["gameType"] = r.GameType;
            o["season"] = r.Season;
            o["ratingSeason"] = r.RatingSeason;
            o["types"] = new JArray(r.Types);
            o["mechanics"] = JObject.FromObject(r.Mechanics);
            o["hero"] = r.Hero;
            o["heroOffers"] = JToken.FromObject(r.HeroOffers);
            o["heroPowers"] = new JArray(r.HeroPowers);
            o["trinkets"] = new JArray(r.Trinkets);
            o["place"] = r.Place;
            if (r.RatingAfter != null) o["rating"] = r.RatingAfter.Value;
            if (r.RatingChange != null) o["ratingChange"] = r.RatingChange.Value;
            o["rounds"] = r.Turns.Count;

            int won = 0, lost = 0, tied = 0;
            var tierTurns = new JObject();
            foreach (var t in r.Turns)
            {
                if (t.Result == "W") won++; else if (t.Result == "L") lost++; else if (t.Result == "T") tied++;
                string tier = t.Tier.ToString();
                if (t.Tier > 0 && tierTurns[tier] == null) tierTurns[tier] = t.Number;
            }
            o["fights"] = new JObject { ["W"] = won, ["L"] = lost, ["T"] = tied };
            o["tierTurns"] = tierTurns;

            var players = new JArray();
            foreach (var p in r.Players)
            {
                var q = new JObject { ["hero"] = p.Hero, ["picked"] = p.Picked, ["place"] = p.Place };
                if (p.Me) q["me"] = true;
                if (p.Types != null && p.Types.Count > 0) q["types"] = JObject.FromObject(p.Types);
                // The hero's tags as last seen: what it had left, and its tavern.
                int hp, dmg, armor, tier;
                if (p.Tags != null && p.Tags.TryGetValue(Tags.HEALTH, out hp))
                {
                    p.Tags.TryGetValue(Tags.DAMAGE, out dmg); p.Tags.TryGetValue(TagArmor, out armor);
                    q["health"] = hp - dmg + armor;
                }
                if (p.Tags != null && p.Tags.TryGetValue(Tags.PLAYER_TECH_LEVEL, out tier) && tier > 0) q["tier"] = tier;
                players.Add(q);
            }
            o["players"] = players;
            o["finalBoard"] = JToken.FromObject(r.FinalBoard);
            return o;
        }
    }

    /// <summary>
    /// "My stats": asks the site for a one-time address that ties this install's reports to whoever signs
    /// in there, and hands it to the browser. The install key goes in the request's body only, never into
    /// an address; the answer's address carries a short-lived code instead.
    /// </summary>
    public static class MatchLink
    {
        /// <summary>Where the request goes. Empty until the site has it: the button is not shown.</summary>
        public const string DefaultEndpoint = "";

        public static string Endpoint
        {
            get
            {
                string test = Environment.GetEnvironmentVariable("TOOLKIT_LINK_URL");
                return string.IsNullOrEmpty(test) ? DefaultEndpoint : test;
            }
        }

        public static bool Available => !string.IsNullOrEmpty(Endpoint);

        /// <summary>The address to open, or null when the site didn't give a usable one.</summary>
        public static async Task<string> RequestAsync(string dataDir, string client)
        {
            string endpoint = Endpoint;
            if (string.IsNullOrEmpty(endpoint)) return null;
            var body = new JObject { ["install"] = MatchUploader.InstallKey(dataDir), ["client"] = client };
            string answer = await AssetClient.PostJsonAsync(endpoint, body.ToString(Formatting.None)).ConfigureAwait(false);
            if (string.IsNullOrEmpty(answer)) return null;
            try
            {
                string url = (string)JObject.Parse(answer).SelectToken("data.url");
                return Trusted(url, endpoint) ? url : null;
            }
            catch { return null; }
        }

        // Only a web address on the host we asked (or the site itself) is handed to the browser.
        private static bool Trusted(string url, string endpoint)
        {
            Uri u, e, site;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp)) return false;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out e) || !Uri.TryCreate(AssetClient.SiteBase, UriKind.Absolute, out site)) return false;
            if (u.Scheme == Uri.UriSchemeHttp && !u.IsLoopback) return false;
            return string.Equals(u.Host, e.Host, StringComparison.OrdinalIgnoreCase) || string.Equals(u.Host, site.Host, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Sends the reports, one POST each. A report is a file in <c>match-uploads\</c> until the site takes
    /// it, so a match played offline (or before the site listens) goes out at a later start.
    /// </summary>
    public sealed class MatchUploader
    {
        public const string FolderName = "match-uploads";
        /// <summary>Where reports go. Empty until the site has the address: reports wait on disk.</summary>
        public const string DefaultEndpoint = "";
        private const int Keep = 200;                 // waiting reports; older ones are dropped
        private const int RatingWaitSec = 60;         // the rating packet can follow the match's end
        private const int IdleSec = 30, RetrySec = 300;

        private readonly string _folder, _client, _install, _endpoint;
        private readonly Func<bool> _enabled;
        private readonly Action<string> _log;
        private DateTime _next = DateTime.MinValue;
        private volatile bool _busy;

        public MatchUploader(string dataDir, string client, Func<bool> enabled, Action<string> log, string endpoint = null)
        {
            _folder = Path.Combine(dataDir, FolderName);
            _client = client; _enabled = enabled; _log = log;
            _endpoint = string.IsNullOrEmpty(endpoint) ? DefaultEndpoint : endpoint;
            _install = InstallKey(dataDir);
        }

        // A random key made once per install: tells one toolkit's reports from another's, names nobody.
        public static string InstallKey(string dataDir)
        {
            string file = Path.Combine(dataDir, "install-id.txt");
            try
            {
                if (File.Exists(file))
                {
                    string have = File.ReadAllText(file).Trim();
                    if (have.Length == 32) return have;
                }
                string made = Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(dataDir);
                File.WriteAllText(file, made);
                return made;
            }
            catch { return Guid.NewGuid().ToString("N"); }
        }

        /// <summary>A match was written (again when its rating arrives): its report replaces the waiting one.</summary>
        public void Enqueue(MatchRecord rec)
        {
            try
            {
                if (!_enabled() || !MatchUpload.Wanted(rec)) return;
                string path = Path.Combine(_folder, rec.Id + ".json");
                if (File.Exists(path + ".sent")) return;          // gone out already, before a late rating
                Directory.CreateDirectory(_folder);
                File.WriteAllText(path, MatchUpload.Build(rec, _client, _install).ToString(Formatting.None));
                _next = DateTime.MinValue;
            }
            catch (Exception e) { _log?.Invoke("[upload] queue failed: " + e.Message); }
        }

        /// <summary>From the toolkit's poll; cheap unless something waits.</summary>
        public void Tick()
        {
            if (_busy || DateTime.UtcNow < _next) return;
            _next = DateTime.UtcNow.AddSeconds(IdleSec);
            try
            {
                if (!Directory.Exists(_folder)) return;
                if (!_enabled())
                {
                    foreach (string f in Directory.GetFiles(_folder)) File.Delete(f);     // sharing switched off: nothing waits
                    return;
                }
                foreach (string f in Directory.GetFiles(_folder, "*.sent"))
                    if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-1)) File.Delete(f);
                var waiting = Directory.GetFiles(_folder, "*.json").OrderBy(f => f, StringComparer.Ordinal).ToList();
                while (waiting.Count > Keep) { File.Delete(waiting[0]); waiting.RemoveAt(0); }
                if (waiting.Count == 0 || string.IsNullOrEmpty(_endpoint)) return;
                _busy = true;
                Task.Run(() => Send(waiting));
            }
            catch (Exception e) { _log?.Invoke("[upload] " + e.Message); }
        }

        private async Task Send(List<string> files)
        {
            try
            {
                foreach (string file in files)
                {
                    string json = File.ReadAllText(file);
                    // No rating in it yet: give the game's packet a minute before the report leaves without one.
                    if (json.IndexOf("\"rating\":", StringComparison.Ordinal) < 0
                        && (DateTime.UtcNow - File.GetLastWriteTimeUtc(file)).TotalSeconds < RatingWaitSec) continue;
                    int status = await AssetClient.PostJsonGzipAsync(_endpoint, json).ConfigureAwait(false);
                    bool taken = status >= 200 && status < 300;
                    bool refused = status == 400 || status == 409 || status == 422;     // it will never be taken: don't resend
                    string id = Path.GetFileNameWithoutExtension(file);
                    if (taken || refused)
                    {
                        File.WriteAllText(file + ".sent", status.ToString());
                        File.Delete(file);
                        _log?.Invoke("[upload] " + id + (taken ? " sent" : " refused " + status));
                    }
                    else
                    {
                        _log?.Invoke("[upload] " + id + " not sent (" + status + "), later");
                        _next = DateTime.UtcNow.AddSeconds(RetrySec);
                        break;
                    }
                }
            }
            catch (Exception e) { _log?.Invoke("[upload] " + e.Message); }
            finally { _busy = false; }
        }
    }
}
