// Shared widgets: nav bar, poster/thumbnail card with the three focus styles, pill buttons, chips.
using System;
using System.Collections.Generic;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Warp.Core;

namespace Warp.Ui
{
    public class NavBar
    {
        public static readonly string[] Tabs = { "Home", "Movies", "Shows", "Settings" };
        public View Root;
        readonly List<(View pill, TextLabel label, View bar)> tabs = new List<(View, TextLabel, View)>();
        readonly Theme t;
        readonly int current;
        TextLabel clock;
        Timer clockTimer;
        public int Focus = -1;

        public NavBar(App a, int current, View parent)
        {
            t = a.Theme; this.current = current;
            Root = Theme.At(new View(), 0, 0, App.W, Layout.NavH);
            Theme.Gradient(Root, true, (0f, Theme.Hex(0x0b0d10, 0.96f)), (0.55f, Theme.Hex(0x0b0d10, 0.78f)), (1f, Theme.Hex(0x0b0d10, 0f)));
            parent.Add(Root);
            var diamond = Theme.At(Theme.Box(t.Accent, 4), 98, 57, 22, 22);
            diamond.PivotPoint = PivotPoint.Center;
            diamond.Orientation = new Rotation(new Radian(new Degree(45)), Vector3.ZAxis);
            Root.Add(diamond);
            Root.Add(Theme.At(Theme.Label("warp", 40, Theme.Text, true), 132, 36, 140, 60));
            float x = 326;
            for (int i = 0; i < Tabs.Length; i++)
            {
                float w = 40 + Tabs[i].Length * 15;
                var pill = Theme.At(Theme.Box(Color.Transparent, 26), x - 18, 40, w + 36, 52);
                pill.PivotPoint = PivotPoint.Center;
                var label = Theme.Label(Tabs[i], t.Px(29), i == current ? Theme.Text : Theme.Muted, i == current, HorizontalAlignment.Center);
                Theme.At(label, 0, 0, w + 36, 52);
                pill.Add(label);
                var bar = Theme.At(Theme.Box(t.Accent, 2), x + w / 2 - 14, 96, 28, 4);
                if (i != current) bar.Hide();
                Root.Add(pill); Root.Add(bar);
                tabs.Add((pill, label, bar));
                x += w + 60;
            }
            Root.Add(Theme.At(Theme.Label(a.Source?.Name ?? "", t.Px(22), Theme.Dim, false, HorizontalAlignment.End), 1160, 40, 480, 52));
            clock = Theme.At(Theme.Label("", t.Px(34), Theme.Text, true, HorizontalAlignment.End), 1640, 40, 184, 52);
            if (a.Prefs.Clock)
            {
                Root.Add(clock);
                Tick();
                clockTimer = new Timer(5000);
                clockTimer.Tick += (s, e) => { Tick(); return clock != null; };
                clockTimer.Start();
            }
        }

        void Tick() { if (clock != null) clock.Text = DateTime.Now.ToString("h:mm tt"); }

        public void Stop() { clockTimer?.Stop(); clockTimer?.Dispose(); clockTimer = null; clock = null; }

        public void SetFocus(int i)
        {
            if (Focus >= 0 && Focus < tabs.Count)
            {
                var o = tabs[Focus];
                o.pill.BackgroundColor = Color.Transparent; o.pill.Scale = new Vector3(1, 1, 1);
                o.label.TextColor = Focus == current ? Theme.Text : Theme.Muted;
            }
            Focus = i;
            if (i >= 0 && i < tabs.Count)
            {
                var n = tabs[i];
                n.pill.BackgroundColor = t.Accent;
                n.label.TextColor = t.Ink;
                Theme.Animate(n.pill, "Scale", new Vector3(1.08f, 1.08f, 1), t.Motion);
            }
        }
    }

    /// <summary>One poster (2:3) or thumbnail (16:9) with caption. Focus style: ring (outline), glow (outline + accent
    /// glow + scale), lift (bigger scale + rise + dark shadow). Reduced motion snaps instead of animating.</summary>
    public class CardView
    {
        public View Root, Body;
        View ring, glow;
        TextLabel title, sub;
        readonly Theme t;
        readonly float h;
        public bool Focused;

