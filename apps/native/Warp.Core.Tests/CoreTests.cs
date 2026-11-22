// Ports of the web app's vitest suites (decision, prefs, home-rails, layout, settings-model, details-model,
// client parsing, patterns) plus native-only pieces (telemetry discovery, session, home cache).
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Warp.Core;
using Xunit;

namespace Warp.Core.Tests
{
    public class DecisionTests
    {
        static MediaItem Item(string codec, string audio = "eac3", string container = "mkv", string hdr = "sdr")
        {
            return new MediaItem
            {
                RatingKey = "42", Key = "/library/metadata/42", Type = "movie", Title = "Fixture", Duration = 6000000, ViewOffset = 12000,
                Parts = new List<Part> { new Part { Key = "/library/parts/9/file", Container = container, Streams = new List<Stream> {
                    new Stream { StreamType = 1, Codec = codec, Width = 3840, Height = 2160, Bitrate = 28000,
                        ColorTrc = hdr == "hdr10" ? "smpte2084" : null, DoviPresent = hdr == "dv5", DoviProfile = hdr == "dv5" ? (int?)5 : null },
                    new Stream { StreamType = 2, Codec = audio, Bitrate = 768 } } } },
            };
        }

        [Fact] public void DirectPlays4kHdr10HevcMkv()
        {
            var d = Decision.Decide(Item("hevc", hdr: "hdr10"), Engine.Native);
            Assert.Equal(PlaybackMode.DirectPlay, d.Mode);
            Assert.Contains("profile matches", d.Reason);
            Assert.Equal("hdr10", d.Hdr);
            Assert.Equal("3840x2160", d.Resolution);
            Assert.Equal(28000, d.Bitrate);
            Assert.Equal(12000, d.StartMs);
        }

        [Fact] public void RemuxesWhenOnlyContainerIsOutsideHtml5() =>
            Assert.Equal(PlaybackMode.DirectStream, Decision.Decide(Item("h264", "aac", "mkv"), Engine.Html5).Mode);

        [Fact] public void TranscodesOnlyForUnsupportedVideo()
        {
            var d = Decision.Decide(Item("theora"), Engine.Native);
            Assert.Equal(PlaybackMode.Transcode, d.Mode);
            Assert.Contains("theora", d.Reason);
        }

        [Fact] public void DolbyVisionProfile5Transcodes() =>
            Assert.Equal(PlaybackMode.Transcode, Decision.Decide(Item("hevc", hdr: "dv5"), Engine.Native).Mode);

        [Fact] public void UnsupportedAudioDirectStreams()
        {
            var d = Decision.Decide(Item("hevc", "atmos-x"), Engine.Native);
            Assert.Equal(PlaybackMode.DirectStream, d.Mode);
            Assert.Contains("audio", d.Reason);
        }

        [Fact] public void TrueHdAndDtsDirectPlayOnTheTv()
        {
            Assert.Equal(PlaybackMode.DirectPlay, Decision.Decide(Item("hevc", "truehd"), Engine.Native).Mode);
            Assert.Equal(PlaybackMode.DirectPlay, Decision.Decide(Item("hevc", "dts"), Engine.Native).Mode);
        }

        [Fact] public void BuildsPartUrlForDirectPlayAndTranscodeUrlOtherwise()
        {
            var direct = Decision.Decide(Item("hevc"), Engine.Native);
            Assert.Contains("/library/parts/9/file?", Decision.PlaybackUrl("http://192.0.2.10:32400", "tok", "cid", direct));
            var tr = Decision.Decide(Item("theora"), Engine.Native);
            var url = Decision.PlaybackUrl("http://192.0.2.10:32400/", "tok", "cid", tr);
            Assert.Contains("directPlay=0", url);
            Assert.Contains("directStream=0", url);
            Assert.DoesNotContain("32400//", url);
        }

        [Fact] public void JudgesSelectedAudioNotFirst()
        {
            var f = Item("h264", "aac", "mp4");
            f.Parts[0].Streams.Add(new Stream { StreamType = 2, Codec = "dts", Selected = true });
            Assert.Equal("dts", Decision.Decide(f, Engine.Html5).AudioCodec);
        }

        [Fact] public void HdrFromTitleAndHlg()
        {
            Assert.Equal("hlg", Decision.HdrOf(new Stream { ColorTrc = "arib-std-b67" }));
            Assert.Equal("hdr10+", Decision.HdrOf(new Stream { DisplayTitle = "4K HDR10+" }));
            Assert.Equal("dolby-vision-with-base", Decision.HdrOf(new Stream { DoviPresent = true, DoviProfile = 8 }));
            Assert.Equal("unknown", Decision.HdrOf(null));
        }

