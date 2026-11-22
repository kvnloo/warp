// Screen models as pure functions (ports of home-rails.ts, layout.ts, settings-model.ts, details-model.ts).
// The NUI layer only draws what these return, so every focus/scroll rule is unit-tested off the TV.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Warp.Core
{
    public class HomeRail
    {
        public string Kind;
        public Hub Hub;
    }

    public static class HomeRails
    {
        /// <summary>Map a Plex hub to one of the rail kinds the user can show, hide and reorder.</summary>
        public static string RailKind(Hub hub)
        {
            var id = (hub.Identifier + " " + hub.Title).ToLowerInvariant();
            if (Regex.IsMatch(id, "continue|ondeck|on deck")) return "continue";
            bool recent = id.Contains("recent");
            if (recent && id.Contains("movie")) return "recent-movies";
            if (recent && Regex.IsMatch(id, "tv|show|episode")) return "recent-shows";
            if (recent) return "recent";
            return "more";
        }

        /// <summary>Hubs grouped by kind in the user's rail order; hidden kinds and empty hubs removed.</summary>
        public static List<HomeRail> Order(IList<Hub> hubs, IList<RailPref> rails)
        {
            var outp = new List<HomeRail>();
            foreach (var rail in rails)
            {
                if (!rail.Visible) continue;
                foreach (var hub in hubs)
                    if (hub.Items.Count > 0 && RailKind(hub) == rail.Id) outp.Add(new HomeRail { Kind = rail.Id, Hub = hub });
            }
            return outp;
        }

        /// <summary>The featured title: first in-progress item, else the newest item on Home.</summary>
        public static MediaItem HeroItem(IList<Hub> hubs)
        {
            foreach (var hub in hubs) if (RailKind(hub) == "continue" && hub.Items.Count > 0) return hub.Items[0];
            return hubs.FirstOrDefault(h => h.Items.Count > 0)?.Items[0];
        }
    }

    public struct CardMetrics { public int Width, Height, Gap; public bool Landscape; }

    public struct ScrollWindow { public int Scroll, Start, End; }

    public static class Layout
    {
        public const int SafeX = 96, NavH = 132, HeroH = 720, ContentW = 1920 - 2 * SafeX;

        /// <summary>Continue Watching uses 16:9 thumbnails with progress, the rest 2:3 posters.</summary>
        public static CardMetrics Card(string kind, string posterSize)
        {
            var p = PrefsLogic.Poster(posterSize);
            if (kind != "continue" && kind != "episodes") return new CardMetrics { Width = p.Width, Height = p.Height, Gap = p.Gap };
            int h = (int)Math.Round(p.Height * 0.66);
            return new CardMetrics { Width = (int)Math.Round(h * 16.0 / 9), Height = h, Gap = p.Gap, Landscape = true };
        }

        public static int RailHeight(string kind, Prefs prefs)
        {
            var ts = PrefsLogic.TextScale(prefs.TextSize);
            return (int)Math.Round(34 * ts + 24) + Card(kind, prefs.PosterSize).Height + (int)Math.Round(66 * ts + 18) + 40;
        }

        public static List<int> RailTops(IList<string> kinds, Prefs prefs)
        {
            var tops = new List<int>(); int y = 0;
            foreach (var k in kinds) { tops.Add(y); y += RailHeight(k, prefs); }
            return tops;
        }

        public static int VisibleCards(CardMetrics m) => Math.Max(1, (ContentW + m.Gap) / (m.Width + m.Gap));

        /// <summary>Keep the focused card second from the left once the rail scrolls; build only a few extra.</summary>
        public static ScrollWindow RailWindow(int focusCol, int count, int visible, int overscan = 2)
        {
            int maxScroll = Math.Max(0, count - visible);
            int scroll = Math.Min(maxScroll, Math.Max(0, focusCol - 1));
            return new ScrollWindow { Scroll = scroll, Start = Math.Max(0, scroll - overscan), End = Math.Min(count, scroll + visible + overscan) };
        }

        /// <summary>Library grid: rows built around the focused row only. Start/End are item indices; Scroll is a row.</summary>
        public static ScrollWindow GridWindow(int focusIndex, int count, int cols, int visibleRows, int overscan = 1)
        {
            int rows = (count + cols - 1) / cols;
            int focusRow = Math.Max(0, focusIndex) / cols;
            int maxScroll = Math.Max(0, rows - visibleRows);
            int scrollRow = Math.Min(maxScroll, Math.Max(0, focusRow - 1));
            int startRow = Math.Max(0, scrollRow - overscan);
            int endRow = Math.Min(rows, scrollRow + visibleRows + overscan);
            return new ScrollWindow { Scroll = scrollRow, Start = startRow * cols, End = Math.Min(count, endRow * cols) };
        }

        /// <summary>Home vertical scroll: hero at the top, otherwise the focused rail sits just under the nav.</summary>
        public static int HomeScrollY(int row, IList<int> tops, bool heroOn)
        {
            if (row < 0 || tops.Count == 0) return 0;
            int railsStart = heroOn ? HeroH : NavH + 8;
            return Math.Max(0, railsStart + tops[Math.Min(row, tops.Count - 1)] - NavH - 8);
        }

        /// <summary>Library grid: columns from poster size, widths stretched to span the safe area.</summary>
        public static (CardMetrics metrics, int cols) Grid(string size)
        {
            var b = PrefsLogic.Poster(size);
            int cols = Math.Max(1, (int)Math.Round((ContentW + b.Gap) / (double)(b.Width + b.Gap)));
            int gap = size == "s" ? 24 : 28;
            int w = (ContentW - (cols - 1) * gap) / cols;
            return (new CardMetrics { Width = w, Height = (int)Math.Round(w * 1.5), Gap = gap }, cols);
        }

        public static int GridRowHeight(Prefs prefs) => Grid(prefs.PosterSize).metrics.Height + (int)Math.Round(66 * PrefsLogic.TextScale(prefs.TextSize) + 18) + 28;
    }

    // ---------------- Settings ----------------

    public class Choice
    {
        public string Value, Label; public int? Swatch;
        public Choice(string v, string l, int? sw = null) { Value = v; Label = l; Swatch = sw; }
    }

    public class SettingsRow
    {
        public string Kind;   // option | rail | action
        public string Id, Group, Label, Hint;
        public List<Choice> Choices = new List<Choice>();  // option choices, rail buttons, or action buttons
        public bool Visible; public int Index, Count;     // rail rows
        public int Width => Choices.Count;
    }

    public struct SettingsPos { public int Row, Col; public SettingsPos(int r, int c) { Row = r; Col = c; } }

    public class SettingsResult { public SettingsPos Pos; public Prefs Prefs; public string Action; }

    public static class SettingsModel
    {
        static readonly Dictionary<string, string> SizeLabel = new Dictionary<string, string> { { "s", "Small" }, { "m", "Medium" }, { "l", "Large" } };
        static List<Choice> OnOff() => new List<Choice> { new Choice("on", "On"), new Choice("off", "Off") };
        static string Cap(string s) => char.ToUpperInvariant(s[0]) + s.Substring(1);

        static SettingsRow Opt(string id, string label, string hint, List<Choice> choices) =>
            new SettingsRow { Kind = "option", Id = id, Group = "Appearance", Label = label, Hint = hint, Choices = choices };

        public static readonly string[] RailButtons = { "toggle", "up", "down" };

        public static List<SettingsRow> Build(Prefs prefs, bool debug, bool mock)
        {
            var rows = new List<SettingsRow>
            {
                Opt("accent", "Accent colour", "Buttons, focus and progress.", PrefsLogic.Accents.Select(a => new Choice(a.Id, a.Label, a.Hex)).ToList()),
                Opt("background", "Background", "Backdrop shows the focused title.", PrefsLogic.Backgrounds.Select(v => new Choice(v, Cap(v))).ToList()),
                Opt("posterSize", "Poster size", "Larger posters show fewer per row.", PrefsLogic.PosterSizes.Select(v => new Choice(v, SizeLabel[v])).ToList()),
                Opt("radius", "Corners", "Posters, buttons and panels.", PrefsLogic.Radii.Select(v => new Choice(v, Cap(v))).ToList()),
                Opt("focusStyle", "Focus style", "How the selected poster stands out.", PrefsLogic.FocusStyles.Select(v => new Choice(v, Cap(v))).ToList()),
                Opt("textSize", "Text size", "Scales every label on screen.", PrefsLogic.TextSizes.Select(v => new Choice(v, SizeLabel[v])).ToList()),
                Opt("hero", "Featured title", "Large banner at the top of Home.", OnOff()),
                Opt("clock", "Clock", "Time in the top bar.", OnOff()),
                Opt("reducedMotion", "Reduce motion", "Focus changes snap instead of easing.", OnOff()),
            };
            for (int i = 0; i < prefs.Rails.Count; i++)
            {
                var r = prefs.Rails[i];
                rows.Add(new SettingsRow
                {
                    Kind = "rail", Id = r.Id, Group = "Home rows", Label = PrefsLogic.RailLabels[r.Id], Visible = r.Visible, Index = i, Count = prefs.Rails.Count,
                    Choices = new List<Choice> { new Choice("toggle", r.Visible ? "Shown" : "Hidden"), new Choice("up", "Move up"), new Choice("down", "Move down") },
                });
            }
            rows.Add(new SettingsRow { Kind = "action", Id = "reset", Group = "Home rows", Label = "Reset appearance", Hint = "Restore every appearance and Home row setting.", Choices = new List<Choice> { new Choice("reset", "Reset to defaults") } });
            rows.Add(new SettingsRow { Kind = "action", Id = "account", Group = "Server & account", Label = mock ? "Demo library" : "Plex account", Hint = mock ? "Leave the demo and link a real Plex account." : "Sign out to link a different account or server.", Choices = new List<Choice> { new Choice("sign-out", mock ? "Leave demo" : "Sign out") } });
            rows.Add(new SettingsRow { Kind = "action", Id = "playback", Group = "Playback & display", Label = "Diagnostics", Hint = "Timing overlay and TV calibration patterns.", Choices = new List<Choice> { new Choice("debug", debug ? "Hide timings" : "Show timings"), new Choice("patterns", "Display patterns") } });
            rows.Add(new SettingsRow { Kind = "action", Id = "cache", Group = "Cache", Label = "Home cache", Hint = "Cached Home loads instantly at launch.", Choices = new List<Choice> { new Choice("clear-cache", "Clear cache") } });
            return rows;
        }

        /// <summary>The column focus lands on when entering a row vertically.</summary>
        public static int EntryCol(SettingsRow row, Prefs prefs, int fromCol)
        {
            if (row.Kind == "option")
            {
                var v = prefs.Get(row.Id);
                return Math.Max(0, row.Choices.FindIndex(c => c.Value == v));
            }
            return Math.Min(Math.Max(0, fromCol), row.Width - 1);
        }

        /// <summary>Option rows are segmented controls: Left/Right moves AND applies (live preview). Rail/action rows act on Enter.</summary>
        public static SettingsResult Key(List<SettingsRow> rows, Prefs prefs, SettingsPos pos, string key)
        {
            if (pos.Row < 0 || pos.Row >= rows.Count) return new SettingsResult { Pos = new SettingsPos(0, 0), Prefs = prefs };
            var row = rows[pos.Row];
            if (key == "up" || key == "down")
            {
                int t = pos.Row + (key == "down" ? 1 : -1);
                if (t < 0 || t >= rows.Count) return new SettingsResult { Pos = pos, Prefs = prefs };
                return new SettingsResult { Pos = new SettingsPos(t, EntryCol(rows[t], prefs, pos.Col)), Prefs = prefs };
            }
            if (key == "left" || key == "right")
            {
                int col = Math.Min(row.Width - 1, Math.Max(0, pos.Col + (key == "right" ? 1 : -1)));
                if (row.Kind == "option") return new SettingsResult { Pos = new SettingsPos(pos.Row, col), Prefs = prefs.With(row.Id, row.Choices[col].Value) };
                return new SettingsResult { Pos = new SettingsPos(pos.Row, col), Prefs = prefs };
            }
            // enter
            if (row.Kind == "option")
            {
                var c = pos.Col >= 0 && pos.Col < row.Width ? row.Choices[pos.Col] : row.Choices[0];
                return new SettingsResult { Pos = pos, Prefs = prefs.With(row.Id, c.Value) };
            }
            if (row.Kind == "rail")
            {
                var b = RailButtons[Math.Max(0, Math.Min(2, pos.Col))];
                var p = prefs.Clone();
                if (b == "toggle") { p.Rails = PrefsLogic.ToggleRail(prefs.Rails, row.Id); return new SettingsResult { Pos = pos, Prefs = p }; }
                p.Rails = PrefsLogic.MoveRail(prefs.Rails, row.Id, b == "up" ? -1 : 1);
                int moved = p.Rails.FindIndex(r => r.Id == row.Id) - prefs.Rails.FindIndex(r => r.Id == row.Id);
                // Focus follows the moved rail so repeated Enter keeps moving it.
                return new SettingsResult { Pos = new SettingsPos(pos.Row + moved, pos.Col), Prefs = p };
            }
            return new SettingsResult { Pos = pos, Prefs = prefs, Action = pos.Col >= 0 && pos.Col < row.Width ? row.Choices[pos.Col].Value : null };
        }
    }

    // ---------------- Details ----------------

    public class PickOption { public string Id, Label; public PickOption(string i, string l) { Id = i; Label = l; } }
    public class DetailAction { public string Id, Label, Value; public DetailAction(string i, string l, string v = null) { Id = i; Label = l; Value = v; } }

    public class DetailsState
    {
        public MediaItem Item;
        public bool Loading = true;
        public int PartIndex;
        public List<MediaItem> Seasons = new List<MediaItem>();
        public int SeasonIndex;
        public List<MediaItem> Episodes = new List<MediaItem>();
    }

    public static class DetailsModel
    {
        public static Part CurrentPart(DetailsState s) =>
            s.PartIndex >= 0 && s.PartIndex < s.Item.Parts.Count ? s.Item.Parts[s.PartIndex] : s.Item.Parts.FirstOrDefault();

        static List<Stream> ByType(Part p, int t) => p == null ? new List<Stream>() : p.Streams.Where(x => x.StreamType == t).ToList();

        static string StreamLabel(Stream s)
        {
            if (!string.IsNullOrEmpty(s.DisplayTitle)) return s.DisplayTitle;
            var l = string.Join(" ", new[] { s.Language, s.Codec.ToUpperInvariant() }.Where(x => !string.IsNullOrEmpty(x)));
            return l.Length > 0 ? l : "Unknown";
        }

        public static List<PickOption> AudioOptions(Part p) => ByType(p, 2).Select((s, i) => new PickOption(s.Id ?? i.ToString(), StreamLabel(s))).ToList();

        public static List<PickOption> SubtitleOptions(Part p)
        {
            var l = new List<PickOption> { new PickOption("0", "Off") };
            l.AddRange(ByType(p, 3).Select((s, i) => new PickOption(s.Id ?? "s" + i, StreamLabel(s))));
            return l;
        }

        public static List<PickOption> VersionOptions(MediaItem item) => item.Parts.Select((p, i) => new PickOption(i.ToString(), p.Label ?? $"Version {i + 1}")).ToList();

        public static string SelectedId(Part p, string kind)
        {
            var opts = kind == "audio" ? AudioOptions(p) : SubtitleOptions(p);
            var streams = ByType(p, kind == "audio" ? 2 : 3);
            int i = streams.FindIndex(s => s.Selected);
            if (i < 0) return kind == "audio" ? opts.FirstOrDefault()?.Id ?? "" : "0";
            return kind == "audio" ? opts[i].Id : opts[i + 1].Id;
        }

        public static List<PickOption> OptionsFor(DetailsState s, string kind) =>
            kind == "version" ? VersionOptions(s.Item) : kind == "audio" ? AudioOptions(CurrentPart(s)) : SubtitleOptions(CurrentPart(s));

        public static string CurrentChoice(DetailsState s, string kind) => kind == "version" ? s.PartIndex.ToString() : SelectedId(CurrentPart(s), kind);

        /// <summary>Mark one audio or subtitle stream selected (subtitle "0" clears all) in the current part only.</summary>
        public static void WithStream(DetailsState s, string kind, string id)
        {
            int type = kind == "audio" ? 2 : 3;
            var part = CurrentPart(s);
            if (part == null) return;
            var opts = kind == "audio" ? AudioOptions(part) : SubtitleOptions(part).Skip(1).ToList();
            int n = -1;
            foreach (var st in part.Streams)
            {
                if (st.StreamType != type) continue;
                n++;
                st.Selected = n < opts.Count && opts[n].Id == id;
            }
        }

        public static MediaItem NextEpisode(IList<MediaItem> eps) =>
            eps.FirstOrDefault(e => (e.ViewOffset ?? 0) > 0) ?? eps.FirstOrDefault(e => (e.ViewCount ?? 0) == 0) ?? eps.FirstOrDefault();

        /// <summary>"English (TrueHD 7.1)" -> "English" for the compact button.</summary>
        public static string ShortStream(string label) => label == null ? null : Regex.Replace(label, "\\s*\\(.*\\)\\s*$", "") is string r && r.Length > 0 ? r : label;

        public static List<DetailAction> Actions(DetailsState s)
        {
            var item = s.Item;
            if (item.Type == "show" || item.Type == "season")
            {
                var next = NextEpisode(s.Episodes);
                return new List<DetailAction> { new DetailAction("play", next != null ? $"Play S{next.ParentIndex ?? s.SeasonIndex + 1} E{next.Index ?? 1}" : "Play") };
            }
            bool resume = (item.ViewOffset ?? 0) > 0;
            var a = new List<DetailAction> { new DetailAction("play", resume ? "Resume" : "Play") };
            if (resume) a.Add(new DetailAction("restart", "From start"));
            var part = CurrentPart(s);
            if (item.Parts.Count > 1) a.Add(new DetailAction("version", "Version", (part?.Label ?? "").Split(new[] { " · " }, StringSplitOptions.None)[0]));
            var audio = AudioOptions(part);
            if (audio.Count > 0) a.Add(new DetailAction("audio", "Audio", ShortStream(audio.FirstOrDefault(o => o.Id == SelectedId(part, "audio"))?.Label)));
            if (ByType(part, 3).Count > 0) a.Add(new DetailAction("subs", "Subtitles", ShortStream(SubtitleOptions(part).FirstOrDefault(o => o.Id == SelectedId(part, "subs"))?.Label)));
            return a;
        }

        /// <summary>The item Play starts, with the chosen version first so the playback decision uses it.</summary>
        public static MediaItem Playable(DetailsState s)
        {
            var part = CurrentPart(s);
            if (part == null) return s.Item;
            var m = s.Item.Clone();
            var chosen = m.Parts[s.Item.Parts.IndexOf(part)];
            m.Parts.Remove(chosen);
            m.Parts.Insert(0, chosen);
            return m;
        }

        /// <summary>Metadata chips: resolution, HDR, codec, audio, decision mode, then genres (quiet).</summary>
        public static List<string> TechChips(MediaItem item, PlaybackDecision d)
        {
            var chips = new List<string>();
            if (d == null || item.Parts.Count == 0) return chips;
            var v = item.Parts[0].Streams.FirstOrDefault(x => x.StreamType == 1);
            if (v?.Height >= 2000) chips.Add("4K"); else if (v?.Height >= 1000) chips.Add("1080P"); else if (v?.Height > 0) chips.Add(v.Height + "P");
            if (d.Hdr != "sdr" && d.Hdr != "unknown") chips.Add(d.Hdr.StartsWith("dolby") ? "DOLBY VISION" : d.Hdr.ToUpperInvariant());
            if (d.VideoCodec != "unknown") chips.Add(d.VideoCodec.ToUpperInvariant());
            if (d.AudioCodec != "none") chips.Add(d.AudioCodec.ToUpperInvariant());
            chips.Add(d.Mode == PlaybackMode.DirectPlay ? "DIRECT PLAY" : d.Mode == PlaybackMode.DirectStream ? "DIRECT STREAM" : "TRANSCODE");
            return chips;
        }

        public static string MetaLine(MediaItem i)
        {
            var parts = new List<string>();
            if (i.Year.HasValue) parts.Add(i.Year.ToString());
            if (!string.IsNullOrEmpty(i.ContentRating)) parts.Add(i.ContentRating);
            if (i.Type == "show" && i.LeafCount.HasValue) parts.Add($"{i.LeafCount} episodes");
            else if (i.Duration.HasValue)
            {
                if ((i.ViewOffset ?? 0) > 0) parts.Add(Fmt.Minutes(i.Duration.Value - i.ViewOffset.Value) + " left");
                else parts.Add(Fmt.Minutes(i.Duration.Value));
            }
            if (i.AudienceRating.HasValue) parts.Add("★ " + i.AudienceRating.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            return string.Join(" · ", parts);
        }
    }

    public static class Fmt
    {
        public static string Minutes(double ms)
        {
            int m = (int)Math.Round(ms / 60000);
            return m >= 60 ? $"{m / 60}h {m % 60}m" : $"{m}m";
        }

        public static string Clock(double ms)
        {
            var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
        }

        public static string Sub(MediaItem i)
        {
            if (i.Type == "episode") return $"S{i.ParentIndex} · E{i.Index} {i.Title}";
            if (i.Type == "show") return i.LeafCount.HasValue ? $"{i.LeafCount} episodes" : (i.Year?.ToString() ?? "");
            return i.Year?.ToString() ?? "";
        }

        public static string CardTitle(MediaItem i) => i.Type == "episode" && !string.IsNullOrEmpty(i.GrandparentTitle) ? i.GrandparentTitle : i.Title;
    }
}