        public CardView(App a, MediaItem item, CardMetrics m, bool showProgress, string titleText = null, string subText = null)
        {
            t = a.Theme; h = m.Height;
            float capH = t.Px(66) + 18;
            Root = Theme.At(new View(), 0, 0, m.Width, m.Height + capH);
            Body = Theme.At(new View(), 0, 0, m.Width, m.Height);
            Body.PivotPoint = PivotPoint.Center;
            Root.Add(Body);
            glow = Theme.At(Theme.Box(t.Accent, t.R), 0, 0, m.Width, m.Height);
            glow.BoxShadow = new Shadow(34f, t.AccentGlow, new Vector2(0, 0), new Vector2(4, 4));
            glow.Hide();
            Body.Add(glow);
            // 16:9 cards show the episode still or the title's backdrop; 2:3 cards show the poster
            string art = m.Landscape ? (item.Type == "episode" ? item.Thumb : item.Art) : item.Thumb;
            if (string.IsNullOrEmpty(art)) art = item.Thumb;
            Body.Add(Art.Make(a.Source?.Image(art, m.Width, m.Height) ?? art, m.Width, m.Height, t.R));
            if (showProgress && item.Duration > 0 && item.ViewOffset > 0)
            {
                var track = Theme.At(Theme.Box(Theme.Hex(0, 0.55f)), 0, m.Height - 7, m.Width, 7);
                track.Add(Theme.At(Theme.Box(t.Accent), 0, 0, (float)(m.Width * Math.Min(1, item.ViewOffset.Value / item.Duration.Value)), 7));
                Body.Add(track);
            }
            if (item.ViewCount > 0 && item.Type == "episode")
            {
                var tick = Theme.At(Theme.Box(Theme.Hex(0x0b0d10, 0.78f), 16), m.Width - 46, 12, 32, 32);
                tick.Add(Theme.At(Theme.Label("✓", 20, t.Accent, true, HorizontalAlignment.Center), 0, 0, 32, 32));
                Body.Add(tick);
            }
            ring = Theme.At(new View { BackgroundColor = Color.Transparent, BorderlineWidth = 4, BorderlineColor = t.Accent, CornerRadius = t.R + 2 }, -4, -4, m.Width + 8, m.Height + 8);
            ring.Hide();
            Body.Add(ring);
            title = Theme.Label(titleText ?? Fmt.CardTitle(item), t.Px(24), Theme.Hex(0xcfcac1), true);
            title.Ellipsis = true;
            Root.Add(Theme.At(title, 0, m.Height + 14, m.Width, t.Px(32)));
            sub = Theme.Label(subText ?? Fmt.Sub(item), t.Px(20), Theme.Dim);
            sub.Ellipsis = true;
            Root.Add(Theme.At(sub, 0, m.Height + 14 + t.Px(34), m.Width, t.Px(28)));
        }

        public void SetFocused(bool on)
        {
            if (on == Focused) return;
            Focused = on;
            var style = t.P.FocusStyle;
            float scale = !on ? 1f : style == "ring" ? 1.03f : style == "glow" ? 1.07f : 1.1f;
            if (on && style != "lift") ring.Show(); else ring.Hide();
            if (on && style != "ring")
            {
                glow.BoxShadow = style == "lift" ? new Shadow(40f, Theme.Hex(0, 0.8f), new Vector2(0, 18), new Vector2(0, 0)) : new Shadow(34f, t.AccentGlow, new Vector2(0, 0), new Vector2(4, 4));
                glow.Show();
            }
            else glow.Hide();
            title.TextColor = on ? Theme.Hex(0xffffff) : Theme.Hex(0xcfcac1);
            sub.TextColor = on ? Theme.Muted : Theme.Dim;
            Theme.Animate(Body, "Scale", new Vector3(scale, scale, 1), t.Motion);
            if (style == "lift") Theme.Animate(Body, "PositionY", on ? -12f : 0f, t.Motion);
        }
    }

    public class Pill
    {
        public View Root;
        TextLabel label, value;
        readonly Theme t;
        readonly bool primary;
        public string Id;

        public Pill(Theme t, string id, string text, string val = null, bool primary = false, float x = 0, float y = 0, string icon = null)
        {
            this.t = t; this.primary = primary; Id = id;
            float px = t.Px(28);
            string full = (icon != null ? icon + "  " : "") + text;
            float w = 64 + full.Length * px * 0.55f + (val != null ? 20 + val.Length * px * 0.5f : 0);
            Root = Theme.At(Theme.Box(primary ? Theme.Hex(0xf4f1ea) : Theme.Surface2, t.R), x, y, w, t.Px(76));
            Root.PivotPoint = PivotPoint.Center;
            label = Theme.Label(full, px, primary ? Theme.Hex(0x0b0d10) : Theme.Text, true, val == null ? HorizontalAlignment.Center : HorizontalAlignment.Begin);
            Theme.At(label, val == null ? 0 : 32, 0, val == null ? w : w - 32, t.Px(76));
            Root.Add(label);
            if (val != null)
            {
                value = Theme.Label(val, px, Theme.Muted, false, HorizontalAlignment.End);
                Root.Add(Theme.At(value, 0, 0, w - 32, t.Px(76)));
            }
        }

