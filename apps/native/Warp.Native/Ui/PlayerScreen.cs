// Player: the TV's own media pipeline (Tizen.Multimedia.Player on the video plane under a transparent NUI window).
// Direct play hands Plex's original file URL to the hardware decoders, so 4K/HDR stays at original quality.
// Pause/resume, seek (-10 s / +30 s), resume offset, and Plex timeline reports (playing every 10 s, paused, stopped).
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Tizen.Multimedia;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Warp.Core;

namespace Warp.Ui
{
    public class PlayerScreen : Screen
    {
        public override string Name => "player";
        readonly MediaItem item;
        readonly PlaybackDecision d;
        readonly string url;
        Player player;
        View overlay, fill;
        TextLabel status, time, left;
        Tizen.NUI.Timer tick;
        int hideIn = 5, reportIn = 10;
        double posMs, durMs;
        bool started, paused, closed;
        readonly Stopwatch sw = Stopwatch.StartNew();

        public PlayerScreen(MediaItem item, PlaybackDecision d, string url) { this.item = item; this.d = d; this.url = url; posMs = d.StartMs; durMs = d.DurationMs; }

        public override void Build()
        {
            var t = A.Theme;
            Tizen.NUI.Window.Instance.BackgroundColor = Color.Transparent;
            A.Layer.BackgroundColor = Color.Transparent;
            overlay = Theme.At(new View(), 0, 0, App.W, App.H);
            Root.Add(overlay);
            var scrim = Theme.At(new View(), 0, App.H - 360, App.W, 360);
            Theme.Gradient(scrim, true, (0f, Theme.Hex(0, 0f)), (1f, Theme.Hex(0, 0.85f)));
            overlay.Add(scrim);
            string title = item.Type == "episode" ? $"{item.GrandparentTitle} · S{item.ParentIndex} E{item.Index} · {item.Title}" : item.Title;
            overlay.Add(Theme.At(Theme.Label(title, t.Px(40), Theme.Text, true), Layout.SafeX, App.H - 250, 1500, t.Px(54)));
            overlay.Add(Theme.At(Theme.Label($"{d.ModeLabel.ToUpperInvariant()} · {d.VideoCodec.ToUpperInvariant()} {d.Resolution} {d.Hdr.ToUpperInvariant()} · {d.AudioCodec.ToUpperInvariant()} · {d.Container}", t.Px(22), t.Accent, true), Layout.SafeX, App.H - 186, 1500, t.Px(32)));
            var track = Theme.At(Theme.Box(Theme.Hex(0xffffff, 0.22f), 4), Layout.SafeX, App.H - 130, App.W - 2 * Layout.SafeX, 8);
            fill = Theme.At(Theme.Box(t.Accent, 4), 0, 0, 0, 8);
            track.Add(fill);
            overlay.Add(track);
            time = Theme.At(Theme.Label("", t.Px(24), Theme.Text), Layout.SafeX, App.H - 108, 600, t.Px(34));
            left = Theme.At(Theme.Label("", t.Px(24), Theme.Muted, false, HorizontalAlignment.End), App.W - Layout.SafeX - 600, App.H - 108, 600, t.Px(34));
            status = Theme.At(Theme.Label("Starting…", t.Px(26), Theme.Meta), Layout.SafeX, App.H - 300, 1700, t.Px(36));
            overlay.Add(time); overlay.Add(left); overlay.Add(status);
            UpdateBar();
            if (!started) { started = true; _ = Start(); }
            tick = new Tizen.NUI.Timer(1000);
            tick.Tick += (s, e) => { Tick(); return !closed; };
            tick.Start();
            Detail = $"mode={d.ModeLabel} url={(url.Length > 0 ? "set" : "none")}";
        }

