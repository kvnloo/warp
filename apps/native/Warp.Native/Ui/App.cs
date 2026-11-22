// Warp native entry point: window, screen stack, key routing, persistence, instrumentation.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Warp.Core;

namespace Warp.Ui
{
    public abstract class Screen
    {
        public App A;
        public View Root;
        public abstract string Name { get; }
        public int ThemeVersion;
        /// <summary>(Re)build every view from state. Timed and reported as page build ms.</summary>
        public abstract void Build();
        /// <summary>Handle a normalised key (up/down/left/right/enter/back/play/pause/ff/rew). Return false to let App handle Back.</summary>
        public abstract bool OnKey(string key);
        public virtual void OnShow() { if (ThemeVersion != A.Theme.Version) A.Rebuild(this); }
        public virtual void OnHide() { }
        public virtual void OnClose() { }
        public string Detail = "";
    }

    public class App : NUIApplication
    {
        public const float W = 1920, H = 1080;
        public Theme Theme;
        public Prefs Prefs;
        public IKeyStore Store;
        public Session Session;
        public AppConfig Config = new AppConfig();
        public ILibrarySource Source;
        public PlexClient Plex;
        public bool Debug;
        public View Layer;
        readonly List<Screen> stack = new List<Screen>();
        TextLabel hud;
        FrameCounter frames;
        Timer perfTimer;
        readonly Stopwatch clock = Stopwatch.StartNew();
        public readonly Stat KeyToFrame = new Stat(), Handle = new Stat(), Build = new Stat();
        double lastK2f = -1, lastBuild = -1;
        string lastBuildScreen = "";
        int keyCount;

        public Screen Current => stack.Count > 0 ? stack[stack.Count - 1] : null;
        public string DataDir => Tizen.Applications.Application.Current.DirectoryInfo.Data;
        public string ResDir => Tizen.Applications.Application.Current.DirectoryInfo.Resource;

        protected override void OnCreate()
        {
            base.OnCreate();
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Telemetry.Report("unhandled", Telemetry.F("error", e.ExceptionObject?.ToString(), "screen", Current?.Name));
            TaskScheduler.UnobservedTaskException += (s, e) => { Telemetry.Report("task_error", Telemetry.F("error", e.Exception?.ToString())); e.SetObserved(); };
            var t0 = clock.Elapsed.TotalMilliseconds;
            try { Create(); }
            catch (Exception ex) { Telemetry.Report("create_failed", Telemetry.F("error", ex.ToString())); throw; }
            Telemetry.Report("start", Telemetry.F("runtime", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, "env", Environment.Version.ToString(),
                "window", $"{Window.Instance.WindowSize.Width}x{Window.Instance.WindowSize.Height}", "mock", Session.Mock, "linked", Session.Token.Length > 0,
                "create_ms", clock.Elapsed.TotalMilliseconds - t0, "cpus", Environment.ProcessorCount));
        }

        void Create()
        {
            try { Config = AppConfig.Parse(File.ReadAllText(Path.Combine(ResDir, "config.json"))); } catch (Exception) { }
            Store = new FileKeyStore(DataDir);
            Prefs = PrefsLogic.Load(Store);
            Theme = new Theme(Prefs);
            Session = Session.Load(Store);
            Plex = new PlexClient(Session.ClientId);
            try { Debug = File.Exists(Path.Combine(DataDir, "debug.flag")); } catch (Exception) { }
            _ = FindLogEndpoint();

            var win = Window.Instance;
            win.BackgroundColor = Theme.Bg;
            win.KeyEvent += OnKeyEvent;
            Layer = new View { Size = new Size(W, H), BackgroundColor = Theme.Bg, PositionUsesPivotPoint = false };
            win.GetDefaultLayer().Add(Layer);
            hud = Theme.Label("", 20, Theme.Hex(0xffffff, 0.85f), false, HorizontalAlignment.End);
            hud.BackgroundColor = Theme.Hex(0, 0.55f);
            Theme.At(hud, W - 760, H - 46, 740, 34);
            hud.Hide();
            win.GetDefaultLayer().Add(hud);
            frames = new FrameCounter();
            win.AddFrameUpdateCallback(frames);
            perfTimer = new Timer(1000);
            int ticks = 0;
            double peak = 0;
            perfTimer.Tick += (s, e) =>
            {
                var fps = frames.Fps();
                peak = Math.Max(peak, fps);
                if (Debug) { hud.Show(); hud.Text = $"{Current?.Name} · {fps:0} fps · key→frame {lastK2f:0.0} ms (p95 {KeyToFrame.Pct(95):0.0}) · build {lastBuild:0} ms  "; }
                else hud.Hide();
                if (++ticks % 5 == 0) { PerfReport(fps, peak); peak = 0; }
                return true;
            };
            perfTimer.Start();

            bool mock = Session.Mock || Config.ForceMock;
#if WARP_MOCK
            mock = true;
#endif
            if (mock) StartMock();
            else if (Session.Token.Length > 0 && Session.ServerUri.Length > 0) { Source = new PlexSource(Plex, Session); SwitchTab(new HomeScreen()); }
            else SwitchTab(new LoginScreen());
        }

