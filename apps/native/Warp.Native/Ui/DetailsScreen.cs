// Details: backdrop, metadata chips (incl. the playback decision), Play/Resume, version/audio/subtitle pickers,
// and for shows a season tab row plus an episode rail.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Warp.Core;

namespace Warp.Ui
{
    public class DetailsScreen : Screen
    {
        public override string Name => "details";
        readonly DetailsState s;
        bool autoPlay, loadStarted;
        string zone = "actions"; // actions | seasons | episodes
        int actionCol, seasonCol, episodeCol;
        List<DetailAction> actions = new List<DetailAction>();
        readonly List<Pill> pills = new List<Pill>();
        readonly List<(View pill, TextLabel label, View bar)> seasonTabs = new List<(View, TextLabel, View)>();
        RailRow episodes;
        PlaybackDecision decision;
        // picker overlay
        View picker;
        string pickerKind;
        List<PickOption> pickerOptions;
        int pickerFocus;
        readonly List<(View row, TextLabel label, TextLabel tick)> pickerRows = new List<(View, TextLabel, TextLabel)>();

        public DetailsScreen(MediaItem item, bool autoPlay = false) { s = new DetailsState { Item = item }; this.autoPlay = autoPlay; }

        bool IsShow => s.Item.Type == "show" || s.Item.Type == "season";

