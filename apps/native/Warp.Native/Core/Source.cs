// Library sources: Plex over HTTP (port of PlexClient + plexSource) and a deterministic mock fixture
// (port of mock.ts) so every screen can be exercised on the TV without a Plex account.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Warp.Core
{
    public interface ILibrarySource
    {
        string Name { get; }
        bool IsMock { get; }
        Task<List<Hub>> Hubs();
        Task<Sections> Sections();
        Task<MediaItem> Metadata(string ratingKey);
        Task<List<MediaItem>> SectionAll(string sectionId, int type);
        Task<List<MediaItem>> Children(string ratingKey);
        Task SelectStreams(string partId, string audioStreamId, string subtitleStreamId);
        Task Timeline(string ratingKey, string state, double timeMs, double durationMs);
        /// <summary>Image URL for a Plex path, or a "mock:" descriptor the UI draws natively.</summary>
        string Image(string path, int width, int height);
        /// <summary>URL the native player opens for this decision ("" = nothing playable in this source).</summary>
        string PlaybackUrl(PlaybackDecision d);
    }

    public class Session
    {
        public const string Key = "warp.session.v1";
        public string ClientId = "";
        public string Token = "";
        public string ServerUri = "";
        public string ServerName = "";
        public bool Mock;

        public static Session Load(IKeyStore store)
        {
            try
            {
                var raw = store.Get(Key);
                if (!string.IsNullOrEmpty(raw))
                    using (var d = JsonDocument.Parse(raw))
                    {
                        var r = d.RootElement;
                        var s = new Session { ClientId = J.Str(r, "clientId"), Token = J.Str(r, "token"), ServerUri = J.Str(r, "serverUri"), ServerName = J.Str(r, "serverName"), Mock = J.True(r, "mock") };
                        if (s.ClientId.Length > 0) return s;
                    }
            }
            catch (Exception) { /* replace a corrupt session */ }
            var created = new Session { ClientId = Guid.NewGuid().ToString() };
            Save(store, created);
            return created;
        }

        public static void Save(IKeyStore store, Session s)
        {
            var sb = new StringBuilder();
            using (var ms = new System.IO.MemoryStream())
            {
                using (var w = new Utf8JsonWriter(ms))
                {
                    w.WriteStartObject();
                    w.WriteString("clientId", s.ClientId); w.WriteString("token", s.Token); w.WriteString("serverUri", s.ServerUri);
                    w.WriteString("serverName", s.ServerName); w.WriteBoolean("mock", s.Mock);
                    w.WriteEndObject();
                }
                try { store.Set(Key, Encoding.UTF8.GetString(ms.ToArray())); } catch (Exception) { }
            }
        }
    }

    /// <summary>Home snapshot cache: the raw /hubs payload plus savedAt, so a warm launch paints Home before the network.</summary>
    public static class HomeCache
    {
        public const string Key = "warp.home.v1";
        public const double MaxAgeMs = 1000 * 60 * 60 * 12;

        public static string Write(string hubsPayload, double savedAt, string serverName) =>
            "{\"savedAt\":" + savedAt.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"serverName\":\"" + JsonEncodedText.Encode(serverName ?? "").ToString() + "\",\"hubs\":" + hubsPayload + "}";

        public static List<Hub> Read(string raw, double maxAgeMs, double now)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            try
            {
                using (var d = JsonDocument.Parse(raw))
                {
                    var r = d.RootElement;
                    var saved = J.Num(r, "savedAt");
                    if (saved == null || !J.Has(r, "hubs", out var hubs)) return null;
                    if (now - saved.Value > maxAgeMs) return null;
                    return PlexParse.Hubs(hubs);
                }
            }
            catch (Exception) { return null; }
        }
    }

    public class PlexClient
    {
        public static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        public readonly string ClientId;
        public string Version = "0.3.0";

        public PlexClient(string clientId) { ClientId = clientId; }

        public Dictionary<string, string> Headers(string token)
        {
            var h = new Dictionary<string, string>
            {
                { "Accept", "application/json" }, { "X-Plex-Product", "Warp" }, { "X-Plex-Version", Version },
                { "X-Plex-Client-Identifier", ClientId }, { "X-Plex-Platform", "Tizen" }, { "X-Plex-Device", "Samsung TV" },
                { "X-Plex-Device-Name", "Warp (native)" }, { "X-Plex-Provides", "player,client" },
            };
            if (!string.IsNullOrEmpty(token)) h["X-Plex-Token"] = token;
            return h;
        }

        public async Task<string> Text(string url, string token, HttpMethod method = null, TimeSpan? timeout = null)
        {
            using (var req = new HttpRequestMessage(method ?? HttpMethod.Get, url))
            {
                foreach (var kv in Headers(token)) req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                using (var cts = new System.Threading.CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10)))
                using (var res = await Http.SendAsync(req, cts.Token).ConfigureAwait(false))
                {
                    if (!res.IsSuccessStatusCode) throw new HttpRequestException($"{req.Method} {Redact(url)} failed: {(int)res.StatusCode}");
                    return await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
        }

        public static string Redact(string url) => System.Text.RegularExpressions.Regex.Replace(url ?? "", "X-Plex-Token=[^&]+", "X-Plex-Token=***");

        async Task<T> Json<T>(string url, string token, Func<JsonElement, T> parse, HttpMethod method = null)
        {
            var text = await Text(url, token, method).ConfigureAwait(false);
            using (var d = JsonDocument.Parse(text)) return parse(d.RootElement);
        }

        /// <summary>4-character PIN for plex.tv/link (strong=false; strong PINs are for the OAuth web flow, not /link).</summary>
        public async Task<PinStart> StartPin()
        {
            var pin = await Json("https://plex.tv/api/v2/pins?strong=false", null, PlexParse.Pin, HttpMethod.Post).ConfigureAwait(false);
            if (pin == null) throw new InvalidOperationException("Plex did not return a PIN");
            return pin;
        }

        public Task<string> PollPin(long id) => Json($"https://plex.tv/api/v2/pins/{id}", null, PlexParse.PinToken);

        public Task<List<PlexServer>> Resources(string token) => Json("https://plex.tv/api/v2/resources?includeHttps=1&includeRelay=1", token, PlexParse.Resources);

        /// <summary>First connection of the first owned server that answers /identity within 3 s.</summary>
        public async Task<(PlexServer server, string uri)> PickServer(string token)
        {
            var servers = await Resources(token).ConfigureAwait(false);
            foreach (var s in servers.OrderBy(x => x.Owned ? 0 : 1))
                foreach (var c in PlexParse.RankConnections(s))
                {
                    try { await Text(c.Uri.TrimEnd('/') + "/identity", token, null, TimeSpan.FromSeconds(3)).ConfigureAwait(false); return (s, c.Uri); }
                    catch (Exception) { }
                }
            return (null, null);
        }

        public static string TimelineUrl(string serverUri, string ratingKey, string state, double timeMs, double durationMs) =>
            $"{serverUri.TrimEnd('/')}/:/timeline?ratingKey={Uri.EscapeDataString(ratingKey)}&key={Uri.EscapeDataString("/library/metadata/" + ratingKey)}&state={state}&time={(long)Math.Max(0, Math.Round(timeMs))}&duration={(long)Math.Max(0, Math.Round(durationMs))}";
    }

    public class PlexSource : ILibrarySource
    {
        readonly PlexClient c; readonly Session s;
        public string LastHubsPayload;
        public PlexSource(PlexClient client, Session session) { c = client; s = session; }
        string B => s.ServerUri.TrimEnd('/');
        public string Name => s.ServerName;
        public bool IsMock => false;

        public async Task<List<Hub>> Hubs()
        {
            var text = await c.Text(B + "/hubs", s.Token).ConfigureAwait(false);
            LastHubsPayload = text;
            using (var d = JsonDocument.Parse(text)) return PlexParse.Hubs(d.RootElement);
        }

        async Task<T> Get<T>(string path, Func<JsonElement, T> parse)
        {
            var text = await c.Text(B + path, s.Token).ConfigureAwait(false);
            using (var d = JsonDocument.Parse(text)) return parse(d.RootElement);
        }

        public Task<Sections> Sections() => Get("/library/sections", PlexParse.ParseSections);

        public async Task<MediaItem> Metadata(string rk)
        {
            var list = await Get($"/library/metadata/{rk}", PlexParse.Metadata).ConfigureAwait(false);
            return list.FirstOrDefault() ?? throw new InvalidOperationException($"metadata {rk} missing");
        }

        public Task<List<MediaItem>> SectionAll(string id, int type) => Get($"/library/sections/{id}/all?type={type}&includeGuids=0", PlexParse.Metadata);
        public Task<List<MediaItem>> Children(string rk) => Get($"/library/metadata/{rk}/children", PlexParse.Metadata);

        public async Task SelectStreams(string partId, string audio, string subs)
        {
            var q = "allParts=1" + (audio != null ? "&audioStreamID=" + Uri.EscapeDataString(audio) : "") + (subs != null ? "&subtitleStreamID=" + Uri.EscapeDataString(subs) : "");
            await c.Text($"{B}/library/parts/{partId}?{q}", s.Token, HttpMethod.Put).ConfigureAwait(false);
        }

        public async Task Timeline(string rk, string state, double t, double dur) =>
            await c.Text(PlexClient.TimelineUrl(B, rk, state, t, dur), s.Token, HttpMethod.Post).ConfigureAwait(false);

        public string Image(string path, int w, int h) => PlexParse.ImageUrl(B, s.Token, path, w, h);
        public string PlaybackUrl(PlaybackDecision d) => Decision.PlaybackUrl(B, s.Token, c.ClientId, d);
    }

    /// <summary>Deterministic fake library (same titles/shape as the web fixture). Art is "mock:poster:seed:title"
    /// or "mock:backdrop:seed", drawn natively by the UI. Mock playback uses Config.MockStreamUrl when set.</summary>
    public class MockSource : ILibrarySource
    {
        static readonly string[] Movies = {
            "Northern Lights", "The Quiet Harbor", "Glass Meridian", "Paper Satellites", "Ember & Ash", "The Long Field",
            "Midnight Cartography", "Saltwater Hymn", "A Room of Echoes", "Tidal", "The Ninth Lantern", "Copperline",
            "Hollow Sun", "The Lighthouse Keeper", "Static Bloom", "Far From Kestrel", "Orchard", "Cold Open",
            "Wildflower Road", "The Archivist", "Blue Hour", "Ironwood", "Second Light", "Parallax" };
        static readonly string[] Shows = { "Low Orbit", "Saltwater", "The Understudy", "Night Shift", "Fathom", "Westbound", "Kingfisher", "Signal & Noise", "Harbor Lights", "The Cartographers", "Driftwood", "Polar" };
        static readonly string[] Genres = { "Drama", "Sci-Fi", "Thriller", "Adventure", "Mystery", "Comedy", "Romance" };
        static readonly string[] Ratings = { "PG", "PG-13", "R", "TV-MA", "TV-14" };
        static readonly string[] EpisodeTitles = { "Pilot", "Undertow", "The Weight of Water", "Lanterns", "Dead Reckoning", "Shoals", "Salt", "Beacon", "Fog Bank", "Landfall" };
        const string Summary = "A cartographer mapping the last uncharted coastline finds a town that appears on no chart and remembers her name. Each tide reveals a little more of what she left behind.";

        public string StreamUrl = "";
        public int DelayMs = 30;
        public readonly List<string> TimelineLog = new List<string>();
        public string Name => "Demo Library";
        public bool IsMock => true;

        public static int Hue(int seed) => (seed * 47) % 360;
        static string Poster(string title, int seed) => $"mock:poster:{seed}:{title}";
        static string Backdrop(int seed) => $"mock:backdrop:{seed}";

        static List<Stream> Streams(int seed) => new List<Stream>
        {
            new Stream { Id = $"{seed}01", StreamType = 1, Codec = seed % 3 != 0 ? "hevc" : "h264", Width = 3840, Height = 2160, Bitrate = 32000, ColorTrc = "smpte2084", DisplayTitle = "4K HDR10" },
            new Stream { Id = $"{seed}11", StreamType = 2, Codec = "truehd", Channels = 8, Selected = true, Language = "en", DisplayTitle = "English (TrueHD 7.1)" },
            new Stream { Id = $"{seed}12", StreamType = 2, Codec = "ac3", Channels = 6, Language = "en", DisplayTitle = "English (AC3 5.1)" },
            new Stream { Id = $"{seed}13", StreamType = 2, Codec = "aac", Channels = 2, Language = "es", DisplayTitle = "Español (AAC Stereo)" },
            new Stream { Id = $"{seed}21", StreamType = 3, Codec = "srt", Language = "en", DisplayTitle = "English (SRT)" },
            new Stream { Id = $"{seed}22", StreamType = 3, Codec = "srt", Language = "en", DisplayTitle = "English SDH (SRT)" },
            new Stream { Id = $"{seed}23", StreamType = 3, Codec = "pgs", Language = "es", DisplayTitle = "Español (PGS)" },
        };

        static List<Part> Parts(int seed, double duration) => new List<Part>
        {
            new Part { Id = $"{seed}1", Label = "4K HEVC HDR10 · 7.1", Key = $"/library/parts/{seed}1/file.mkv", Container = "mkv", Duration = duration, Streams = Streams(seed) },
            new Part { Id = $"{seed}2", Label = "1080p H264 · 5.1", Key = $"/library/parts/{seed}2/file.mp4", Container = "mp4", Duration = duration, Streams = new List<Stream> {
                new Stream { Id = $"{seed}31", StreamType = 1, Codec = "h264", Width = 1920, Height = 1080, Bitrate = 9000 },
                new Stream { Id = $"{seed}32", StreamType = 2, Codec = "aac", Channels = 6, Selected = true, DisplayTitle = "English (AAC 5.1)" } } },
        };

        public static MediaItem Movie(int i, bool withParts = false)
        {
            var title = Movies[i % Movies.Length] + (i >= Movies.Length ? $" {i / Movies.Length + 1}" : "");
            double duration = (88 + (i * 7) % 60) * 60000;
            return new MediaItem
            {
                RatingKey = $"m{i}", Key = $"/library/metadata/m{i}", Type = "movie", Title = title, Year = 2026 - (i % 14), Summary = Summary,
                Thumb = Poster(title, i + 3), Art = Backdrop(i + 3), Duration = duration, ContentRating = Ratings[i % 3],
                AudienceRating = 6.4 + ((i * 3) % 30) / 10.0, Genres = new List<string> { Genres[i % Genres.Length], Genres[(i + 2) % Genres.Length] },
                Tagline = "Some maps lead home.", Parts = withParts ? Parts(i + 3, duration) : new List<Part>(),
            };
        }

        public static MediaItem Show(int i)
        {
            var title = Shows[i % Shows.Length] + (i >= Shows.Length ? $" {i / Shows.Length + 1}" : "");
            return new MediaItem
            {
                RatingKey = $"s{i}", Key = $"/library/metadata/s{i}/children", Type = "show", Title = title, Year = 2019 + (i % 7),
                Summary = "Six strangers crew a salvage tug in the last port at the end of the world, and the sea keeps giving back things it should not have.",
                Thumb = Poster(title, i + 101), Art = Backdrop(i + 101), ContentRating = Ratings[3 + (i % 2)], AudienceRating = 7.1 + (i % 20) / 10.0,
                Genres = new List<string> { Genres[(i + 1) % Genres.Length] }, LeafCount = 30, ViewedLeafCount = 4,
            };
        }

        static MediaItem Season(int show, int n)
        {
            var p = Show(show);
            return new MediaItem { RatingKey = $"s{show}-{n}", Type = "season", Title = $"Season {n}", Index = n, ParentTitle = p.Title, Thumb = p.Thumb, Art = p.Art, LeafCount = 10 };
        }

        static MediaItem Episode(int show, int season, int n, bool withParts = false)
        {
            var p = Show(show);
            int seed = show * 100 + season * 10 + n;
            double duration = 48 * 60000;
            return new MediaItem
            {
                RatingKey = $"s{show}-{season}-{n}", Type = "episode", Title = EpisodeTitles[(n - 1) % EpisodeTitles.Length], GrandparentTitle = p.Title,
                ParentTitle = $"Season {season}", ParentIndex = season, Index = n, Year = p.Year,
                Summary = "The crew hauls up a sealed container that is still warm. Nobody agrees on whether to open it.",
                Thumb = Backdrop(seed + 7), Art = p.Art, GrandparentArt = p.Art, Duration = duration, ContentRating = p.ContentRating,
                ViewCount = season == 1 && n < 4 ? (int?)1 : null, Parts = withParts ? Parts(seed, duration) : new List<Part>(),
            };
        }

        static MediaItem WithProgress(MediaItem m, double f) { var d = m.Duration ?? 3000000; m.Duration = d; m.ViewOffset = Math.Round(d * f); return m; }

        public static List<Hub> FixtureHubs() => new List<Hub>
        {
            new Hub { Identifier = "home.continue", Title = "Continue Watching", Items = new List<MediaItem> {
                WithProgress(Movie(1), 0.42), WithProgress(Episode(0, 1, 4), 0.18), WithProgress(Movie(6), 0.71),
                WithProgress(Episode(2, 2, 3), 0.55), WithProgress(Movie(9), 0.12), WithProgress(Episode(4, 1, 2), 0.88) } },
            new Hub { Identifier = "movie.recentlyadded", Title = "Recently Added Movies", Items = Enumerable.Range(0, 18).Select(i => Movie(i + 2)).ToList() },
            new Hub { Identifier = "tv.recentlyadded", Title = "Recently Added TV", Items = Enumerable.Range(0, 12).Select(Show).ToList() },
            new Hub { Identifier = "movie.toprated", Title = "Top Rated", Items = Enumerable.Range(0, 14).Select(i => Movie(i * 3 + 1)).ToList() },
        };

        async Task<T> Delay<T>(T v) { if (DelayMs > 0) await Task.Delay(DelayMs).ConfigureAwait(false); return v; }

        public Task<List<Hub>> Hubs() => Delay(FixtureHubs());
        public Task<Sections> Sections() => Delay(new Sections { MovieSectionId = "1", ShowSectionId = "2" });
        public Task<List<MediaItem>> SectionAll(string id, int type) =>
            Delay(id == "1" ? Enumerable.Range(0, 1200).Select(i => Movie(i)).ToList() : Enumerable.Range(0, 96).Select(Show).ToList());

        public Task<MediaItem> Metadata(string rk)
        {
            if (rk.StartsWith("m")) return Delay(WithProgress(Movie(int.Parse(rk.Substring(1)), true), rk == "m1" ? 0.42 : 0));
            var bits = rk.Substring(1).Split('-');
            int show = int.Parse(bits[0]);
            if (bits.Length == 3) return Delay(Episode(show, int.Parse(bits[1]), int.Parse(bits[2]), true));
            if (bits.Length == 2) return Delay(Season(show, int.Parse(bits[1])));
            return Delay(Show(show));
        }

        public Task<List<MediaItem>> Children(string rk)
        {
            var bits = rk.Substring(1).Split('-');
            if (bits.Length == 1) return Delay(new[] { 1, 2, 3 }.Select(n => Season(int.Parse(bits[0]), n)).ToList());
            return Delay(Enumerable.Range(1, 10).Select(n => Episode(int.Parse(bits[0]), int.Parse(bits[1]), n)).ToList());
        }

        public Task SelectStreams(string partId, string a, string s) => Delay(true);
        public Task Timeline(string rk, string state, double t, double dur) { lock (TimelineLog) TimelineLog.Add($"{rk} {state} {(long)t}/{(long)dur}"); return Task.CompletedTask; }
        public string Image(string path, int w, int h) => path ?? "";
        public string PlaybackUrl(PlaybackDecision d) => StreamUrl ?? "";
    }
}