        async Task Start()
        {
            if (string.IsNullOrEmpty(url))
            {
                status.Text = "Demo library: no stream configured, so this shows the playback decision only.";
                Telemetry.Report("play_skipped", Telemetry.F("reason", "no url (mock without test stream)", "mode", d.ModeLabel));
                return;
            }
            try
            {
                player = new Player();
                player.Display = new Display(Tizen.NUI.Window.Instance);
                player.DisplaySettings.Mode = PlayerDisplayMode.LetterBox;
                player.ErrorOccurred += (s, e) => { Telemetry.Report("player_error", Telemetry.F("error", e.Error.ToString())); status.Text = "Playback error: " + e.Error; ShowOverlay(); };
                player.PlaybackCompleted += (s, e) => { Telemetry.Report("player_completed", null); _ = Report("stopped"); };
                player.BufferingProgressChanged += (s, e) => { if (e.Percent < 100) { status.Text = $"Buffering {e.Percent}%"; } };
                player.SetSource(new MediaUriSource(url));
                var t0 = sw.Elapsed.TotalMilliseconds;
                await player.PrepareAsync();
                var prepared = sw.Elapsed.TotalMilliseconds;
                if (d.StartMs > 1000) await player.SetPlayPositionAsync((int)d.StartMs, false);
                player.Start();
                try { durMs = player.StreamInfo.GetDuration(); } catch (Exception) { }
                status.Text = "";
                Telemetry.Report("player_started", Telemetry.F("prepare_ms", prepared - t0, "start_ms", sw.Elapsed.TotalMilliseconds - t0, "resume_ms", d.StartMs,
                    "duration_ms", durMs, "mode", d.ModeLabel, "video", VideoInfo()));
                _ = Report("playing");
            }
            catch (Exception ex)
            {
                status.Text = "Could not start playback: " + ex.GetType().Name;
                Telemetry.Report("player_start_failed", Telemetry.F("error", PlexClient.Redact(ex.ToString())));
            }
        }

        string VideoInfo()
        {
            try { var v = player.StreamInfo.GetVideoProperties(); return $"{v.Size.Width}x{v.Size.Height}@{v.Fps} {v.BitRate}bps codec={player.StreamInfo.GetVideoCodec()}"; }
            catch (Exception e) { return e.GetType().Name; }
        }

        void Tick()
        {
            if (closed) return;
            if (player != null && player.State == PlayerState.Playing) { try { posMs = player.GetPlayPosition(); } catch (Exception) { } }
            UpdateBar();
            if (hideIn > 0 && --hideIn == 0 && !paused && player != null) overlay.Hide();
            if (!paused && --reportIn <= 0) { reportIn = 10; _ = Report("playing"); }
        }

        void UpdateBar()
        {
            double f = durMs > 0 ? Math.Min(1, posMs / durMs) : 0;
            fill.SizeWidth = (float)((App.W - 2 * Layout.SafeX) * f);
            time.Text = Fmt.Clock(posMs) + (paused ? "  ·  paused" : "");
            left.Text = durMs > 0 ? "-" + Fmt.Clock(durMs - posMs) : "";
        }

        void ShowOverlay() { overlay.Show(); hideIn = 5; }

        async Task Report(string state)
        {
            try
            {
                await A.Source.Timeline(item.RatingKey, state, posMs, durMs);
                Telemetry.Report("timeline", Telemetry.F("state", state, "pos_ms", posMs, "dur_ms", durMs, "mock", A.Source.IsMock));
            }
            catch (Exception ex) { Telemetry.Report("timeline_error", Telemetry.F("state", state, "error", PlexClient.Redact(ex.Message))); }
        }

        public override bool OnKey(string key)
        {
            ShowOverlay();
            switch (key)
            {
                case "enter":
                case "play":
                case "pause":
                    if (player == null) return true;
                    try
                    {
                        if (player.State == PlayerState.Playing && key != "play") { player.Pause(); paused = true; _ = Report("paused"); }
                        else if (player.State == PlayerState.Paused && key != "pause") { player.Start(); paused = false; _ = Report("playing"); }
                    }
                    catch (Exception ex) { Telemetry.Report("player_key_error", Telemetry.F("key", key, "error", ex.Message)); }
                    UpdateBar();
                    return true;
                case "left":
                case "rew":
                case "right":
                case "ff":
                    Seek(key == "left" || key == "rew" ? -10000 : 30000);
                    return true;
                case "stop":
                case "back":
                    Close();
                    A.Pop();
                    return true;
            }
            return true;
        }

