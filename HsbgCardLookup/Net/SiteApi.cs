using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace HsbgCardLookup.Net
{
    /// <summary>
    /// The two hsbg.cards endpoints the toolkit reads besides card data: the newest patch-notes page
    /// (<c>/api/v1/patches/latest</c>) and the tools list (<c>/api/v1/plugins</c>). Both are best-effort:
    /// offline, a site without them, or any parse failure gives null.
    /// </summary>
    public static class SiteApi
    {
        public static async Task<PatchRow> GetLatestPatchAsync() =>
            (await AssetClient.GetJsonAsync<Envelope<PatchRow>>(AssetClient.SiteBase + "/api/v1/patches/latest").ConfigureAwait(false))?.Data;

        public static async Task<List<PluginRow>> GetPluginsAsync() =>
            (await AssetClient.GetJsonAsync<Envelope<List<PluginRow>>>(AssetClient.SiteBase + "/api/v1/plugins").ConfigureAwait(false))?.Data;

        // Every /api/v1 answer comes as { "data": … } (seen on the live site 2026-11-19).
        private sealed class Envelope<T>
        {
            [JsonProperty("data")] public T Data { get; set; }
        }
    }

    /// <summary>One patch-notes page. <see cref="Links"/> lists only the languages the page really
    /// exists in, so the URL is picked from it (<see cref="UrlFor"/>) — never built from a pattern.</summary>
    public sealed class PatchRow
    {
        [JsonProperty("version")] public string Version { get; set; }
        [JsonProperty("publishedAt")] public string PublishedAt { get; set; }
        [JsonProperty("url")] public string Url { get; set; }
        [JsonProperty("links")] public List<PatchLink> Links { get; set; }

        /// <summary>The page in the site language <paramref name="web"/> when it exists, else English.</summary>
        public string UrlFor(string web)
        {
            if (Links != null && !string.IsNullOrEmpty(web))
                foreach (var l in Links)
                    if (l != null && !string.IsNullOrEmpty(l.Url) && string.Equals(l.Locale, web, StringComparison.OrdinalIgnoreCase))
                        return l.Url;
            return Url;
        }
    }

    public sealed class PatchLink
    {
        [JsonProperty("locale")] public string Locale { get; set; }
        [JsonProperty("url")] public string Url { get; set; }
    }

    /// <summary>One tool in the site's list: its own (<see cref="Official"/>) or a third party's.</summary>
    public sealed class PluginRow
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("author")] public string Author { get; set; }
        [JsonProperty("official")] public bool Official { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("page")] public string Page { get; set; }
        [JsonProperty("repo")] public string Repo { get; set; }
        [JsonProperty("releasesUrl")] public string ReleasesUrl { get; set; }
        [JsonProperty("latest")] public PluginRelease Latest { get; set; }   // null when GitHub's data is unavailable
    }

    public sealed class PluginRelease
    {
        [JsonProperty("version")] public string Version { get; set; }
        [JsonProperty("url")] public string Url { get; set; }
        [JsonProperty("downloads")] public List<PluginDownload> Downloads { get; set; }
    }

    public sealed class PluginDownload
    {
        [JsonProperty("platform")] public string Platform { get; set; }   // "windows" | "linux"
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("url")] public string Url { get; set; }
        [JsonProperty("size")] public long Size { get; set; }
    }
}
