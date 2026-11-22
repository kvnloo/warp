// Plex JSON -> model (port of the parse* helpers in packages/plex/src/client.ts). Tolerant: Plex mixes numbers
// and numeric strings, and missing fields must never throw. Uses JsonElement only (System.Text.Json 4.6 safe).
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Warp.Core
{
    public static class J
    {
        public static bool Has(JsonElement e, string k, out JsonElement v)
        {
            v = default;
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out v) && v.ValueKind != JsonValueKind.Null && v.ValueKind != JsonValueKind.Undefined;
        }

        public static string Str(JsonElement e, string k)
        {
            if (!Has(e, k, out var v)) return "";
            switch (v.ValueKind)
            {
                case JsonValueKind.String: return v.GetString();
                case JsonValueKind.Number: return v.GetRawText();
                case JsonValueKind.True: return "true";
                case JsonValueKind.False: return "false";
                default: return "";
            }
        }

        public static double? Num(JsonElement e, string k)
        {
            if (!Has(e, k, out var v)) return null;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
            if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) return s;
            return null;
        }

        public static int? Int(JsonElement e, string k) { var n = Num(e, k); return n.HasValue ? (int?)(int)n.Value : null; }

        public static bool True(JsonElement e, string k)
        {
            if (!Has(e, k, out var v)) return false;
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.Number) return v.GetRawText() == "1";
            if (v.ValueKind == JsonValueKind.String) return v.GetString() == "1" || v.GetString() == "true";
            return false;
        }

        public static IEnumerable<JsonElement> Arr(JsonElement e, string k)
        {
            if (Has(e, k, out var v) && v.ValueKind == JsonValueKind.Array)
                foreach (var x in v.EnumerateArray()) yield return x;
        }

        public static JsonElement Obj(JsonElement e, string k) => Has(e, k, out var v) && v.ValueKind == JsonValueKind.Object ? v : default;

        public static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;
    }

    public static class PlexParse
    {
        static Stream StreamOf(JsonElement raw)
        {
            int type = J.Int(raw, "streamType") ?? 0;
            var lang = J.Str(raw, "languageTag");
            if (lang.Length == 0) lang = J.Str(raw, "language");
            var title = J.Str(raw, "displayTitle");
            if (title.Length == 0) title = J.Str(raw, "extendedDisplayTitle");
            int? dovi = J.Int(raw, "doviProfile");
            if (dovi == null || dovi == 0) dovi = J.Int(J.Obj(raw, "DOVI"), "profile");
            return new Stream
            {
                Id = J.NullIfEmpty(J.Str(raw, "id")),
                Selected = J.True(raw, "selected"),
                Language = J.NullIfEmpty(lang),
                StreamType = type == 2 || type == 3 ? type : 1,
                Codec = J.Str(raw, "codec"),
                Bitrate = J.Num(raw, "bitrate"),
                Width = J.Int(raw, "width"),
                Height = J.Int(raw, "height"),
                DisplayTitle = title,
                Channels = J.Int(raw, "channels"),
                ColorTrc = J.Str(raw, "colorTrc"),
                DoviPresent = J.True(raw, "DOVIPresent") || J.True(raw, "doviPresent"),
                DoviProfile = dovi,
            };
        }

        /// <summary>"4K HEVC HDR · 7.1" style label for one Media entry (a version).</summary>
        public static string VersionLabel(JsonElement media)
        {
            var res = J.Str(media, "videoResolution").ToLowerInvariant();
            var resLabel = res == "4k" ? "4K" : Regex.IsMatch(res, "^\\d+$") ? res + "p" : res.ToUpperInvariant();
            var codec = J.Str(media, "videoCodec").ToUpperInvariant();
            var hdr = J.Str(media, "videoDynamicRange");
            var ch = J.Int(media, "audioChannels") ?? 0;
            var head = string.Join(" ", new[] { resLabel, codec, hdr.Length > 0 && hdr != "SDR" ? hdr : "" }).Trim();
            head = Regex.Replace(head, "\\s+", " ");
            var audio = ch > 0 ? (ch >= 8 ? "7.1" : ch >= 6 ? "5.1" : ch == 2 ? "Stereo" : ch + "ch") : "";
            var parts = new List<string>();
            if (head.Length > 0) parts.Add(head);
            if (audio.Length > 0) parts.Add(audio);
            return parts.Count > 0 ? string.Join(" · ", parts) : "Original";
        }

        public static MediaItem Item(JsonElement row)
        {
            if (row.ValueKind != JsonValueKind.Object) return null;
            var ratingKey = J.Str(row, "ratingKey");
            var title = J.Str(row, "title");
            if (ratingKey.Length == 0 || title.Length == 0) return null;
            var item = new MediaItem
            {
                RatingKey = ratingKey,
                Key = J.Str(row, "key").Length > 0 ? J.Str(row, "key") : "/library/metadata/" + ratingKey,
                Type = J.Str(row, "type"),
                Title = title,
                Year = J.Int(row, "year"),
                Summary = J.Str(row, "summary"),
                Thumb = J.Str(row, "thumb"),
                Art = J.Str(row, "art"),
                Duration = J.Num(row, "duration"),
                ViewOffset = J.Num(row, "viewOffset"),
                GrandparentTitle = J.Str(row, "grandparentTitle"),
                ParentTitle = J.Str(row, "parentTitle"),
                Index = J.Int(row, "index"),
                ParentIndex = J.Int(row, "parentIndex"),
                ParentRatingKey = J.NullIfEmpty(J.Str(row, "parentRatingKey")),
                GrandparentRatingKey = J.NullIfEmpty(J.Str(row, "grandparentRatingKey")),
                GrandparentArt = J.NullIfEmpty(J.Str(row, "grandparentArt")),
                ContentRating = J.NullIfEmpty(J.Str(row, "contentRating")),
                AudienceRating = J.Num(row, "audienceRating") ?? J.Num(row, "rating"),
                Tagline = J.NullIfEmpty(J.Str(row, "tagline")),
                LeafCount = J.Int(row, "leafCount"),
                ViewedLeafCount = J.Int(row, "viewedLeafCount"),
                ViewCount = J.Int(row, "viewCount"),
            };
            if (J.Has(row, "Genre", out var g) && g.ValueKind == JsonValueKind.Array)
            {
                item.Genres = new List<string>();
                foreach (var x in g.EnumerateArray()) { var t = J.Str(x, "tag"); if (t.Length > 0) item.Genres.Add(t); }
            }
            foreach (var media in J.Arr(row, "Media"))
            {
                var label = VersionLabel(media);
                foreach (var part in J.Arr(media, "Part"))
                {
                    var p = new Part
                    {
                        Id = J.NullIfEmpty(J.Str(part, "id")),
                        Label = label,
                        Key = J.Str(part, "key"),
                        Container = J.Str(part, "container"),
                        Duration = J.Num(part, "duration"),
                    };
                    foreach (var s in J.Arr(part, "Stream")) p.Streams.Add(StreamOf(s));
                    item.Parts.Add(p);
                }
            }
            return item;
        }

        public static List<MediaItem> Metadata(JsonElement payload)
        {
            var list = new List<MediaItem>();
            foreach (var x in J.Arr(J.Obj(payload, "MediaContainer"), "Metadata")) { var i = Item(x); if (i != null) list.Add(i); }
            return list;
        }

        public static List<Hub> Hubs(JsonElement payload)
        {
            var hubs = new List<Hub>();
            IEnumerable<JsonElement> rows = payload.ValueKind == JsonValueKind.Array ? payload.EnumerateArray() : J.Arr(J.Obj(payload, "MediaContainer"), "Hub");
            foreach (var h in rows)
            {
                var title = J.Str(h, "title");
                if (title.Length == 0) continue;
                var id = J.Str(h, "hubIdentifier");
                if (id.Length == 0) id = J.Str(h, "identifier");
                if (id.Length == 0) id = title;
                var hub = new Hub { Identifier = id, Title = title };
                foreach (var m in J.Arr(h, "Metadata")) { var i = Item(m); if (i != null) hub.Items.Add(i); }
                hubs.Add(hub);
            }
            return hubs;
        }

        public static PinStart Pin(JsonElement payload)
        {
            var id = J.Num(payload, "id");
            var code = J.Str(payload, "code");
            if (id == null || id == 0 || code.Length == 0) return null;
            return new PinStart { Id = (long)id.Value, Code = code };
        }

        public static string PinToken(JsonElement payload) => J.Str(payload, "authToken");

        public static List<PlexServer> Resources(JsonElement payload)
        {
            var list = new List<PlexServer>();
            if (payload.ValueKind != JsonValueKind.Array) return list;
            foreach (var row in payload.EnumerateArray())
            {
                if (System.Array.IndexOf(J.Str(row, "provides").Split(','), "server") < 0) continue;
                var s = new PlexServer { Name = J.Str(row, "name"), ClientIdentifier = J.Str(row, "clientIdentifier"), Owned = J.True(row, "owned") };
                foreach (var c in J.Arr(row, "connections"))
                {
                    var uri = J.Str(c, "uri");
                    if (uri.Length > 0) s.Connections.Add(new ServerConnection { Uri = uri, Local = J.True(c, "local"), Relay = J.True(c, "relay") });
                }
                list.Add(s);
            }
            return list;
        }

        public static Sections ParseSections(JsonElement payload)
        {
            var r = new Sections();
            foreach (var row in J.Arr(J.Obj(payload, "MediaContainer"), "Directory"))
            {
                var type = J.Str(row, "type");
                var key = J.Str(row, "key");
                if (type == "movie" && r.MovieSectionId == null) r.MovieSectionId = key;
                if (type == "show" && r.ShowSectionId == null) r.ShowSectionId = key;
            }
            return r;
        }

        /// <summary>Connection order for a server: local direct first, then remote direct, relay last.</summary>
        public static List<ServerConnection> RankConnections(PlexServer s)
        {
            var list = new List<ServerConnection>(s.Connections);
            list.Sort((a, b) => Score(a).CompareTo(Score(b)));
            return list;
        }

        static int Score(ServerConnection c) => (c.Relay ? 4 : 0) + (c.Local ? 0 : 2) + (c.Uri.StartsWith("https") ? 0 : 1);

        /// <summary>Any-size image through the Plex transcoder (backdrops 16:9, posters 2:3).</summary>
        public static string ImageUrl(string serverUri, string token, string path, int width, int height)
        {
            if (string.IsNullOrEmpty(path)) return "";
            if (path.StartsWith("http")) return path;
            return $"{serverUri.TrimEnd('/')}/photo/:/transcode?width={width}&height={height}&minSize=1&upscale=0&url={System.Uri.EscapeDataString(path)}&X-Plex-Token={System.Uri.EscapeDataString(token)}";
        }
    }
}
