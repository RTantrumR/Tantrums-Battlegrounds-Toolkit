using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using HsbgCardLookup.Config;

namespace HsbgCardLookup.Net
{
    /// <summary>
    /// A disk cache for the small JSON answers the toolkit polls — the site's patch, tools and notice
    /// lists and GitHub's latest release — so the servers are asked at most every <see cref="Ttl"/>, a
    /// restart included. One file per URL under <c>cache\api\</c> in the data folder; its write time is the
    /// fetch time. A failed fetch falls back to the stale copy rather than to nothing.
    /// </summary>
    public static class SiteCache
    {
        public static readonly TimeSpan Ttl = TimeSpan.FromHours(2);

        private static string Dir => Path.Combine(PluginConfig.DataDir, "cache", "api");

        /// <param name="fresh">Skip the cache (a "check now" button); the answer still refreshes it.</param>
        public static async Task<T> GetJsonAsync<T>(string url, bool fresh = false) where T : class
        {
            string file = FileFor(url);
            string cached = Read(file, out DateTime fetchedUtc);
            if (!fresh && cached != null && DateTime.UtcNow - fetchedUtc < Ttl)
            {
                var hit = Parse<T>(cached);
                if (hit != null) return hit;
            }

            string body = await AssetClient.GetStringAsync(url).ConfigureAwait(false);
            var parsed = body != null ? Parse<T>(body) : null;
            if (parsed != null)
            {
                Write(file, body);
                return parsed;
            }
            return cached != null ? Parse<T>(cached) : null;   // offline or a bad answer: the last good one
        }

        private static T Parse<T>(string json) where T : class
        {
            try { return JsonConvert.DeserializeObject<T>(json); } catch { return null; }
        }

        private static string FileFor(string url)
        {
            var sb = new StringBuilder();
            foreach (char c in url.Replace("https://", "").Replace("http://", ""))
                sb.Append(char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_');
            return Path.Combine(Dir, sb + ".json");
        }

        private static string Read(string file, out DateTime fetchedUtc)
        {
            fetchedUtc = DateTime.MinValue;
            try
            {
                if (!File.Exists(file)) return null;
                fetchedUtc = File.GetLastWriteTimeUtc(file);
                return File.ReadAllText(file, Encoding.UTF8);
            }
            catch { return null; }
        }

        private static void Write(string file, string body)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, body, new UTF8Encoding(false));
            }
            catch { /* a cache that can't be written is just a cache miss next time */ }
        }
    }
}