        void PerfReport(double fps, double peak)
        {
            long rss = 0;
            try { foreach (var line in File.ReadAllLines("/proc/self/status")) if (line.StartsWith("VmRSS:")) rss = long.Parse(line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[1]); } catch (Exception) { }
            Telemetry.Report("perf", Telemetry.F("screen", Current?.Name, "fps_now", fps, "fps_peak5s", peak, "keys", keyCount,
                "k2f_last", lastK2f, "k2f_p50", KeyToFrame.Pct(50), "k2f_p95", KeyToFrame.Pct(95), "k2f_max", KeyToFrame.Max,
                "handle_p50", Handle.Pct(50), "handle_p95", Handle.Pct(95), "build_last", lastBuild, "build_screen", lastBuildScreen,
                "build_p50", Build.Pct(50), "build_max", Build.Max, "rss_kb", rss, "gc_kb", GC.GetTotalMemory(false) / 1024));
        }

        async Task FindLogEndpoint()
        {
            string cache = Path.Combine(DataDir, "logserver.txt");
            try { var c = File.ReadAllText(cache).Trim(); if (c.StartsWith("http")) Telemetry.Endpoint = c; } catch (Exception) { }
            if (Telemetry.Endpoint.Length == 0 && Config.LogEndpoint.StartsWith("http")) Telemetry.Endpoint = Config.LogEndpoint;
            var found = await Telemetry.Discover();
            if (found != null)
            {
                Telemetry.Endpoint = found;
                try { File.WriteAllText(cache, found); } catch (Exception) { }
            }
            Telemetry.Flush();
            Telemetry.Report("log_endpoint", Telemetry.F("endpoint", Telemetry.Endpoint, "discovered", found != null));
            // the log server may also hand out a test stream for demo playback (no IPs baked into the app)
            if (Telemetry.Endpoint.Length > 0 && string.IsNullOrEmpty(Config.MockStreamUrl))
            {
                try
                {
                    var text = await PlexClient.Http.GetStringAsync(Telemetry.Endpoint.TrimEnd('/') + "/config");
                    var cfg = AppConfig.Parse(text);
                    if (cfg.MockStreamUrl.Length > 0) { Config.MockStreamUrl = cfg.MockStreamUrl; if (Source is MockSource m) m.StreamUrl = cfg.MockStreamUrl; }
                }
                catch (Exception) { }
            }
        }

        public void StartMock()
        {
            Session.Mock = true;
            Session.Save(Store, Session);
            Source = new MockSource { StreamUrl = Config.MockStreamUrl };
            Telemetry.Report("mode", Telemetry.F("mock", true));
            SwitchTab(new HomeScreen());
        }

        public void SignOut()
        {
            Session.Token = ""; Session.ServerUri = ""; Session.ServerName = ""; Session.Mock = false;
            Session.Save(Store, Session);
            try { Store.Remove(HomeCache.Key); } catch (Exception) { }
            Source = null;
            SwitchTab(new LoginScreen());
        }

        public void SetPrefs(Prefs p, bool rebuildCurrent = true)
        {
            Prefs = p.Clone();
            Theme = new Theme(Prefs);
            PrefsLogic.Save(Store, Prefs);
            if (rebuildCurrent && Current != null) Rebuild(Current);
        }

        public void SetDebug(bool on)
        {
            Debug = on;
            try { var f = Path.Combine(DataDir, "debug.flag"); if (on) File.WriteAllText(f, "1"); else if (File.Exists(f)) File.Delete(f); } catch (Exception) { }
        }

        // ---------- navigation ----------

        public void Push(Screen s)
        {
            var prev = Current;
            if (prev != null) { prev.OnHide(); prev.Root?.Hide(); }
            s.A = this;
            stack.Add(s);
            Rebuild(s);
        }

        public void Pop()
        {
            if (stack.Count <= 1) { Exit(); return; }
            var top = Current;
            stack.RemoveAt(stack.Count - 1);
            top.OnClose();
            Theme.DisposeTree(top.Root);
            top.Root = null;
            Current.Root?.Show();
            Current.OnShow();
            Telemetry.Report("nav", Telemetry.F("to", Current.Name, "from", top.Name));
        }