        [Fact] public void EmptyItemIsSafe()
        {
            var d = Decision.Decide(new MediaItem { RatingKey = "1", Title = "x" }, Engine.Native);
            Assert.Equal(PlaybackMode.Transcode, d.Mode);
            Assert.Equal("unknown", d.VideoCodec);
        }
    }

    public class PrefsTests
    {
        [Fact] public void VersionedKeyRoundTripsEveryField()
        {
            Assert.Equal("warp.prefs.v1", PrefsLogic.Key);
            var s = new MemoryKeyStore();
            var p = PrefsLogic.Defaults();
            p.Accent = "teal"; p.PosterSize = "l"; p.Hero = false; p.ReducedMotion = true; p.FocusStyle = "lift";
            p.Rails = PrefsLogic.MoveRail(p.Rails, "recent", -1);
            PrefsLogic.Save(s, p);
            using (var d = JsonDocument.Parse(s.Data[PrefsLogic.Key])) Assert.Equal(1, d.RootElement.GetProperty("v").GetInt32());
            Assert.Equal(p, PrefsLogic.Load(s));
        }

        [Fact] public void DefaultsOnMissingCorruptOrWrongShape()
        {
            var d = PrefsLogic.Defaults();
            Assert.Equal(d, PrefsLogic.Parse(null));
            Assert.Equal(d, PrefsLogic.Parse("{not json"));
            Assert.Equal(d, PrefsLogic.Parse("42"));
            Assert.Equal(d, PrefsLogic.Parse("null"));
            Assert.Equal(d, PrefsLogic.Load(new ThrowingStore()));
        }

        [Fact] public void KeepsValidFieldsWhenOneIsCorrupt()
        {
            var p = PrefsLogic.Parse("{\"v\":1,\"prefs\":{\"accent\":\"nope\",\"posterSize\":\"s\",\"clock\":\"yes\",\"focusStyle\":\"lift\"}}");
            Assert.Equal("gold", p.Accent);
            Assert.True(p.Clock);
            Assert.Equal("s", p.PosterSize);
            Assert.Equal("lift", p.FocusStyle);
        }

        [Fact] public void MigratesBareObject() => Assert.Equal("rose", PrefsLogic.Parse("{\"accent\":\"rose\"}").Accent);

        [Fact] public void NeverThrowsWhenStorageIsFull() => PrefsLogic.Save(new ThrowingStore(), PrefsLogic.Defaults());

        [Fact] public void ResetRemovesKeyAndReturnsFreshDefaults()
        {
            var s = new MemoryKeyStore();
            var p0 = PrefsLogic.Defaults(); p0.Accent = "rose";
            PrefsLogic.Save(s, p0);
            var p = PrefsLogic.Reset(s);
            Assert.False(s.Data.ContainsKey(PrefsLogic.Key));
            Assert.Equal(PrefsLogic.Defaults(), p);
            p.Rails[0].Visible = false;
            Assert.True(PrefsLogic.Defaults().Rails[0].Visible);
        }