        public override void Build()
        {
            if (!loadStarted) { loadStarted = true; _ = Load(); }
            var t = A.Theme;
            var item = s.Item;
            pills.Clear(); seasonTabs.Clear(); pickerRows.Clear(); picker = null; episodes = null;
            Root.Add(Art.Make(A.Source.Image(item.Art ?? item.GrandparentArt ?? item.Thumb, 1920, 1080), App.W, App.H, 0));
            var left = Theme.At(new View(), 0, 0, App.W, App.H);
            Theme.Gradient(left, false, (0f, Theme.Hex(0x0b0d10, 0.96f)), (0.34f, Theme.Hex(0x0b0d10, 0.8f)), (0.64f, Theme.Hex(0x0b0d10, 0.25f)), (1f, Theme.Hex(0x0b0d10, 0f)));
            Root.Add(left);
            var bottom = Theme.At(new View(), 0, 0, App.W, App.H);
            Theme.Gradient(bottom, true, (0f, Theme.Hex(0x0b0d10, 0f)), (0.5f, Theme.Hex(0x0b0d10, 0.1f)), (1f, Theme.Hex(0x0b0d10, 0.95f)));
            Root.Add(bottom);

            float x = Layout.SafeX, y = IsShow ? 170 : 420;
            string over = item.Type == "movie" ? "MOVIE" : IsShow ? "SERIES" : item.Type == "episode" ? $"{item.GrandparentTitle} · S{item.ParentIndex} E{item.Index}".ToUpperInvariant() : item.Type.ToUpperInvariant();
            Root.Add(Theme.At(Theme.Label(over, t.Px(21), t.Accent, true), x, y, 1400, t.Px(30))); y += t.Px(34);
            var title = Theme.Label(item.Title, t.Px(84), Theme.Text, true);
            title.Ellipsis = true;
            Root.Add(Theme.At(title, x, y, 1500, t.Px(104))); y += t.Px(108);
            Root.Add(Theme.At(Theme.Label(DetailsModel.MetaLine(item), t.Px(25), Theme.Meta), x, y, 1400, t.Px(36))); y += t.Px(46);
            if (!IsShow && item.Parts.Count > 0) decision = Decision.Decide(DetailsModel.Playable(s), Engine.Native);
            var tech = decision != null ? DetailsModel.TechChips(DetailsModel.Playable(s), decision) : new List<string>();
            if (tech.Count > 0 || item.Genres?.Count > 0) { Chips.Row(Root, t, x, y, tech, item.Genres); y += t.Px(50); }
            if (!string.IsNullOrEmpty(item.Tagline))
            {
                var tag = Theme.Label(item.Tagline, t.Px(26), Theme.Muted);
                tag.FontStyle = new PropertyMap().Add("slant", new PropertyValue("italic"));
                Root.Add(Theme.At(tag, x, y, 1200, t.Px(36))); y += t.Px(44);
            }
            var sum = Theme.Label(item.Summary ?? "", t.Px(26), Theme.Hex(0xdcd8d0));
            sum.MultiLine = true; sum.Ellipsis = true; sum.VerticalAlignment = VerticalAlignment.Top;
            float sumH = IsShow ? t.Px(80) : t.Px(118);
            Root.Add(Theme.At(sum, x, y, 980, sumH)); y += sumH + 24;

            actions = s.Loading && !IsShow && item.Parts.Count == 0 ? new List<DetailAction>() : DetailsModel.Actions(s);
            float bx = x - 6;
            foreach (var a in actions)
            {
                var p = new Pill(t, a.Id, a.Label, a.Value, a.Id == "play", bx, y, a.Id == "play" ? "▶" : null);
                Root.Add(p.Root); pills.Add(p); bx += p.Width + 16;
            }
            if (actions.Count == 0) Root.Add(Theme.At(Theme.Label("Loading…", t.Px(26), Theme.Muted), x, y, 600, t.Px(76)));
            y += t.Px(76) + 30;
            if (decision != null && A.Debug)
                Root.Add(Theme.At(Theme.Label($"{decision.ModeLabel}: {decision.Reason} · {decision.Container} · {decision.Bitrate:0} kbps", t.Px(20), Theme.Dim), x, y - 24, 1700, 28));

            if (IsShow && s.Seasons.Count > 0)
            {
                float sx = x;
                for (int i = 0; i < s.Seasons.Count; i++)
                {
                    var name = s.Seasons[i].Title;
                    float w = 48 + name.Length * t.Px(26) * 0.55f;
                    var pill = Theme.At(Theme.Box(Color.Transparent, t.R), sx, y, w, t.Px(56));
                    pill.PivotPoint = PivotPoint.Center;
                    var label = Theme.Label(name, t.Px(27), i == s.SeasonIndex ? Theme.Text : Theme.Muted, i == s.SeasonIndex, HorizontalAlignment.Center);
                    pill.Add(Theme.At(label, 0, 0, w, t.Px(56)));
                    var bar = Theme.At(Theme.Box(t.Accent, 2), sx + w / 2 - 14, y + t.Px(56) + 2, 28, 4);
                    if (i != s.SeasonIndex) bar.Hide();
                    Root.Add(pill); Root.Add(bar);
                    seasonTabs.Add((pill, label, bar));
                    sx += w + 12;
                }
                y += t.Px(70);
                if (s.Episodes.Count > 0)
                {
                    var hub = new Hub { Identifier = "episodes", Title = $"{s.Seasons[Math.Min(s.SeasonIndex, s.Seasons.Count - 1)].Title} · {s.Episodes.Count} episodes", Items = s.Episodes };
                    episodes = new RailRow(A, new HomeRail { Kind = "episodes", Hub = hub }, "episodes", Root, y, episodeCol);
                    episodes.Ensure(true);
                }
            }
            ApplyFocus();
            if (autoPlay && !s.Loading) { autoPlay = false; Play(false); }
        }

