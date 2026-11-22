// Login: Plex PIN link. Shows the 4-character code large; the person types it at plex.tv/link; the app polls
// the PIN until a token appears, then picks the first reachable server connection. "Use demo library" = mock mode.
using System;
using System.Threading.Tasks;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Warp.Core;

namespace Warp.Ui
{
    public class LoginScreen : Screen
    {
        public override string Name => "login";
        PinStart pin;
        string status = "Asking Plex for a code…";
        int col;
        Pill[] buttons;
        Timer poll;
        DateTime pinAt;
        bool busy, closed;

        public override void Build()
        {
            var t = A.Theme;
            var bg = Theme.At(new View(), 0, 0, App.W, App.H);
            Theme.Gradient(bg, true, (0f, Theme.Hex(0x121821)), (0.7f, Theme.Bg), (1f, Theme.Bg));
            Root.Add(bg);
            var glow = Theme.At(new View(), 1100, -500, 1600, 1200);
            Theme.Radial(glow, Theme.Hex(t.AccentHex, 0.22f), Theme.Hex(t.AccentHex, 0f));
            Root.Add(glow);
            var diamond = Theme.At(Theme.Box(t.Accent, 6), 100, 110, 34, 34);
            diamond.PivotPoint = PivotPoint.Center;
            diamond.Orientation = new Rotation(new Radian(new Degree(45)), Vector3.ZAxis);
            Root.Add(diamond);
            Root.Add(Theme.At(Theme.Label("warp", 64, Theme.Text, true), 156, 90, 400, 80));
            Root.Add(Theme.At(Theme.Label("Link your Plex account", t.Px(60), Theme.Text, true), 96, 260, 1400, t.Px(80)));
            Root.Add(Theme.At(Theme.Label("On your phone or computer, open  plex.tv/link  and enter this code:", t.Px(30), Theme.Muted), 96, 260 + t.Px(96), 1600, t.Px(44)));
            var code = Theme.Label(pin != null ? string.Join(" ", pin.Code.ToCharArray()) : "· · · ·", 190, t.Accent, true);
            Root.Add(Theme.At(code, 90, 470, 1200, 240));
            Root.Add(Theme.At(Theme.Label(status, t.Px(28), Theme.Meta), 96, 720, 1600, t.Px(40)));
            buttons = new[]
            {
                new Pill(t, "new", "Get a new code", null, false, 90, 820),
                null,
            };
            buttons[1] = new Pill(t, "demo", "Use demo library", null, false, 90 + buttons[0].Width + 20, 820);
            foreach (var b in buttons) Root.Add(b.Root);
            for (int i = 0; i < buttons.Length; i++) buttons[i].SetFocused(i == col);
            Root.Add(Theme.At(Theme.Label("Warp is a native Tizen app. Video plays through the TV's own decoder at original quality.", t.Px(22), Theme.Dim), 96, 990, 1700, 34));
            Detail = $"pin={(pin != null ? "shown len=" + pin.Code.Length : "none")} col={col} status={status}";
            if (pin == null && !busy) _ = NewPin();
        }

        async Task NewPin()
        {
            busy = true;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                pin = await A.Plex.StartPin();
                pinAt = DateTime.UtcNow;
                status = "Waiting for you to enter the code…";
                Telemetry.Report("pin_shown", Telemetry.F("ms", sw.Elapsed.TotalMilliseconds, "code_len", pin.Code.Length));
                StartPolling();
            }
            catch (Exception ex)
            {
                status = "Could not reach plex.tv: " + ex.GetType().Name + ". Press OK on “Get a new code” to retry.";
                Telemetry.Report("pin_error", Telemetry.F("error", ex.Message));
            }
            busy = false;
            if (!closed && A.Current == this) A.Rebuild(this);
        }

        void StartPolling()
        {
            poll?.Stop();
            poll = new Timer(2000);
            poll.Tick += (s, e) => { _ = Poll(); return !closed; };
            poll.Start();
        }

        bool polling;
        async Task Poll()
        {
            if (polling || pin == null || closed) return;
            if ((DateTime.UtcNow - pinAt).TotalMinutes > 14) { pin = null; poll?.Stop(); _ = NewPin(); return; }
            polling = true;
            try
            {
                var token = await A.Plex.PollPin(pin.Id);
                if (!string.IsNullOrEmpty(token))
                {
                    poll.Stop();
                    status = "Linked. Finding your server…";
                    if (A.Current == this) A.Rebuild(this);
                    Telemetry.Report("pin_linked", null);
                    var (server, uri) = await A.Plex.PickServer(token);
                    if (server == null) { status = "Linked, but no Plex server answered. Check that the server is on."; Telemetry.Report("no_server", null); if (A.Current == this) A.Rebuild(this); return; }
                    A.Session.Token = token; A.Session.ServerUri = uri; A.Session.ServerName = server.Name; A.Session.Mock = false;
                    Session.Save(A.Store, A.Session);
                    A.Source = new PlexSource(A.Plex, A.Session);
                    Telemetry.Report("server", Telemetry.F("name", server.Name, "local", uri.Contains("192.168.") || uri.Contains(".plex.direct")));
                    A.SwitchTab(new HomeScreen());
                }
            }
            catch (Exception ex) { Telemetry.Report("pin_poll_error", Telemetry.F("error", ex.Message)); }
            finally { polling = false; }
        }

        public override bool OnKey(string key)
        {
            switch (key)
            {
                case "left": case "right":
                    col = key == "right" ? 1 : 0;
                    for (int i = 0; i < buttons.Length; i++) buttons[i].SetFocused(i == col);
                    Detail = $"pin={(pin != null ? "shown" : "none")} col={col}";
                    return true;
                case "enter":
                    if (col == 1) { closed = true; poll?.Stop(); A.StartMock(); }
                    else if (!busy) { pin = null; status = "Asking Plex for a code…"; poll?.Stop(); A.Rebuild(this); }
                    return true;
            }
            return false;
        }

        public override void OnClose() { closed = true; poll?.Stop(); }
    }
}
