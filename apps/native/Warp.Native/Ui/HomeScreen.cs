// Home: hero + user-ordered rails. Virtualized both ways: a rail only holds the cards in its RailWindow, and only
// rails near the focused row hold cards at all. Focus moves touch two cards and one transform.
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
    public class RailRow
    {
        public View Root, Track;
        public readonly HomeRail R;
        public readonly CardMetrics M;
        readonly App a;
        readonly Dictionary<int, CardView> cards = new Dictionary<int, CardView>();
        public int Focus;
        public bool Built;
        readonly int visible;
        public List<MediaItem> Items => R.Hub.Items;

        public RailRow(App a, HomeRail r, string kind, View parent, float y, int focus)
        {
            this.a = a; R = r; Focus = Math.Min(focus, r.Hub.Items.Count - 1);
            var t = a.Theme;
            M = Layout.Card(kind, a.Prefs.PosterSize);
            visible = Layout.VisibleCards(M);
            Root = Theme.At(new View(), 0, y, App.W, Layout.RailHeight(kind, a.Prefs));
            parent.Add(Root);
            float th = (float)Math.Round(34 * t.Ts + 24);
            var title = Theme.Label(r.Hub.Title, t.Px(32), Theme.Hex(0xe9e5dd), true);
            Root.Add(Theme.At(title, Layout.SafeX, 0, 1100, th - 10));
            Track = Theme.At(new View(), Layout.SafeX, th, App.W, M.Height + t.Px(66) + 18);
            Root.Add(Track);
        }

        public void Ensure(bool on)
        {
            if (!on)
            {
                if (!Built) return;
                foreach (var c in cards.Values) Theme.DisposeTree(c.Root);
                cards.Clear();
                Built = false;
                return;
            }
            Built = true;
            Window(false);
        }

        void Window(bool animate)
        {
            var w = Layout.RailWindow(Focus, Items.Count, visible);
            foreach (var i in cards.Keys.Where(i => i < w.Start || i >= w.End).ToList()) { Theme.DisposeTree(cards[i].Root); cards.Remove(i); }
            for (int i = w.Start; i < w.End; i++)
                if (!cards.ContainsKey(i))
                {
                    var c = new CardView(a, Items[i], M, R.Kind == "continue");
                    c.Root.PositionX = i * (M.Width + M.Gap);
                    Track.Add(c.Root);
                    cards[i] = c;
                }
            float x = Layout.SafeX - w.Scroll * (M.Width + M.Gap);
            if (animate) Theme.Animate(Track, "PositionX", x, a.Theme.Motion); else Track.PositionX = x;
        }

        public void SetFocus(int col, bool focused)
        {
            int next = Math.Max(0, Math.Min(Items.Count - 1, col));
            if (next != Focus && cards.TryGetValue(Focus, out var old)) old.SetFocused(false);
            Focus = next;
            if (Built) Window(true);
            if (cards.TryGetValue(Focus, out var n)) n.SetFocused(focused);
        }

        public int CardCount => cards.Count;
    }

    public class HomeScreen : Screen
    {
        public override string Name => "home";
        List<Hub> hubs;
        List<HomeRail> rails = new List<HomeRail>();
        MediaItem hero;
        NavBar nav;
        Backdrop bd;
        View content;
        List<RailRow> rows = new List<RailRow>();
        List<Pill> heroButtons = new List<Pill>();
        List<int> tops;
        int row = -1, heroCol;
        bool inNav, loadStarted;
        string error;
        readonly Dictionary<string, int> cols = new Dictionary<string, int>();

        public override void Build()
        {
            if (!loadStarted) { loadStarted = true; _ = Load(); }
            var t = A.Theme;
            bd = new Backdrop(A, Root);
            var left = Theme.At(new View(), 0, 0, App.W, App.H);
            Theme.Gradient(left, false, (0f, Theme.Hex(0x0b0d10, 0.92f)), (0.36f, Theme.Hex(0x0b0d10, 0.62f)), (0.68f, Theme.Hex(0x0b0d10, 0f)), (1f, Theme.Hex(0x0b0d10, 0f)));
            Root.Add(left);
            var bottom = Theme.At(new View(), 0, 0, App.W, App.H);
            Theme.Gradient(bottom, true, (0f, Theme.Hex(0x0b0d10, 0f)), (0.30f, Theme.Hex(0x0b0d10, 0f)), (0.48f, Theme.Hex(0x0b0d10, 0.2f)), (0.76f, Theme.Hex(0x0b0d10, 0.92f)), (1f, Theme.Bg));
            Root.Add(bottom);
            content = Theme.At(new View(), 0, 0, App.W, App.H);
            Root.Add(content);
            rows.Clear(); heroButtons.Clear();
            if (hubs == null || hubs.Count == 0)
            {
                content.Add(Theme.At(Theme.Label(error ?? "Loading your library…", t.Px(30), Theme.Muted), Layout.SafeX, 200, 1600, 60));
                nav = new NavBar(A, 0, Root);
                Detail = "loading";
                return;
            }
            rails = HomeRails.Order(hubs, A.Prefs.Rails);
            hero = A.Prefs.Hero ? HomeRails.HeroItem(hubs) : null;
            if (hero == null && row < 0) row = 0;
            if (row >= rails.Count) row = rails.Count - 1;
            tops = Layout.RailTops(rails.Select(r => r.Kind).ToList(), A.Prefs);
            if (hero != null) BuildHero(t);
            float railsStart = hero != null ? Layout.HeroH : Layout.NavH + 8;
            for (int i = 0; i < rails.Count; i++)
                rows.Add(new RailRow(A, rails[i], rails[i].Kind, content, railsStart + tops[i], cols.TryGetValue(rails[i].Hub.Identifier, out var c) ? c : 0));
            nav = new NavBar(A, 0, Root);
            ApplyRow(false);
            if (inNav) nav.SetFocus(0);
        }

        void BuildHero(Theme t)
        {
            float x = Layout.SafeX;
            bool cont = (hero.ViewOffset ?? 0) > 0;
            content.Add(Theme.At(Theme.Label(cont ? "CONTINUE WATCHING" : "FEATURED", t.Px(21), t.Accent, true), x, 260, 1000, t.Px(30)));
            var title = Theme.Label(Fmt.CardTitle(hero), t.Px(84), Theme.Text, true);
            title.Ellipsis = true;
            content.Add(Theme.At(title, x, 290, 1100, t.Px(110)));
            content.Add(Theme.At(Theme.Label(DetailsModel.MetaLine(hero), t.Px(25), Theme.Meta), x, 290 + t.Px(112), 1100, t.Px(36)));
            var sum = Theme.Label(hero.Summary ?? "", t.Px(26), Theme.Hex(0xdcd8d0));
            sum.MultiLine = true; sum.Ellipsis = true; sum.VerticalAlignment = VerticalAlignment.Top;
            content.Add(Theme.At(sum, x, 290 + t.Px(160), 960, t.Px(116)));
            float bx = x - 6, by = 600;
            foreach (var (id, label, primary) in new[] { ("play", cont ? "Resume" : "Play", true), ("info", "More info", false) })
            {
                var p = new Pill(t, id, label, null, primary, bx, by, id == "play" ? "▶" : null);
                if (primary) { p.Root.BackgroundColor = t.Accent; }
                content.Add(p.Root);
                heroButtons.Add(p);
                bx += p.Width + 16;
            }
        }

        async Task Load()
        {
            var sw = Stopwatch.StartNew();
            bool cached = false;
            if (A.Source is PlexSource)
            {
                var c = HomeCache.Read(SafeGet(HomeCache.Key), HomeCache.MaxAgeMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                if (c != null && c.Count > 0) { hubs = c; cached = true; Refresh(); Telemetry.Report("home_cache", Telemetry.F("ms", sw.Elapsed.TotalMilliseconds, "hubs", c.Count)); }
            }
            try
            {
                var fresh = await A.Source.Hubs();
                double ms = sw.Elapsed.TotalMilliseconds;
                if (A.Source is PlexSource ps && ps.LastHubsPayload != null)
                    try { A.Store.Set(HomeCache.Key, HomeCache.Write(ps.LastHubsPayload, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ps.Name)); } catch (Exception) { }
                hubs = fresh;
                Telemetry.Report("home_loaded", Telemetry.F("ms", ms, "hubs", fresh.Count, "items", fresh.Sum(h => h.Items.Count), "had_cache", cached));
                Refresh();
            }
            catch (Exception ex)
            {
                Telemetry.Report("home_error", Telemetry.F("error", PlexClient.Redact(ex.Message)));
                if (!cached) { error = "Could not reach your server: " + PlexClient.Redact(ex.Message); Refresh(); }
            }
        }

        string SafeGet(string k) { try { return A.Store.Get(k); } catch (Exception) { return null; } }

        void Refresh()
        {
            if (Root == null) return;
            if (A.Current == this) A.Rebuild(this); else ThemeVersion = -1;
        }

        MediaItem Focused => row < 0 ? hero : row < rows.Count ? rows[row].Items[rows[row].Focus] : null;

        void ApplyRow(bool animate)
        {
            for (int i = 0; i < rows.Count; i++) rows[i].Ensure(Math.Abs(i - Math.Max(0, row)) <= 2);
            for (int i = 0; i < rows.Count; i++) rows[i].SetFocus(rows[i].Focus, !inNav && i == row);
            for (int i = 0; i < heroButtons.Count; i++) heroButtons[i].SetFocused(!inNav && row < 0 && i == heroCol);
            float y = -Layout.HomeScrollY(row, tops, hero != null);
            if (animate) Theme.Animate(content, "PositionY", y, A.Theme.Motion); else content.PositionY = y;
            var f = Focused;
            if (f != null) bd.Show(f.Art ?? f.GrandparentArt, !animate);
            Detail = $"row={row} col={(row >= 0 && row < rows.Count ? rows[row].Focus : heroCol)} cards={rows.Sum(r => r.CardCount)} item={f?.Title}";
        }

        public override bool OnKey(string key)
        {
            if (hubs == null || hubs.Count == 0)
            {
                if (key == "up" || key == "down" || key == "left" || key == "right" || key == "enter") return NavKey(key == "down" ? "left" : key);
                return false;
            }
            if (inNav) return NavKey(key);
            switch (key)
            {
                case "up":
                    if (row < 0 || (row == 0 && hero == null)) { inNav = true; ApplyRow(true); nav.SetFocus(0); return true; }
                    row--; ApplyRow(true); return true;
                case "down":
                    if (row < rows.Count - 1) { row++; ApplyRow(true); }
                    return true;
                case "left":
                case "right":
                    int d = key == "right" ? 1 : -1;
                    if (row < 0) { heroCol = Math.Max(0, Math.Min(heroButtons.Count - 1, heroCol + d)); ApplyRow(true); return true; }
                    var r = rows[row];
                    r.SetFocus(r.Focus + d, true);
                    cols[r.R.Hub.Identifier] = r.Focus;
                    var f = Focused;
                    if (f != null) bd.Show(f.Art ?? f.GrandparentArt);
                    Detail = $"row={row} col={r.Focus} cards={rows.Sum(x => x.CardCount)} item={f?.Title}";
                    return true;
                case "enter":
                case "play":
                    var item = Focused;
                    if (item == null) return true;
                    bool autoplay = key == "play" || (row < 0 && heroCol == 0);
                    A.Push(new DetailsScreen(item, autoplay));
                    return true;
            }
            return false;
        }

        bool NavKey(string key)
        {
            if (!inNav) { inNav = true; nav.SetFocus(0); return true; }
            switch (key)
            {
                case "left": nav.SetFocus(Math.Max(0, nav.Focus - 1)); return true;
                case "right": nav.SetFocus(Math.Min(NavBar.Tabs.Length - 1, nav.Focus + 1)); return true;
                case "down":
                    if (rows.Count == 0 && hero == null) return true;
                    inNav = false; nav.SetFocus(-1); ApplyRow(true); return true;
                case "enter": return Tabs.Open(A, nav.Focus, 0);
                case "up": return true;
            }
            return false;
        }

        public override void OnClose() { nav?.Stop(); bd?.Stop(); }
        public override void OnHide() { }
    }

    public static class Tabs
    {
        /// <summary>Open a nav tab (0 Home, 1 Movies, 2 Shows, 3 Settings) as the new top-level screen.</summary>
        public static bool Open(App a, int tab, int current)
        {
            if (tab == current) return true;
            Telemetry.Report("tab", Telemetry.F("tab", NavBar.Tabs[tab]));
            switch (tab)
            {
                case 0: a.SwitchTab(new HomeScreen()); break;
                case 1: a.SwitchTab(new LibraryScreen("movie")); break;
                case 2: a.SwitchTab(new LibraryScreen("show")); break;
                case 3: a.SwitchTab(new SettingsScreen()); break;
            }
            return true;
        }

        /// <summary>Back on a non-Home tab returns to Home; Back on Home exits.</summary>
        public static bool Back(App a, int current)
        {
            if (current == 0) return false;
            a.SwitchTab(new HomeScreen());
            return true;
        }
    }
}