        /// <summary>Replace the whole stack with a top-level tab (Home, Movies, Shows, Settings, Login).</summary>
        public void SwitchTab(Screen s)
        {
            foreach (var old in stack) { old.OnClose(); Theme.DisposeTree(old.Root); old.Root = null; }
            stack.Clear();
            Push(s);
        }

        public void Rebuild(Screen s)
        {
            var sw = Stopwatch.StartNew();
            if (s.Root != null) Theme.DisposeTree(s.Root);
            s.Root = new View { Size = new Size(W, H), PositionUsesPivotPoint = false };
            Layer.Add(s.Root);
            s.ThemeVersion = Theme.Version;
            try { s.Build(); }
            catch (Exception ex) { Telemetry.Report("build_error", Telemetry.F("screen", s.Name, "error", ex.ToString())); }
            Built(s, sw.Elapsed.TotalMilliseconds);
        }

        /// <summary>Record a page build (full or partial) for the perf receipt.</summary>
        public void Built(Screen s, double ms, string what = "page")
        {
            lastBuild = ms; lastBuildScreen = s.Name; Build.Add(ms);
            Telemetry.Report(what, Telemetry.F("screen", s.Name, "build_ms", ms, "views", Theme.CountViews(s.Root), "detail", s.Detail));
        }

        // ---------- keys ----------

        static string Normalise(string k)
        {
            switch (k)
            {
                case "Up": return "up";
                case "Down": return "down";
                case "Left": return "left";
                case "Right": return "right";
                case "Return": case "KP_Enter": case "Select": return "enter";
                case "XF86Back": case "Escape": case "BackSpace": return "back";
                case "XF86AudioPlay": case "XF86PlayBack": return "play";
                case "XF86AudioPause": return "pause";
                case "XF86AudioStop": return "stop";
                case "XF86AudioForward": return "ff";
                case "XF86AudioRewind": return "rew";
                case "XF86Red": return "red";
                default: return null;
            }
        }

        void OnKeyEvent(object sender, Window.KeyEventArgs e)
        {
            if (e.Key.State != Tizen.NUI.Key.StateType.Down) return;
            long t0 = clock.ElapsedTicks;
            var name = e.Key.KeyPressedName;
            var key = Normalise(name);
            if (key == null) { Telemetry.Report("key_unmapped", Telemetry.F("name", name)); return; }
            if (key == "red") { SetDebug(!Debug); return; }
            var screen = Current?.Name;
            try
            {
                bool handled = Current != null && Current.OnKey(key);
                if (!handled && key == "back") Pop();
            }
            catch (Exception ex) { Telemetry.Report("key_error", Telemetry.F("key", key, "screen", screen, "error", ex.ToString())); }
            double handleMs = (clock.ElapsedTicks - t0) * 1000.0 / Stopwatch.Frequency;
            Handle.Add(handleMs);
            keyCount++;
            var detail = Current?.Detail;
            frames.OnNextFrame = () =>
            {
                double k2f = (clock.ElapsedTicks - t0) * 1000.0 / Stopwatch.Frequency;
                lastK2f = k2f; KeyToFrame.Add(k2f);
                Telemetry.Report("key", Telemetry.F("key", key, "screen", screen, "now", Current?.Name, "handle_ms", handleMs, "k2f_ms", k2f, "detail", detail));
            };
        }

        protected override void OnTerminate()
        {
            Telemetry.Report("terminate", Telemetry.F("screen", Current?.Name));
            foreach (var s in stack) { try { s.OnClose(); } catch (Exception) { } }
            base.OnTerminate();
        }

        protected override void OnPause() { Telemetry.Report("pause", Telemetry.F("screen", Current?.Name)); Current?.OnHide(); base.OnPause(); }
        protected override void OnResume() { Telemetry.Report("resume", Telemetry.F("screen", Current?.Name)); base.OnResume(); }

        static void Main(string[] args) => new App().Run(args);
    }

    /// <summary>Counts frames DALi actually updates (update thread) so fps is real, not a timer's.</summary>
    public class FrameCounter : FrameUpdateCallbackInterface
    {
        readonly Queue<double> stamps = new Queue<double>();
        readonly Stopwatch sw = Stopwatch.StartNew();
        public Action OnNextFrame;
        public override void OnUpdate(float elapsedSeconds)
        {
            lock (stamps) { stamps.Enqueue(sw.Elapsed.TotalSeconds); while (stamps.Count > 240) stamps.Dequeue(); }
            var a = OnNextFrame; OnNextFrame = null; a?.Invoke();
        }
        public double Fps()
        {
            lock (stamps)
            {
                double now = sw.Elapsed.TotalSeconds;
                while (stamps.Count > 0 && now - stamps.Peek() > 1.0) stamps.Dequeue();
                return stamps.Count;
            }
        }
    }
}
