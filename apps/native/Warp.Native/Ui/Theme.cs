// Theme: Prefs -> concrete colours, radii, type scale and motion for NUI. Plus small view helpers (labels,
// gradients, art for real Plex images or the mock fixture drawn natively).
using System;
using System.Collections.Generic;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Warp.Core;

namespace Warp.Ui
{
    public class Theme
    {
        public static readonly Color Bg = Hex(0x0b0d10), Text = Hex(0xf4f1ea), Muted = Hex(0xa7adb6), Dim = Hex(0x6c737e),
            Surface = Hex(0xffffff, 0.08f), Surface2 = Hex(0xffffff, 0.14f), Meta = Hex(0xd4d0c8), Panel = Hex(0x141920), Line = Hex(0xffffff, 0.12f);

        public readonly Prefs P;
        public readonly Color Accent, AccentSoft, AccentGlow, Ink;
        public readonly int AccentHex;
        public readonly float R, Rs, Ts;
        public readonly int Motion;
        public readonly int Version;
        static int versions;

        public Theme(Prefs p)
        {
            P = p.Clone();
            AccentHex = PrefsLogic.AccentOf(p.Accent).Hex;
            Accent = Hex(AccentHex);
            AccentSoft = Hex(AccentHex, 0.18f);
            AccentGlow = Hex(AccentHex, 0.55f);
            Ink = Hex(PrefsLogic.AccentInk(AccentHex));
            R = PrefsLogic.RadiusPx(p.Radius);
            Rs = Math.Max(2, (float)Math.Round(R / 2));
            Ts = PrefsLogic.TextScale(p.TextSize);
            Motion = PrefsLogic.MotionMs(p);
            Version = ++versions;
        }

        public float Px(float px) => (float)Math.Round(px * Ts);

        public static Color Hex(int rgb, float a = 1f) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);
        public static Color Hsl(double h, double s, double l, float a = 1f) => Hex(Pattern.Hsl(((h % 360) + 360) % 360, s, l), a);

        public static TextLabel Label(string text, float px, Color c, bool bold = false, HorizontalAlignment align = HorizontalAlignment.Begin, string family = null)
        {
            var l = new TextLabel(text ?? "")
            {
                TextColor = c, PixelSize = px, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center,
                PositionUsesPivotPoint = false,
            };
            if (family != null) l.FontFamily = family;
            if (bold) l.FontStyle = new PropertyMap().Add("weight", new PropertyValue("bold"));
            return l;
        }

        public static T At<T>(T v, float x, float y, float w, float h) where T : View
        {
            v.PositionUsesPivotPoint = false;
            v.Position = new Position(x, y);
            v.Size = new Size(w, h);
            return v;
        }

        public static View Box(Color c, float radius = 0)
        {
            var v = new View { BackgroundColor = c, PositionUsesPivotPoint = false };
            if (radius > 0) v.CornerRadius = radius;
            return v;
        }

        /// <summary>Linear gradient background. stops: (offset 0..1, colour). vertical = top->bottom, else left->right.</summary>
        public static void Gradient(View v, bool vertical, params (float o, Color c)[] stops)
        {
            var offsets = new PropertyArray();
            var colors = new PropertyArray();
            foreach (var (o, c) in stops)
            {
                offsets.Add(new PropertyValue(o));
                colors.Add(new PropertyValue(new Vector4(c.R, c.G, c.B, c.A)));
            }
            var map = new PropertyMap()
                .Add(Visual.Property.Type, new PropertyValue((int)Visual.Type.Gradient))
                .Add(GradientVisualProperty.StartPosition, new PropertyValue(vertical ? new Vector2(0, -0.5f) : new Vector2(-0.5f, 0)))
                .Add(GradientVisualProperty.EndPosition, new PropertyValue(vertical ? new Vector2(0, 0.5f) : new Vector2(0.5f, 0)))
                .Add(GradientVisualProperty.StopOffset, new PropertyValue(offsets))
                .Add(GradientVisualProperty.StopColor, new PropertyValue(colors));
            v.Background = map;
        }

        /// <summary>Radial gradient (centre colour -> transparent), used for the mock art's light and the gradient background.</summary>
        public static void Radial(View v, Color inner, Color outer)
        {
            var offsets = new PropertyArray(); offsets.Add(new PropertyValue(0f)); offsets.Add(new PropertyValue(1f));
            var colors = new PropertyArray();
            colors.Add(new PropertyValue(new Vector4(inner.R, inner.G, inner.B, inner.A)));
            colors.Add(new PropertyValue(new Vector4(outer.R, outer.G, outer.B, outer.A)));
            v.Background = new PropertyMap()
                .Add(Visual.Property.Type, new PropertyValue((int)Visual.Type.Gradient))
                .Add(GradientVisualProperty.Center, new PropertyValue(new Vector2(0, 0)))
                .Add(GradientVisualProperty.Radius, new PropertyValue(0.5f))
                .Add(GradientVisualProperty.StopOffset, new PropertyValue(offsets))
                .Add(GradientVisualProperty.StopColor, new PropertyValue(colors));
        }

        public static void Animate(View v, string prop, object to, int ms)
        {
            if (ms <= 0)
            {
                switch (prop)
                {
                    case "Scale": v.Scale = (Vector3)to; break;
                    case "PositionX": v.PositionX = (float)to; break;
                    case "PositionY": v.PositionY = (float)to; break;
                    case "Opacity": v.Opacity = (float)to; break;
                }
                return;
            }
            var a = new Animation(ms);
            a.AnimateTo(v, prop, to, new AlphaFunction(AlphaFunction.BuiltinFunctions.EaseOut));
            a.Finished += (s, e) => a.Dispose();
            a.Play();
        }