        async Task Load()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var full = await A.Source.Metadata(s.Item.RatingKey);
                if (s.Item.ViewOffset.HasValue && !full.ViewOffset.HasValue) full.ViewOffset = s.Item.ViewOffset;
                s.Item = full;
                if (IsShow)
                {
                    s.Seasons = full.Type == "season" ? new List<MediaItem> { full } : await A.Source.Children(full.RatingKey);
                    if (s.Seasons.Count > 0) s.Episodes = await A.Source.Children(s.Seasons[0].RatingKey);
                }
                s.Loading = false;
                Telemetry.Report("details_loaded", Telemetry.F("ms", sw.Elapsed.TotalMilliseconds, "type", full.Type, "parts", full.Parts.Count, "seasons", s.Seasons.Count, "episodes", s.Episodes.Count));
            }
            catch (Exception ex)
            {
                s.Loading = false;
                Telemetry.Report("details_error", Telemetry.F("error", PlexClient.Redact(ex.Message)));
            }
            if (Root == null) return;
            if (A.Current == this) A.Rebuild(this); else ThemeVersion = -1;
        }

        async Task LoadSeason(int i)
        {
            s.SeasonIndex = i; episodeCol = 0;
            try { s.Episodes = await A.Source.Children(s.Seasons[i].RatingKey); } catch (Exception) { s.Episodes = new List<MediaItem>(); }
            if (Root != null && A.Current == this) A.Rebuild(this);
        }

        void ApplyFocus()
        {
            for (int i = 0; i < pills.Count; i++) pills[i].SetFocused(zone == "actions" && i == actionCol);
            for (int i = 0; i < seasonTabs.Count; i++)
            {
                bool f = zone == "seasons" && i == seasonCol;
                seasonTabs[i].pill.BackgroundColor = f ? A.Theme.Accent : Color.Transparent;
                seasonTabs[i].label.TextColor = f ? A.Theme.Ink : i == s.SeasonIndex ? Theme.Text : Theme.Muted;
            }
            episodes?.SetFocus(episodeCol, zone == "episodes");
            Detail = $"zone={zone} action={actionCol} season={seasonCol} episode={episodeCol} item={s.Item.Title} mode={decision?.ModeLabel}";
        }

        public override bool OnKey(string key)
        {
            if (picker != null) return PickerKey(key);
            var zones = new List<string> { "actions" };
            if (seasonTabs.Count > 0) zones.Add("seasons");
            if (episodes != null) zones.Add("episodes");
            int zi = zones.IndexOf(zone);
            switch (key)
            {
                case "up": if (zi > 0) { zone = zones[zi - 1]; ApplyFocus(); } return true;
                case "down": if (zi < zones.Count - 1) { zone = zones[zi + 1]; ApplyFocus(); } return true;
                case "left":
                case "right":
                    int d = key == "right" ? 1 : -1;
                    if (zone == "actions") actionCol = Math.Max(0, Math.Min(pills.Count - 1, actionCol + d));
                    else if (zone == "seasons") seasonCol = Math.Max(0, Math.Min(seasonTabs.Count - 1, seasonCol + d));
                    else episodeCol = Math.Max(0, Math.Min(s.Episodes.Count - 1, episodeCol + d));
                    ApplyFocus();
                    return true;
                case "play": Play(false); return true;
                case "enter":
                    if (zone == "seasons") { if (seasonCol != s.SeasonIndex) _ = LoadSeason(seasonCol); return true; }
                    if (zone == "episodes") { if (episodeCol < s.Episodes.Count) A.Push(new DetailsScreen(s.Episodes[episodeCol])); return true; }
                    if (actionCol >= actions.Count) return true;
                    var id = actions[actionCol].Id;
                    if (id == "play") Play(false);
                    else if (id == "restart") Play(true);
                    else OpenPicker(id);
                    return true;
            }
            return false;
        }

        void Play(bool fromStart)
        {
            if (IsShow)
            {
                var next = DetailsModel.NextEpisode(s.Episodes);
                if (next != null) _ = PlayEpisode(next);
                return;
            }
            if (s.Item.Parts.Count == 0) { autoPlay = true; return; }
            var item = DetailsModel.Playable(s);
            if (fromStart) item.ViewOffset = 0;
            var d = Decision.Decide(item, Engine.Native);
            Telemetry.Report("play_decision", Telemetry.F("mode", d.ModeLabel, "reason", d.Reason, "video", d.VideoCodec, "audio", d.AudioCodec, "hdr", d.Hdr, "res", d.Resolution, "container", d.Container, "start_ms", d.StartMs));
            A.Push(new PlayerScreen(item, d, A.Source.PlaybackUrl(d)));
        }

        async Task PlayEpisode(MediaItem ep)
        {
            try
            {
                var full = await A.Source.Metadata(ep.RatingKey);
                if (ep.ViewOffset.HasValue && !full.ViewOffset.HasValue) full.ViewOffset = ep.ViewOffset;
                var d = Decision.Decide(full, Engine.Native);
                Telemetry.Report("play_decision", Telemetry.F("mode", d.ModeLabel, "reason", d.Reason, "video", d.VideoCodec, "audio", d.AudioCodec, "hdr", d.Hdr, "episode", ep.RatingKey));
                if (A.Current == this) A.Push(new PlayerScreen(full, d, A.Source.PlaybackUrl(d)));
            }
            catch (Exception ex) { Telemetry.Report("play_error", Telemetry.F("error", PlexClient.Redact(ex.Message))); }
        }

        // ---------- picker ----------

        void OpenPicker(string kind)
        {
            var t = A.Theme;
            pickerKind = kind;
            pickerOptions = DetailsModel.OptionsFor(s, kind);
            var current = DetailsModel.CurrentChoice(s, kind);
            pickerFocus = Math.Max(0, pickerOptions.FindIndex(o => o.Id == current));
            picker = Theme.At(Theme.Box(Theme.Hex(0, 0.55f)), 0, 0, App.W, App.H);
            var panel = Theme.At(Theme.Box(Theme.Panel, t.R), 1180, 0, 740, App.H);
            picker.Add(panel);
            panel.Add(Theme.At(Theme.Label(kind == "version" ? "Version" : kind == "audio" ? "Audio" : "Subtitles", t.Px(44), Theme.Text, true), 56, 90, 600, t.Px(60)));
            float y = 200;
            pickerRows.Clear();
            for (int i = 0; i < pickerOptions.Count; i++)
            {
                var row = Theme.At(Theme.Box(Color.Transparent, t.Rs), 40, y, 640, t.Px(70));
                row.PivotPoint = PivotPoint.Center;
                var tick = Theme.Label(pickerOptions[i].Id == current ? "✓" : "", t.Px(28), t.Accent, true);
                row.Add(Theme.At(tick, 18, 0, 40, t.Px(70)));
                var label = Theme.Label(pickerOptions[i].Label, t.Px(28), pickerOptions[i].Id == current ? Theme.Text : Theme.Hex(0xd8d4cc));
                label.Ellipsis = true;
                row.Add(Theme.At(label, 64, 0, 560, t.Px(70)));
                panel.Add(row);
                pickerRows.Add((row, label, tick));
                y += t.Px(70) + 8;
            }
            panel.Add(Theme.At(Theme.Label("OK to choose · BACK to close", t.Px(22), Theme.Dim), 56, App.H - 110, 600, 40));
            Root.Add(picker);
            PickerFocus();
            Telemetry.Report("picker", Telemetry.F("kind", kind, "options", pickerOptions.Count));
        }

        void PickerFocus()
        {
            for (int i = 0; i < pickerRows.Count; i++)
            {
                bool f = i == pickerFocus;
                pickerRows[i].row.BackgroundColor = f ? A.Theme.Accent : Color.Transparent;
                pickerRows[i].label.TextColor = f ? A.Theme.Ink : Theme.Hex(0xd8d4cc);
                pickerRows[i].tick.TextColor = f ? A.Theme.Ink : A.Theme.Accent;
            }
            Detail = $"picker={pickerKind} focus={pickerFocus}";
        }

        bool PickerKey(string key)
        {
            switch (key)
            {
                case "up": pickerFocus = Math.Max(0, pickerFocus - 1); PickerFocus(); return true;
                case "down": pickerFocus = Math.Min(pickerOptions.Count - 1, pickerFocus + 1); PickerFocus(); return true;
                case "back": ClosePicker(); return true;
                case "enter":
                    var id = pickerOptions[pickerFocus].Id;
                    if (pickerKind == "version") s.PartIndex = int.Parse(id);
                    else
                    {
                        DetailsModel.WithStream(s, pickerKind, id);
                        var part = DetailsModel.CurrentPart(s);
                        if (part?.Id != null) _ = A.Source.SelectStreams(part.Id, pickerKind == "audio" ? id : null, pickerKind == "subs" ? id : null);
                    }
                    Telemetry.Report("picked", Telemetry.F("kind", pickerKind, "id", id));
                    ClosePicker();
                    A.Rebuild(this);
                    return true;
            }
            return true;
        }

        void ClosePicker() { if (picker != null) { Theme.DisposeTree(picker); picker = null; pickerRows.Clear(); } }
    }
}
