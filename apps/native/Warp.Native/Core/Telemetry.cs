// Instrumentation: the retail TV has no dlog, so the app reports to a small HTTP log endpoint on the LAN
// (tools/tvlog_server.py on groot). The endpoint is found by UDP broadcast ("warp-log?" -> "warp-log http://...")
// with a cached/configured fallback; nothing is hardcoded in code.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Warp.Core
{
    /// <summary>Rolling latency stats (p50/p95/max) for key-to-frame and page-build timings.</summary>
    public class Stat
    {
        readonly List<double> xs = new List<double>();
        readonly int cap;
        public Stat(int cap = 256) { this.cap = cap; }
        public int Count { get { lock (xs) return xs.Count; } }
        public void Add(double v) { lock (xs) { xs.Add(v); if (xs.Count > cap) xs.RemoveAt(0); } }
        public double Pct(double p)
        {
            lock (xs)
            {
                if (xs.Count == 0) return -1;
                var s = xs.OrderBy(x => x).ToList();
                int i = (int)Math.Ceiling(p / 100.0 * s.Count) - 1;
                return s[Math.Max(0, Math.Min(s.Count - 1, i))];
            }
        }
        public double Max { get { lock (xs) return xs.Count == 0 ? -1 : xs.Max(); } }
        public void Clear() { lock (xs) xs.Clear(); }
    }

    public static class Telemetry
    {
        public const int DiscoveryPort = 8797;
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        public static string Endpoint = "";       // e.g. http://192.168.1.25:8796 (discovered or from config)
        public static string Session = Guid.NewGuid().ToString("N").Substring(0, 8);
        public static string Version = "0.3.0";
        static int seq;

        /// <summary>One JSON line: {"ev":..,"sid":..,"seq":..,"t_ms":.., ...fields}.</summary>
        public static string Line(string ev, IDictionary<string, object> fields)
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(ms))
                {
                    w.WriteStartObject();
                    w.WriteString("ev", ev);
                    w.WriteString("sid", Session);
                    w.WriteString("v", Version);
                    w.WriteNumber("seq", System.Threading.Interlocked.Increment(ref seq));
                    w.WriteNumber("t_ms", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    if (fields != null)
                        foreach (var kv in fields)
                        {
                            switch (kv.Value)
                            {
                                case null: w.WriteNull(kv.Key); break;
                                case bool b: w.WriteBoolean(kv.Key, b); break;
                                case int i: w.WriteNumber(kv.Key, i); break;
                                case long l: w.WriteNumber(kv.Key, l); break;
                                case float f: w.WriteNumber(kv.Key, Math.Round(f, 2)); break;
                                case double d: w.WriteNumber(kv.Key, double.IsNaN(d) || double.IsInfinity(d) ? -1 : Math.Round(d, 2)); break;
                                default: var s = kv.Value.ToString(); w.WriteString(kv.Key, s.Length > 4000 ? s.Substring(0, 4000) : s); break;
                            }
                        }
                    w.WriteEndObject();
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static void Report(string ev, IDictionary<string, object> fields = null)
        {
            var line = Line(ev, fields);
            var ep = Endpoint;
            if (string.IsNullOrEmpty(ep)) { lock (pending) { if (pending.Count < 200) pending.Add(line); } return; }
            _ = Send(ep, line);
        }

        static readonly List<string> pending = new List<string>();

        static async Task Send(string ep, string line)
        {
            try { using (var c = new StringContent(line, Encoding.UTF8, "application/json")) using (await Http.PostAsync(ep.TrimEnd('/') + "/log", c).ConfigureAwait(false)) { } }
            catch (Exception) { /* the log endpoint is best-effort */ }
        }

        public static void Flush()
        {
            List<string> lines;
            lock (pending) { lines = new List<string>(pending); pending.Clear(); }
            foreach (var l in lines) _ = Send(Endpoint, l);
        }

        public static Dictionary<string, object> F(params object[] kv)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[kv[i].ToString()] = kv[i + 1];
            return d;
        }

        /// <summary>Broadcast "warp-log?" on UDP and take the first "warp-log http://..." answer.</summary>
        public static async Task<string> Discover(int attempts = 3, int waitMs = 1000)
        {
            try
            {
                using (var u = new UdpClient { EnableBroadcast = true })
                {
                    var msg = Encoding.ASCII.GetBytes("warp-log?");
                    for (int i = 0; i < attempts; i++)
                    {
                        await u.SendAsync(msg, msg.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort)).ConfigureAwait(false);
                        var rx = u.ReceiveAsync();
                        if (await Task.WhenAny(rx, Task.Delay(waitMs)).ConfigureAwait(false) == rx)
                        {
                            var url = ParseAnswer(Encoding.ASCII.GetString(rx.Result.Buffer));
                            if (url != null) return url;
                        }
                    }
                }
            }
            catch (Exception) { }
            return null;
        }

        public static string ParseAnswer(string txt)
        {
            if (txt == null || !txt.StartsWith("warp-log ")) return null;
            var url = txt.Substring(9).Trim();
            return url.StartsWith("http://") || url.StartsWith("https://") ? url : null;
        }
    }

    /// <summary>res/config.json: optional fallbacks (log endpoint, mock stream URL, mock default).</summary>
    public class AppConfig
    {
        public string LogEndpoint = "";
        public string MockStreamUrl = "";
        public bool ForceMock;
        /// <summary>Opt-in key script from the log server's /config (empty = off).</summary>
        public string SelfTest = "";

        public static AppConfig Parse(string raw)
        {
            var c = new AppConfig();
            if (string.IsNullOrEmpty(raw)) return c;
            try
            {
                using (var d = JsonDocument.Parse(raw))
                {
                    c.LogEndpoint = J.Str(d.RootElement, "logEndpoint");
                    c.MockStreamUrl = J.Str(d.RootElement, "mockStreamUrl");
                    c.ForceMock = J.True(d.RootElement, "forceMock");
                    c.SelfTest = J.Str(d.RootElement, "selftest");
                }
            }
            catch (Exception) { }
            return c;
        }
    }

    /// <summary>Display-lab test patterns (port of packages/display-lab/src/patterns.ts), as plain colours.</summary>
    public class Pattern
    {
        public string Id, Title, Kind; // field | steps | gradient | checker | windows
        public int Background;
        public List<(string label, int color)> Patches = new List<(string, int)>();
        public double Window; // windows: area fraction

        public static List<Pattern> All()
        {
            var l = new List<Pattern>();
            var fields = new (string, string, int)[]
            {
                ("field-black", "Full field 0%", 0x000000), ("field-5", "Full field 5%", 0x0d0d0d), ("field-10", "Full field 10%", 0x1a1a1a),
                ("field-25", "Full field 25%", 0x404040), ("field-50", "Full field 50%", 0x808080), ("field-75", "Full field 75%", 0xbfbfbf),
                ("field-100", "Full field 100%", 0xffffff), ("near-black-1", "Near-black 1/255", 0x010101), ("near-black-4", "Near-black 4/255", 0x040404),
                ("near-white-251", "Near-white 251/255", 0xfbfbfb), ("near-white-254", "Near-white 254/255", 0xfefefe),
            };
            foreach (var (id, t, c) in fields) l.Add(new Pattern { Id = id, Title = t, Kind = "field", Background = c });
            var gray = new Pattern { Id = "gray-steps-16", Title = "16 grayscale steps", Kind = "steps" };
            for (int i = 0; i < 16; i++) { int v = (int)Math.Round(i * 255 / 15.0); gray.Patches.Add((v.ToString(), v << 16 | v << 8 | v)); }
            l.Add(gray);
            var rgb = new Pattern { Id = "rgb-cmy", Title = "RGB/CMY patches", Kind = "steps" };
            foreach (var c in new[] { 0xff0000, 0x00ff00, 0x0000ff, 0x00ffff, 0xff00ff, 0xffff00 }) rgb.Patches.Add(("#" + c.ToString("x6"), c));
            l.Add(rgb);
            var sat = new Pattern { Id = "saturation-sweep", Title = "Saturation sweep", Kind = "steps", Background = 0x202020 };
            foreach (var a in new[] { 0, 20, 40, 60, 80, 100 }) sat.Patches.Add(($"{a}%", Hsl(210, a / 100.0, 0.5)));
            l.Add(sat);
            l.Add(new Pattern { Id = "gradient-h", Title = "Horizontal gradient", Kind = "gradient" });
            l.Add(new Pattern { Id = "checker-8", Title = "8px checkerboard", Kind = "checker" });
            l.Add(new Pattern { Id = "windows-10", Title = "Local-dimming 10% window", Kind = "windows", Window = 0.10 });
            l.Add(new Pattern { Id = "windows-25", Title = "Local-dimming 25% window", Kind = "windows", Window = 0.25 });
            return l;
        }

        public static int Hsl(double h, double s, double li)
        {
            double c = (1 - Math.Abs(2 * li - 1)) * s, hp = h / 60.0, x = c * (1 - Math.Abs(hp % 2 - 1)), m = li - c / 2;
            double r = 0, g = 0, b = 0;
            if (hp < 1) { r = c; g = x; } else if (hp < 2) { r = x; g = c; } else if (hp < 3) { g = c; b = x; }
            else if (hp < 4) { g = x; b = c; } else if (hp < 5) { r = x; b = c; } else { r = c; b = x; }
            int R = (int)Math.Round((r + m) * 255), G = (int)Math.Round((g + m) * 255), B = (int)Math.Round((b + m) * 255);
            return R << 16 | G << 8 | B;
        }
    }
}