        public static void DisposeTree(View v)
        {
            if (v == null) return;
            while (v.ChildCount > 0) { var c = v.GetChildAt(0); v.Remove(c); DisposeTree(c); }
            v.Unparent();
            v.Dispose();
        }

        public static int CountViews(View v)
        {
            if (v == null) return 0;
            int n = 1;
            for (uint i = 0; i < v.ChildCount; i++) n += CountViews(v.GetChildAt(i));
            return n;
        }
    }

    /// <summary>Artwork: Plex image URLs load asynchronously into an ImageView; "mock:" descriptors are drawn
    /// natively (gradient, light, hills, title) so the demo needs no network.</summary>
    public static class Art
    {
        public static View Make(string url, float w, float h, float radius)
        {
            var holder = Theme.At(new View { BackgroundColor = Theme.Hex(0x1b212a), ClippingMode = ClippingModeType.ClipChildren }, 0, 0, w, h);
            if (radius > 0) holder.CornerRadius = radius;
            if (string.IsNullOrEmpty(url)) return holder;
            if (url.StartsWith("mock:poster:")) { MockPoster(holder, url, w, h); return holder; }
            if (url.StartsWith("mock:backdrop:")) { MockBackdrop(holder, url, w, h); return holder; }
            var img = new ImageView { ResourceUrl = url, DesiredWidth = (int)w, DesiredHeight = (int)h, PositionUsesPivotPoint = false };
            Theme.At(img, 0, 0, w, h);
            if (radius > 0) img.CornerRadius = radius;
            holder.Add(img);
            return holder;
        }

        static int Seed(string url, int at)
        {
            var parts = url.Split(new[] { ':' }, 4);
            return parts.Length > at && int.TryParse(parts[at], out var s) ? s : 1;
        }

        static void MockPoster(View v, string url, float w, float h)
        {
            int seed = Seed(url, 2);
            var title = url.Split(new[] { ':' }, 4).Length > 3 ? url.Split(new[] { ':' }, 4)[3] : "";
            int hue = MockSource.Hue(seed);
            Theme.Gradient(v, true, (0f, Theme.Hsl(hue, .55, .42)), (1f, Theme.Hsl(hue + 40, .60, .12)));
            float k = w / 300f;
            var light = Theme.At(new View(), w * 0.25f, -h * 0.1f, w * 1.1f, w * 1.1f);
            Theme.Radial(light, Theme.Hsl(hue + 20, .9, .75, 0.7f), Theme.Hsl(hue, .9, .5, 0f));
            v.Add(light);
            float r = (40 + (seed % 5) * 12) * k;
            var disc = Theme.At(Theme.Box(Theme.Hsl(hue + 180, .7, .6, 0.35f), r), (80 + (seed * 37) % 160) * k - r, (120 + (seed * 13) % 90) * k - r, 2 * r, 2 * r);
            v.Add(disc);
            v.Add(Theme.At(Theme.Box(Theme.Hex(0, 0.25f)), 0, h * 2 / 3, w, h / 3));
            var t = Theme.Label(title.ToUpperInvariant(), Math.Max(12, 38 * k), Theme.Hex(0xffffff), true, HorizontalAlignment.Begin, "BreezeSerif");
            t.MultiLine = true; t.VerticalAlignment = VerticalAlignment.Bottom; t.Ellipsis = true;
            v.Add(Theme.At(t, 26 * k, h * 0.62f, w - 44 * k, h * 0.3f));
            var f = Theme.Label("A WARP FIXTURE", Math.Max(8, 14 * k), Theme.Hex(0xffffff, 0.7f));
            v.Add(Theme.At(f, 26 * k, h - 30 * k, w, 18 * k));
        }

        public static void MockBackdrop(View v, string url, float w, float h)
        {
            int seed = Seed(url, 2), hue = MockSource.Hue(seed);
            Theme.Gradient(v, true, (0f, Theme.Hsl(hue + 200, .45, .18)), (0.6f, Theme.Hsl(hue, .55, .38)), (1f, Theme.Hsl(hue + 30, .5, .10)));
            var sun = Theme.At(new View(), w * 0.68f - h * 0.5f, h * 0.42f - h * 0.5f, h, h);
            Theme.Radial(sun, Theme.Hsl(hue + 40, 1, .82, 0.95f), Theme.Hsl(hue, .9, .5, 0f));
            v.Add(sun);
            // hills: rotated squares, bottoms covered by the ground band
            var far = Theme.Hsl(hue + 210, .35, .09, 0.9f);
            foreach (var (cx, top, s) in new[] { (0.16f, 0.58f, 0.5f), (0.47f, 0.52f, 0.55f), (0.81f, 0.56f, 0.5f) })
            {
                float side = h * s;
                var hill = Theme.At(Theme.Box(far), w * cx - side / 2, h * top, side, side);
                hill.PivotPoint = PivotPoint.Center;
                hill.Orientation = new Rotation(new Radian(new Degree(45)), Vector3.ZAxis);
                v.Add(hill);
            }
            v.Add(Theme.At(Theme.Box(Theme.Hsl(hue + 210, .35, .09)), 0, h * 0.78f, w, h * 0.22f));
            v.Add(Theme.At(Theme.Box(Theme.Hex(0x05070a)), 0, h * 0.86f, w, h * 0.14f));
        }
    }
}