        async void Seek(int delta)
        {
            if (player == null) return;
            var target = Math.Max(0, Math.Min(durMs > 0 ? durMs - 1000 : double.MaxValue, posMs + delta));
            posMs = target; UpdateBar();
            var t0 = sw.Elapsed.TotalMilliseconds;
            try { await player.SetPlayPositionAsync((int)target, false); Telemetry.Report("seek", Telemetry.F("to_ms", target, "ms", sw.Elapsed.TotalMilliseconds - t0)); }
            catch (Exception ex) { Telemetry.Report("seek_error", Telemetry.F("error", ex.Message)); }
        }

        void Close()
        {
            if (closed) return;
            closed = true;
            tick?.Stop();
            if (player != null)
            {
                try { posMs = player.GetPlayPosition(); } catch (Exception) { }
                _ = Report("stopped");
                try { if (player.State == PlayerState.Playing || player.State == PlayerState.Paused) player.Stop(); } catch (Exception) { }
                try { player.Unprepare(); } catch (Exception) { }
                try { player.Dispose(); } catch (Exception) { }
                player = null;
            }
            else _ = Report("stopped");
            Tizen.NUI.Window.Instance.BackgroundColor = Theme.Bg;
            A.Layer.BackgroundColor = Theme.Bg;
        }

        public override void OnHide() { if (player != null && player.State == PlayerState.Playing) { try { player.Pause(); paused = true; _ = Report("paused"); } catch (Exception) { } } }
        public override void OnClose() => Close();
    }

    /// <summary>Display-lab test patterns: Left/Right cycles, Up/Down toggles the caption, Back exits.</summary>
    public class PatternsScreen : Screen
    {
        public override string Name => "patterns";
        readonly System.Collections.Generic.List<Pattern> all = Pattern.All();
        int index;
        bool caption = true;

        public override void Build()
        {
            var p = all[index];
            var bg = Theme.At(Theme.Box(Theme.Hex(p.Background)), 0, 0, App.W, App.H);
            Root.Add(bg);
            switch (p.Kind)
            {
                case "steps":
                    float w = App.W / p.Patches.Count;
                    for (int i = 0; i < p.Patches.Count; i++)
                    {
                        Root.Add(Theme.At(Theme.Box(Theme.Hex(p.Patches[i].color)), i * w, 240, (float)Math.Ceiling(w), 600));
                        if (caption) Root.Add(Theme.At(Theme.Label(p.Patches[i].label, 22, Theme.Hex(0x808080), false, HorizontalAlignment.Center), i * w, 860, w, 30));
                    }
                    break;
                case "gradient":
                    Theme.Gradient(bg, false, (0f, Theme.Hex(0)), (1f, Theme.Hex(0xffffff)));
                    break;
                case "checker":
                    // 8 px checkerboard as a 256x256 centre patch (512 views; a full-screen one would be 16k views)
                    for (int y = 0; y < 32; y++)
                        for (int x = y % 2; x < 32; x += 2)
                            Root.Add(Theme.At(Theme.Box(Theme.Hex(0xffffff)), 832 + x * 8, 412 + y * 8, 8, 8));
                    break;
                case "windows":
                    float side = (float)Math.Sqrt(p.Window * App.W * App.H);
                    Root.Add(Theme.At(Theme.Box(Theme.Hex(0xffffff)), (App.W - side) / 2, (App.H - side) / 2, side, side));
                    break;
            }
            if (caption)
            {
                var cap = Theme.At(Theme.Box(Theme.Hex(0, 0.6f), 8), 60, 40, 900, 60);
                cap.Add(Theme.At(Theme.Label($"{index + 1}/{all.Count}  {p.Title}   ◀ ▶ change · ▲▼ hide text · BACK", 24, Theme.Hex(0xffffff)), 20, 0, 880, 60));
                Root.Add(cap);
            }
            Detail = p.Id;
        }

        public override bool OnKey(string key)
        {
            switch (key)
            {
                case "left": index = (index + all.Count - 1) % all.Count; A.Rebuild(this); return true;
                case "right": case "enter": index = (index + 1) % all.Count; A.Rebuild(this); return true;
                case "up": case "down": caption = !caption; A.Rebuild(this); return true;
            }
            return false;
        }
    }
}
