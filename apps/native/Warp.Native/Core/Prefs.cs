// Appearance preferences (port of apps/tizen/src/prefs.ts): pure data, parsing, migration, metrics.
// Stored as {"v":1,"prefs":{...}} under key warp.prefs.v1 (a JSON file in the app's data dir on the TV).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Warp.Core
{
    public class Accent
    {
        public string Id, Label;
        public int Hex;
        public Accent(string id, string label, int hex) { Id = id; Label = label; Hex = hex; }
        public string Css => "#" + Hex.ToString("x6");
    }

    public class RailPref
    {
        public string Id;
        public bool Visible;
        public RailPref(string id, bool visible) { Id = id; Visible = visible; }
        public RailPref Clone() => new RailPref(Id, Visible);
        public override bool Equals(object o) => o is RailPref r && r.Id == Id && r.Visible == Visible;
        public override int GetHashCode() => (Id ?? "").GetHashCode() ^ (Visible ? 1 : 0);
    }

    public class Prefs
    {
        public string Accent = "gold";
        public string Background = "backdrop";
        public string PosterSize = "m";
        public string Radius = "soft";
        public string FocusStyle = "glow";
        public string TextSize = "m";
        public bool Hero = true;
        public bool Clock = true;
        public bool ReducedMotion = false;
        public List<RailPref> Rails = PrefsLogic.RailIds.Select(id => new RailPref(id, true)).ToList();

        public Prefs Clone()
        {
            var p = (Prefs)MemberwiseClone();
            p.Rails = Rails.Select(r => r.Clone()).ToList();
            return p;
        }

        public override bool Equals(object o)
        {
            return o is Prefs p && p.Accent == Accent && p.Background == Background && p.PosterSize == PosterSize && p.Radius == Radius &&
                   p.FocusStyle == FocusStyle && p.TextSize == TextSize && p.Hero == Hero && p.Clock == Clock && p.ReducedMotion == ReducedMotion &&
                   p.Rails.SequenceEqual(Rails);
        }

        public override int GetHashCode() => (Accent + Background + PosterSize).GetHashCode();

        public string Get(string id)
        {
            switch (id)
            {
                case "accent": return Accent;
                case "background": return Background;
                case "posterSize": return PosterSize;
                case "radius": return Radius;
                case "focusStyle": return FocusStyle;
                case "textSize": return TextSize;
                case "hero": return Hero ? "on" : "off";
                case "clock": return Clock ? "on" : "off";
                case "reducedMotion": return ReducedMotion ? "on" : "off";
                default: return "";
            }
        }

        /// <summary>Copy with one option row changed (booleans take "on"/"off").</summary>
        public Prefs With(string id, string value)
        {
            var p = Clone();
            switch (id)
            {
                case "accent": p.Accent = value; break;
                case "background": p.Background = value; break;
                case "posterSize": p.PosterSize = value; break;
                case "radius": p.Radius = value; break;
                case "focusStyle": p.FocusStyle = value; break;
                case "textSize": p.TextSize = value; break;
                case "hero": p.Hero = value == "on"; break;
                case "clock": p.Clock = value == "on"; break;
                case "reducedMotion": p.ReducedMotion = value == "on"; break;
            }
            return PrefsLogic.Normalize(p);
        }
    }

    /// <summary>Key/value store shaped like localStorage so the web tests port 1:1.</summary>
    public interface IKeyStore
    {
        string Get(string key);
        void Set(string key, string value);
        void Remove(string key);
    }

    /// <summary>One file per key in a directory (the app's data dir on the TV). Writes are atomic (tmp + move).</summary>
    public class FileKeyStore : IKeyStore
    {
        readonly string dir;
        public FileKeyStore(string dir) { this.dir = dir; }
        string PathOf(string key) => System.IO.Path.Combine(dir, key + ".json");
        public string Get(string key) { var p = PathOf(key); return File.Exists(p) ? File.ReadAllText(p) : null; }
        public void Set(string key, string value)
        {
            Directory.CreateDirectory(dir);
            var tmp = PathOf(key) + ".tmp";
            File.WriteAllText(tmp, value);
            if (File.Exists(PathOf(key))) File.Delete(PathOf(key));
            File.Move(tmp, PathOf(key));
        }
        public void Remove(string key) { var p = PathOf(key); if (File.Exists(p)) File.Delete(p); }
    }

    public class MemoryKeyStore : IKeyStore
    {
        public readonly Dictionary<string, string> Data = new Dictionary<string, string>();
        public string Get(string key) => Data.TryGetValue(key, out var v) ? v : null;
        public void Set(string key, string value) => Data[key] = value;
        public void Remove(string key) => Data.Remove(key);
    }

    public struct PosterMetrics { public int Width, Height, Gap; }

    public static class PrefsLogic
    {
        public const string Key = "warp.prefs.v1";
        public const int Version = 1;

        public static readonly Accent[] Accents =
        {
            new Accent("gold", "Gold", 0xe5a00d), new Accent("ember", "Ember", 0xff6b3d), new Accent("rose", "Rose", 0xff5d7a),
            new Accent("teal", "Teal", 0x2ec4b6), new Accent("sky", "Sky", 0x4aa8ff), new Accent("lime", "Lime", 0xa3d936),
            new Accent("ice", "Ice", 0xe8eef5),
        };

        public static readonly string[] RailIds = { "continue", "recent-movies", "recent-shows", "recent", "more" };
        public static readonly string[] Backgrounds = { "solid", "gradient", "backdrop" };
        public static readonly string[] PosterSizes = { "s", "m", "l" };
        public static readonly string[] Radii = { "sharp", "soft", "round" };
        public static readonly string[] FocusStyles = { "ring", "glow", "lift" };
        public static readonly string[] TextSizes = { "s", "m", "l" };

        public static readonly Dictionary<string, string> RailLabels = new Dictionary<string, string>
        {
            { "continue", "Continue Watching" }, { "recent-movies", "Recently Added Movies" }, { "recent-shows", "Recently Added Shows" },
            { "recent", "Recently Added" }, { "more", "More from your server" },
        };

        public static Prefs Defaults() => new Prefs();

        public static Accent AccentOf(string id) => Accents.FirstOrDefault(a => a.Id == id) ?? Accents[0];

        static string Pick(string v, string[] allowed, string fallback) => v != null && allowed.Contains(v) ? v : fallback;

        /// <summary>Keep known rails in stored order, drop unknown or duplicate ids, append rails added since.</summary>
        public static List<RailPref> NormalizeRails(IEnumerable<RailPref> rails)
        {
            var outp = new List<RailPref>();
            var seen = new HashSet<string>();
            if (rails != null)
                foreach (var r in rails)
                {
                    if (r == null || r.Id == null || !RailIds.Contains(r.Id) || seen.Contains(r.Id)) continue;
                    seen.Add(r.Id);
                    outp.Add(new RailPref(r.Id, r.Visible));
                }
            foreach (var id in RailIds) if (!seen.Contains(id)) outp.Add(new RailPref(id, true));
            return outp;
        }

        public static Prefs Normalize(Prefs p)
        {
            var d = Defaults();
            return new Prefs
            {
                Accent = Pick(p.Accent, Accents.Select(a => a.Id).ToArray(), d.Accent),
                Background = Pick(p.Background, Backgrounds, d.Background),
                PosterSize = Pick(p.PosterSize, PosterSizes, d.PosterSize),
                Radius = Pick(p.Radius, Radii, d.Radius),
                FocusStyle = Pick(p.FocusStyle, FocusStyles, d.FocusStyle),
                TextSize = Pick(p.TextSize, TextSizes, d.TextSize),
                Hero = p.Hero, Clock = p.Clock, ReducedMotion = p.ReducedMotion,
                Rails = NormalizeRails(p.Rails),
            };
        }

        /// <summary>Field-by-field merge from JSON: one bad field never throws away the rest.</summary>
        public static Prefs FromJson(JsonElement raw)
        {
            var d = Defaults();
            if (raw.ValueKind != JsonValueKind.Object) return d;
            string S(string k) => raw.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            bool B(string k, bool f) => raw.TryGetProperty(k, out var v) ? (v.ValueKind == JsonValueKind.True ? true : v.ValueKind == JsonValueKind.False ? false : f) : f;
            var rails = new List<RailPref>();
            if (raw.TryGetProperty("rails", out var ra) && ra.ValueKind == JsonValueKind.Array)
                foreach (var r in ra.EnumerateArray())
                {
                    if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String) continue;
                    bool vis = !(r.TryGetProperty("visible", out var vv) && vv.ValueKind == JsonValueKind.False);
                    rails.Add(new RailPref(id.GetString(), vis));
                }
            return Normalize(new Prefs
            {
                Accent = S("accent"), Background = S("background"), PosterSize = S("posterSize"), Radius = S("radius"),
                FocusStyle = S("focusStyle"), TextSize = S("textSize"),
                Hero = B("hero", d.Hero), Clock = B("clock", d.Clock), ReducedMotion = B("reducedMotion", d.ReducedMotion),
                Rails = rails,
            });
        }

        /// <summary>Parse the stored string. Corrupt JSON or a wrong shape falls back to defaults.</summary>
        public static Prefs Parse(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return Defaults();
            try
            {
                using (var doc = JsonDocument.Parse(raw))
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return Defaults();
                    // v1 envelope {v:1, prefs:{...}}; a bare object (pre-envelope) is migrated field by field.
                    var body = root.TryGetProperty("prefs", out var p) ? p : root;
                    return FromJson(body);
                }
            }
            catch (Exception) { return Defaults(); }
        }

        public static string Serialize(Prefs prefs)
        {
            var p = Normalize(prefs);
            using (var ms = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(ms))
                {
                    w.WriteStartObject();
                    w.WriteNumber("v", Version);
                    w.WriteStartObject("prefs");
                    w.WriteString("accent", p.Accent); w.WriteString("background", p.Background); w.WriteString("posterSize", p.PosterSize);
                    w.WriteString("radius", p.Radius); w.WriteString("focusStyle", p.FocusStyle); w.WriteString("textSize", p.TextSize);
                    w.WriteBoolean("hero", p.Hero); w.WriteBoolean("clock", p.Clock); w.WriteBoolean("reducedMotion", p.ReducedMotion);
                    w.WriteStartArray("rails");
                    foreach (var r in p.Rails) { w.WriteStartObject(); w.WriteString("id", r.Id); w.WriteBoolean("visible", r.Visible); w.WriteEndObject(); }
                    w.WriteEndArray();
                    w.WriteEndObject();
                    w.WriteEndObject();
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static Prefs Load(IKeyStore s) { try { return Parse(s.Get(Key)); } catch (Exception) { return Defaults(); } }
        public static void Save(IKeyStore s, Prefs p) { try { s.Set(Key, Serialize(p)); } catch (Exception) { /* disk full: keep in-memory prefs */ } }
        public static Prefs Reset(IKeyStore s) { try { s.Remove(Key); } catch (Exception) { } return Defaults(); }

        /// <summary>Move a rail up (-1) or down (+1). Out-of-range moves return the same order.</summary>
        public static List<RailPref> MoveRail(IList<RailPref> rails, string id, int delta)
        {
            var next = rails.Select(r => r.Clone()).ToList();
            int i = next.FindIndex(r => r.Id == id), t = i + delta;
            if (i < 0 || t < 0 || t >= next.Count) return next;
            var tmp = next[i]; next[i] = next[t]; next[t] = tmp;
            return next;
        }

        public static List<RailPref> ToggleRail(IList<RailPref> rails, string id) =>
            rails.Select(r => r.Id == id ? new RailPref(r.Id, !r.Visible) : r.Clone()).ToList();

        public static PosterMetrics Poster(string size)
        {
            int w = size == "s" ? 188 : size == "l" ? 276 : 228;
            return new PosterMetrics { Width = w, Height = (int)Math.Round(w * 1.5), Gap = size == "s" ? 24 : 32 };
        }

        /// <summary>Columns that fit the 1920 px layout inside a 96 px safe area on each side.</summary>
        public static int GridColumns(string size, int contentWidth = 1920 - 2 * 96)
        {
            var m = Poster(size);
            return Math.Max(1, (contentWidth + m.Gap) / (m.Width + m.Gap));
        }

        /// <summary>Readable ink for text on the accent: true = dark ink (#0b0d10), false = white.</summary>
        public static bool AccentInkDark(int hex)
        {
            double Lin(int c) { var s = c / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            var l = 0.2126 * Lin((hex >> 16) & 255) + 0.7152 * Lin((hex >> 8) & 255) + 0.0722 * Lin(hex & 255);
            return l > 0.35;
        }

        public static int AccentInk(int hex) => AccentInkDark(hex) ? 0x0b0d10 : 0xffffff;
        public static float RadiusPx(string r) => r == "sharp" ? 2 : r == "round" ? 22 : 12;
        public static float TextScale(string t) => t == "s" ? 0.9f : t == "l" ? 1.14f : 1f;
        public static int MotionMs(Prefs p) => p.ReducedMotion ? 0 : 160;
    }
}