        [Fact] public void FileStoreRoundTrips()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "warp-test-" + System.Guid.NewGuid().ToString("N"));
            var s = new FileKeyStore(dir);
            var p = PrefsLogic.Defaults(); p.Accent = "ice"; p.Radius = "round";
            PrefsLogic.Save(s, p);
            PrefsLogic.Save(s, p);
            Assert.Equal(p, PrefsLogic.Load(new FileKeyStore(dir)));
            PrefsLogic.Reset(s);
            Assert.Equal(PrefsLogic.Defaults(), PrefsLogic.Load(s));
            System.IO.Directory.Delete(dir, true);
        }

        [Fact] public void DropsUnknownAndDuplicateRailsAndAppendsMissing()
        {
            var r = PrefsLogic.NormalizeRails(new[] { new RailPref("recent", false), new RailPref("bogus", true), new RailPref("recent", true), null });
            Assert.Equal(new RailPref("recent", false), r[0]);
            Assert.Equal(new[] { "continue", "more", "recent", "recent-movies", "recent-shows" }, r.Select(x => x.Id).OrderBy(x => x, System.StringComparer.Ordinal));
        }

        [Fact] public void MovesAndTogglesWithoutMutating()
        {
            var rails = PrefsLogic.Defaults().Rails;
            var moved = PrefsLogic.MoveRail(rails, "recent-shows", -1);
            Assert.Equal(new[] { "continue", "recent-shows", "recent-movies" }, moved.Take(3).Select(x => x.Id));
            Assert.Equal(rails, PrefsLogic.MoveRail(rails, "continue", -1));
            Assert.False(PrefsLogic.ToggleRail(rails, "continue")[0].Visible);
            Assert.True(rails[0].Visible);
        }

        [Fact] public void AccentInk()
        {
            Assert.True(PrefsLogic.AccentInkDark(0xe8eef5));
            Assert.True(PrefsLogic.AccentInkDark(0xe5a00d));
            Assert.False(PrefsLogic.AccentInkDark(0x1a237e));
            Assert.Equal(7, PrefsLogic.Accents.Length);
        }

        [Fact] public void FewerGridColumnsAsPostersGrow()
        {
            Assert.True(PrefsLogic.GridColumns("s") > PrefsLogic.GridColumns("m"));
            Assert.True(PrefsLogic.GridColumns("m") > PrefsLogic.GridColumns("l"));
            Assert.Equal(6, PrefsLogic.GridColumns("m"));
            Assert.Equal(276, PrefsLogic.Poster("l").Width);
            Assert.Equal(22f, PrefsLogic.RadiusPx("round"));
            Assert.Equal(1.14f, PrefsLogic.TextScale("l"));
        }

        class ThrowingStore : IKeyStore
        {
            public string Get(string key) => throw new System.Exception("denied");
            public void Set(string key, string value) => throw new System.Exception("quota");
            public void Remove(string key) => throw new System.Exception("denied");
        }
    }

    public class ScreenTests
    {
        static Hub H(string id, string title, int n = 2) => new Hub { Identifier = id, Title = title, Items = Enumerable.Range(0, n).Select(i => new MediaItem { RatingKey = id + i, Title = title + i }).ToList() };

        [Fact] public void RailKinds()
        {
            Assert.Equal("continue", HomeRails.RailKind(H("home.continue", "Continue Watching")));
            Assert.Equal("continue", HomeRails.RailKind(H("home.ondeck", "On Deck")));
            Assert.Equal("recent-movies", HomeRails.RailKind(H("movie.recentlyadded", "Recently Added Movies")));
            Assert.Equal("recent-shows", HomeRails.RailKind(H("tv.recentlyadded", "Recently Added TV")));
            Assert.Equal("recent", HomeRails.RailKind(H("home.recent", "Recently Added")));
            Assert.Equal("more", HomeRails.RailKind(H("movie.toprated", "Top Rated")));
        }

        [Fact] public void OrderRailsFollowsPrefsAndDropsHiddenAndEmpty()
        {
            var hubs = new List<Hub> { H("home.continue", "Continue"), H("movie.recentlyadded", "Recent Movies"), H("tv.recentlyadded", "Recent TV", 0), H("x.top", "Top") };
            var rails = PrefsLogic.MoveRail(PrefsLogic.Defaults().Rails, "recent-movies", -1);
            rails = PrefsLogic.ToggleRail(rails, "more");
            var o = HomeRails.Order(hubs, rails);
            Assert.Equal(new[] { "recent-movies", "continue" }, o.Select(r => r.Kind));
        }

        [Fact] public void HeroPrefersContinueWatching()
        {
            var hubs = new List<Hub> { H("movie.recentlyadded", "Recent"), H("home.continue", "Continue") };
            Assert.Equal("home.continue0", HomeRails.HeroItem(hubs).RatingKey);
            Assert.Null(HomeRails.HeroItem(new List<Hub>()));
        }

        [Fact] public void RailWindowKeepsFocusSecondAndOverscans()
        {
            var w = Layout.RailWindow(0, 30, 6);
            Assert.Equal(0, w.Scroll); Assert.Equal(0, w.Start); Assert.Equal(8, w.End);
            w = Layout.RailWindow(10, 30, 6);
            Assert.Equal(9, w.Scroll); Assert.Equal(7, w.Start); Assert.Equal(17, w.End);
            w = Layout.RailWindow(29, 30, 6);
            Assert.Equal(24, w.Scroll); Assert.Equal(30, w.End);
        }

        [Fact] public void GridWindowBuildsOnlyRowsNearFocus()
        {
            var w = Layout.GridWindow(500, 1200, 7, 2);
            Assert.True(w.End - w.Start <= 7 * 4);
            Assert.InRange(500, w.Start, w.End - 1);
        }

        [Fact] public void HomeScrollAndCards()
        {
            var p = PrefsLogic.Defaults();
            var tops = Layout.RailTops(new[] { "continue", "recent-movies" }, p);
            Assert.Equal(0, Layout.HomeScrollY(-1, tops, true));
            Assert.Equal(Layout.HeroH - Layout.NavH - 8, Layout.HomeScrollY(0, tops, true));
            Assert.True(Layout.Card("continue", "m").Landscape);
            Assert.False(Layout.Card("more", "m").Landscape);
            var (m, cols) = Layout.Grid("m");
            Assert.True(cols * m.Width + (cols - 1) * m.Gap <= Layout.ContentW);
        }

        [Fact] public void SettingsLeftRightAppliesLive()
        {
            var p = PrefsLogic.Defaults();
            var rows = SettingsModel.Build(p, false, true);
            var r = SettingsModel.Key(rows, p, new SettingsPos(0, 0), "right");
            Assert.Equal("ember", r.Prefs.Accent);
            Assert.Equal(1, r.Pos.Col);
            r = SettingsModel.Key(rows, r.Prefs, r.Pos, "down");
            Assert.Equal(1, r.Pos.Row);
            Assert.Equal(2, r.Pos.Col); // lands on current value "backdrop"
            r = SettingsModel.Key(rows, r.Prefs, new SettingsPos(6, 0), "right");
            Assert.False(r.Prefs.Hero);
        }

        [Fact] public void SettingsRailMoveFocusFollows()
        {
            var p = PrefsLogic.Defaults();
            var rows = SettingsModel.Build(p, false, false);
            int first = rows.FindIndex(x => x.Kind == "rail");
            var r = SettingsModel.Key(rows, p, new SettingsPos(first, 2), "enter");
            Assert.Equal("recent-movies", r.Prefs.Rails[0].Id);
            Assert.Equal(first + 1, r.Pos.Row);
            r = SettingsModel.Key(rows, p, new SettingsPos(first, 0), "enter");
            Assert.False(r.Prefs.Rails[0].Visible);
            int reset = rows.FindIndex(x => x.Id == "reset");
            Assert.Equal("reset", SettingsModel.Key(rows, p, new SettingsPos(reset, 0), "enter").Action);
            Assert.Equal("patterns", SettingsModel.Key(rows, p, new SettingsPos(rows.FindIndex(x => x.Id == "playback"), 1), "enter").Action);
        }

        [Fact] public void DetailsActionsAndPickers()
        {
            var item = MockSource.Movie(1, true); item.ViewOffset = 1000;
            var s = new DetailsState { Item = item };
            var a = DetailsModel.Actions(s);
            Assert.Equal(new[] { "play", "restart", "version", "audio", "subs" }, a.Select(x => x.Id));
            Assert.Equal("Resume", a[0].Label);
            Assert.Equal("English", a.First(x => x.Id == "audio").Value);
            Assert.Equal("Off", a.First(x => x.Id == "subs").Value);
            DetailsModel.WithStream(s, "subs", DetailsModel.SubtitleOptions(DetailsModel.CurrentPart(s))[2].Id);
            Assert.Equal("English SDH", DetailsModel.Actions(s).First(x => x.Id == "subs").Value);
            s.PartIndex = 1;
            var play = DetailsModel.Playable(s);
            Assert.Equal("mp4", play.Parts[0].Container);
            Assert.Equal("mkv", s.Item.Parts[0].Container);
            Assert.Equal(PlaybackMode.DirectPlay, Decision.Decide(play, Engine.Native).Mode);
        }

        [Fact] public void ShowPlaysNextEpisode()
        {
            var s = new DetailsState { Item = MockSource.Show(0) };
            s.Episodes = Enumerable.Range(1, 5).Select(i => new MediaItem { RatingKey = "e" + i, Title = "E" + i, Index = i, ParentIndex = 1, ViewCount = i < 4 ? (int?)1 : null }).ToList();
            Assert.Equal("Play S1 E4", DetailsModel.Actions(s)[0].Label);
        }

        [Fact] public void MetaLineAndChips()
        {
            var m = MockSource.Movie(5, true);
            Assert.Contains("★", DetailsModel.MetaLine(m));
            var chips = DetailsModel.TechChips(m, Decision.Decide(m, Engine.Native));
            Assert.Equal(new[] { "4K", "HDR10", "HEVC", "TRUEHD", "DIRECT PLAY" }, chips);
            Assert.Equal("2h 0m", Fmt.Minutes(7200000));
            Assert.Equal("1:02:03", Fmt.Clock(3723000));
        }
    }

    public class ParseTests
    {
        static JsonElement P(string s) => JsonDocument.Parse(s).RootElement;

        [Fact] public void ParsesHubsWithMixedNumberTypes()
        {
            var hubs = PlexParse.Hubs(P("{\"MediaContainer\":{\"Hub\":[{\"hubIdentifier\":\"home.continue\",\"title\":\"Continue Watching\",\"Metadata\":[" +
                "{\"ratingKey\":\"7\",\"title\":\"A\",\"year\":\"2020\",\"viewOffset\":1000,\"Genre\":[{\"tag\":\"Drama\"}],\"Media\":[{\"videoResolution\":\"4k\",\"videoCodec\":\"hevc\",\"videoDynamicRange\":\"HDR10\",\"audioChannels\":8," +
                "\"Part\":[{\"id\":11,\"key\":\"/library/parts/11/file.mkv\",\"container\":\"mkv\",\"Stream\":[{\"streamType\":1,\"codec\":\"hevc\",\"width\":3840,\"height\":2160,\"colorTrc\":\"smpte2084\"},{\"streamType\":\"2\",\"codec\":\"truehd\",\"selected\":true}]}]}]}," +
                "{\"title\":\"no key\"}]},{\"title\":\"\"}]}}"));
            Assert.Single(hubs);
            var it = hubs[0].Items.Single();
            Assert.Equal(2020, it.Year);
            Assert.Equal("4K HEVC HDR10 · 7.1", it.Parts[0].Label);
            Assert.Equal("11", it.Parts[0].Id);
            Assert.Equal(2, it.Parts[0].Streams[1].StreamType);
            Assert.True(it.Parts[0].Streams[1].Selected);
            Assert.Equal(new[] { "Drama" }, it.Genres);
            Assert.Equal(PlaybackMode.DirectPlay, Decision.Decide(it, Engine.Native).Mode);
        }

        [Fact] public void PinResourcesSections()
        {
            Assert.Equal("ABCD", PlexParse.Pin(P("{\"id\":123,\"code\":\"ABCD\"}")).Code);
            Assert.Null(PlexParse.Pin(P("{\"id\":0}")));
            Assert.Equal("tok", PlexParse.PinToken(P("{\"authToken\":\"tok\"}")));
            Assert.Equal("", PlexParse.PinToken(P("{\"authToken\":null}")));
            var servers = PlexParse.Resources(P("[{\"name\":\"S\",\"provides\":\"server\",\"owned\":true,\"connections\":[{\"uri\":\"https://relay\",\"relay\":true},{\"uri\":\"https://remote\"},{\"uri\":\"http://192.168.1.5:32400\",\"local\":true}]},{\"name\":\"P\",\"provides\":\"player\"}]"));
            Assert.Single(servers);
            Assert.Equal(new[] { "http://192.168.1.5:32400", "https://remote", "https://relay" }, PlexParse.RankConnections(servers[0]).Select(c => c.Uri));
            var sec = PlexParse.ParseSections(P("{\"MediaContainer\":{\"Directory\":[{\"type\":\"show\",\"key\":\"2\"},{\"type\":\"movie\",\"key\":\"1\"},{\"type\":\"movie\",\"key\":\"9\"}]}}"));
            Assert.Equal("1", sec.MovieSectionId); Assert.Equal("2", sec.ShowSectionId);
        }

        [Fact] public void UrlsAndRedaction()
        {
            Assert.Equal("", PlexParse.ImageUrl("http://s:32400", "t", null, 1, 1));
            Assert.Contains("url=%2Flibrary%2Fmetadata%2F1%2Fthumb", PlexParse.ImageUrl("http://s:32400/", "t", "/library/metadata/1/thumb", 300, 450));
            var t = PlexClient.TimelineUrl("http://s:32400/", "42", "playing", 1234.6, 6000000);
            Assert.Equal("http://s:32400/:/timeline?ratingKey=42&key=%2Flibrary%2Fmetadata%2F42&state=playing&time=1235&duration=6000000", t);
            Assert.Equal("http://x/?a=1&X-Plex-Token=***&b=2", PlexClient.Redact("http://x/?a=1&X-Plex-Token=secret&b=2"));
            var h = new PlexClient("cid").Headers("tok");
            Assert.Equal("cid", h["X-Plex-Client-Identifier"]);
            Assert.Equal("tok", h["X-Plex-Token"]);
        }

        [Fact] public void SessionAndHomeCache()
        {
            var s = new MemoryKeyStore();
            var a = Session.Load(s);
            Assert.NotEmpty(a.ClientId);
            a.Mock = true; a.Token = "t"; Session.Save(s, a);
            var b = Session.Load(s);
            Assert.Equal(a.ClientId, b.ClientId); Assert.True(b.Mock); Assert.Equal("t", b.Token);
            s.Set(Session.Key, "{bad");
            Assert.NotEqual(a.ClientId, Session.Load(s).ClientId);
            var raw = HomeCache.Write("{\"MediaContainer\":{\"Hub\":[{\"title\":\"T\",\"Metadata\":[{\"ratingKey\":\"1\",\"title\":\"x\"}]}]}}", 1000, "Srv \"q\"");
            Assert.Single(HomeCache.Read(raw, 500, 1200));
            Assert.Null(HomeCache.Read(raw, 100, 1200));
            Assert.Null(HomeCache.Read("{", 100, 1200));
        }

        [Fact] public async System.Threading.Tasks.Task MockFixtureShape()
        {
            var m = new MockSource { DelayMs = 0 };
            var hubs = await m.Hubs();
            Assert.Equal(4, hubs.Count);
            Assert.Equal(1200, (await m.SectionAll("1", 1)).Count);
            Assert.Equal(3, (await m.Children("s0")).Count);
            Assert.Equal(10, (await m.Children("s0-1")).Count);
            Assert.Equal("episode", (await m.Metadata("s0-1-4")).Type);
            var m1 = await m.Metadata("m1");
            Assert.Equal(0.42, m1.ViewOffset.Value / m1.Duration.Value, 2);
            Assert.StartsWith("mock:poster:", hubs[1].Items[0].Thumb);
            await m.Timeline("m1", "playing", 10, 20);
            Assert.Single(m.TimelineLog);
        }
    }

    public class TelemetryTests
    {
        [Fact] public void LineIsValidJsonWithFields()
        {
            var line = Telemetry.Line("perf", Telemetry.F("fps", 60.0, "screen", "home", "ok", true, "n", 3, "nan", double.NaN));
            using (var d = JsonDocument.Parse(line))
            {
                Assert.Equal("perf", d.RootElement.GetProperty("ev").GetString());
                Assert.Equal(60, d.RootElement.GetProperty("fps").GetDouble());
                Assert.Equal(-1, d.RootElement.GetProperty("nan").GetDouble());
            }
        }

        [Fact] public void DiscoveryAnswer()
        {
            Assert.Equal("http://192.168.1.25:8796", Telemetry.ParseAnswer("warp-log http://192.168.1.25:8796\n"));
            Assert.Null(Telemetry.ParseAnswer("z0lanes http://x"));
            Assert.Null(Telemetry.ParseAnswer("warp-log ftp://x"));
        }

        [Fact] public void StatPercentiles()
        {
            var s = new Stat();
            foreach (var v in Enumerable.Range(1, 100)) s.Add(v);
            Assert.Equal(50, s.Pct(50)); Assert.Equal(95, s.Pct(95)); Assert.Equal(100, s.Max);
            Assert.Equal(-1, new Stat().Pct(50));
        }

        [Fact] public void ConfigAndPatterns()
        {
            var c = AppConfig.Parse("{\"logEndpoint\":\"http://a:1\",\"forceMock\":true}");
            Assert.Equal("http://a:1", c.LogEndpoint); Assert.True(c.ForceMock);
            Assert.Equal("", AppConfig.Parse("nope").LogEndpoint);
            Assert.Equal("", AppConfig.Parse("{}").SelfTest);
            Assert.Equal("Right wait:500 Return", AppConfig.Parse("{\"selftest\":\"Right wait:500 Return\"}").SelfTest);
            var ps = Pattern.All();
            Assert.Equal(18, ps.Count);
            Assert.Equal(16, ps.First(p => p.Id == "gray-steps-16").Patches.Count);
            Assert.Equal(0x808080, Pattern.Hsl(210, 0, 0.5));
            Assert.Equal(0x0080ff, Pattern.Hsl(210, 1, 0.5));
        }
    }
}