        public float Width => Root.SizeWidth;

        public void SetFocused(bool on)
        {
            Root.BackgroundColor = on ? t.Accent : primary ? Theme.Hex(0xf4f1ea) : Theme.Surface2;
            label.TextColor = on ? t.Ink : primary ? Theme.Hex(0x0b0d10) : Theme.Text;
            if (value != null) value.TextColor = on ? t.Ink : Theme.Muted;
            Root.BoxShadow = on && t.P.FocusStyle != "ring" ? new Shadow(28f, t.AccentGlow, new Vector2(0, 0), new Vector2(2, 2)) : null;
            Theme.Animate(Root, "Scale", on ? new Vector3(1.06f, 1.06f, 1) : new Vector3(1, 1, 1), t.Motion);
        }
    }

    public static class Chips
    {
        /// <summary>Outlined tech chips then quiet genre chips, left to right. Returns the end x.</summary>
        public static float Row(View parent, Theme t, float x, float y, IList<string> tech, IList<string> quiet)
        {
            float px = t.Px(18), hgt = t.Px(34);
            foreach (var c in tech)
            {
                float w = 24 + c.Length * px * 0.72f;
                var v = Theme.At(new View { BackgroundColor = Color.Transparent, BorderlineWidth = 2, BorderlineColor = Theme.Hex(0xebe7df, 0.7f), CornerRadius = t.Rs }, x, y, w, hgt);
                v.Add(Theme.At(Theme.Label(c, px, Theme.Hex(0xebe7df), true, HorizontalAlignment.Center), 0, 0, w, hgt));
                parent.Add(v); x += w + 12;
            }
            if (quiet != null)
                foreach (var c in quiet)
                {
                    float w = 28 + c.Length * px * 0.6f;
                    var v = Theme.At(Theme.Box(Theme.Hex(0xffffff, 0.1f), t.Rs), x, y, w, hgt);
                    v.Add(Theme.At(Theme.Label(c, px, Theme.Hex(0xebe7df), false, HorizontalAlignment.Center), 0, 0, w, hgt));
                    parent.Add(v); x += w + 12;
                }
            return x;
        }
    }

    /// <summary>Full-screen background per the Background pref: solid, gradient (accent glow), or the focused title's backdrop.</summary>
    public class Backdrop
    {
        public View Root;
        View art;
        string current;
        readonly App a;
        Timer debounce;
        string pending;

        public Backdrop(App a, View parent)
        {
            this.a = a;
            Root = Theme.At(new View { BackgroundColor = Theme.Bg }, 0, 0, App.W, App.H);
            parent.Add(Root);
            if (a.Prefs.Background == "gradient")
            {
                Theme.Gradient(Root, true, (0f, Theme.Hex(0x121821)), (0.72f, Theme.Bg), (1f, Theme.Bg));
                var glow = Theme.At(new View(), App.W * 0.88f - 1100, -0.12f * App.H - 700, 2200, 1400);
                Theme.Radial(glow, Theme.Hex(a.Theme.AccentHex, 0.24f), Theme.Hex(a.Theme.AccentHex, 0f));
                Root.Add(glow);
            }
        }

        public bool Enabled => a.Prefs.Background == "backdrop";

        /// <summary>Swap the art after focus settles (120 ms) so fast scrolling never rebuilds backdrops per key.</summary>
        public void Show(string path, bool immediate = false)
        {
            if (!Enabled || path == current) return;
            pending = path;
            if (immediate) { Apply(); return; }
            if (debounce == null)
            {
                debounce = new Timer(120);
                debounce.Tick += (s, e) => { Apply(); return false; };
            }
            debounce.Stop(); debounce.Start();
        }

        void Apply()
        {
            if (Root == null || pending == current) return;
            current = pending;
            var old = art;
            art = Art.Make(a.Source?.Image(current, 1920, 1080) ?? current, App.W, App.H, 0);
            art.Opacity = 0;
            Root.Add(art);
            Theme.Animate(art, "Opacity", 1f, a.Prefs.ReducedMotion ? 0 : 220);
            if (old != null) Theme.DisposeTree(old);
        }

        public void Stop() { debounce?.Stop(); debounce?.Dispose(); debounce = null; }
    }
}
