// Library grid (Movies / Shows). Only rows around the focused row exist (GridWindow), so 1,200 titles cost the
// same as 30. Scrolling is one transform on the grid container.
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
    public class LibraryScreen : Screen
    {
        readonly string kind;
        public override string Name => kind == "movie" ? "movies" : "shows";
        int Tab => kind == "movie" ? 1 : 2;
        List<MediaItem> items;
        string error;
        int focus;
        bool inNav, loadStarted;
        NavBar nav;
        Backdrop bd;
        View grid;
        TextLabel sub;
        readonly Dictionary<int, CardView> cards = new Dictionary<int, CardView>();
        CardMetrics m;
        int cols, rowH, visibleRows;
        const float GridTop = 280;

        public LibraryScreen(string kind) { this.kind = kind; }

        public override void Build()
        {
            if (!loadStarted) { loadStarted = true; _ = Load(); }
            var t = A.Theme;
            cards.Clear();
            bd = new Backdrop(A, Root);
            var scrim = Theme.At(new View(), 0, 0, App.W, App.H);
            Theme.Gradient(scrim, true, (0f, Theme.Hex(0x0b0d10, 0.55f)), (0.4f, Theme.Hex(0x0b0d10, 0.82f)), (1f, Theme.Hex(0x0b0d10, 0.95f)));
            Root.Add(scrim);
            Root.Add(Theme.At(Theme.Label(kind == "movie" ? "Movies" : "Shows", t.Px(60), Theme.Text, true), Layout.SafeX, 140, 1000, t.Px(76)));
            sub = Theme.At(Theme.Label("", t.Px(24), Theme.Muted), Layout.SafeX, 140 + t.Px(80), 1000, t.Px(34));
            Root.Add(sub);
            var clip = Theme.At(new View { ClippingMode = ClippingModeType.ClipChildren }, 0, GridTop - 30, App.W, App.H - GridTop + 30);
            Root.Add(clip);
            grid = Theme.At(new View(), 0, 30, App.W, 10);
            clip.Add(grid);
            (m, cols) = Layout.Grid(A.Prefs.PosterSize);
            rowH = Layout.GridRowHeight(A.Prefs);
            visibleRows = (int)Math.Ceiling((App.H - GridTop) / (double)rowH);
            nav = new NavBar(A, Tab, Root);
            if (items == null || items.Count == 0)
            {
                sub.Text = error ?? (items == null ? "Loading…" : "Nothing here yet.");
                Detail = "loading";
                inNav = items != null;
                if (inNav) nav.SetFocus(Tab);
                return;
            }
            Window(false);
            if (inNav) nav.SetFocus(Tab);
            else Focus(focus, false);
        }

        async Task Load()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var sec = await A.Source.Sections();
                var id = kind == "movie" ? sec.MovieSectionId : sec.ShowSectionId;
                items = id == null ? new List<MediaItem>() : await A.Source.SectionAll(id, kind == "movie" ? 1 : 2);
                Telemetry.Report("library_loaded", Telemetry.F("kind", kind, "items", items.Count, "ms", sw.Elapsed.TotalMilliseconds));
            }
            catch (Exception ex)
            {
                error = "Could not load: " + PlexClient.Redact(ex.Message);
                items = new List<MediaItem>();
                Telemetry.Report("library_error", Telemetry.F("kind", kind, "error", error));
            }
            if (Root == null) return;
            if (A.Top == this) A.Rebuild(this); else ThemeVersion = -1;
        }

        void Window(bool animate)
        {
            var w = Layout.GridWindow(focus, items.Count, cols, visibleRows);
            foreach (var i in cards.Keys.Where(i => i < w.Start || i >= w.End).ToList()) { Theme.DisposeTree(cards[i].Root); cards.Remove(i); }
            for (int i = w.Start; i < w.End; i++)
                if (!cards.ContainsKey(i))
                {
                    var c = new CardView(A, items[i], m, false);
                    c.Root.Position = new Position(Layout.SafeX + (i % cols) * (m.Width + m.Gap), (i / cols) * rowH);
                    grid.Add(c.Root);
                    cards[i] = c;
                }
            float y = 30 - w.Scroll * rowH;
            if (animate) Theme.Animate(grid, "PositionY", y, A.Theme.Motion); else grid.PositionY = y;
            Detail = $"focus={focus} cards={cards.Count} of {items.Count}";
        }

        void Focus(int i, bool animate)
        {
            if (cards.TryGetValue(focus, out var old) && i != focus) old.SetFocused(false);
            focus = Math.Max(0, Math.Min(items.Count - 1, i));
            Window(animate);
            if (cards.TryGetValue(focus, out var n)) n.SetFocused(true);
            sub.Text = $"{items.Count:N0} titles · {focus + 1:N0} of {items.Count:N0}";
            bd.Show(items[focus].Art);
        }

        public override bool OnKey(string key)
        {
            if (key == "back") return Tabs.Back(A, Tab);
            if (inNav || items == null || items.Count == 0)
            {
                if (!inNav) { inNav = true; nav.SetFocus(Tab); return true; }
                switch (key)
                {
                    case "left": nav.SetFocus(Math.Max(0, nav.Focus - 1)); return true;
                    case "right": nav.SetFocus(Math.Min(NavBar.Tabs.Length - 1, nav.Focus + 1)); return true;
                    case "enter": return Tabs.Open(A, nav.Focus, Tab);
                    case "down":
                        if (items == null || items.Count == 0) return true;
                        inNav = false; nav.SetFocus(-1); Focus(focus, true); return true;
                }
                return true;
            }
            switch (key)
            {
                case "left": if (focus % cols > 0) Focus(focus - 1, true); return true;
                case "right": if (focus % cols < cols - 1 && focus + 1 < items.Count) Focus(focus + 1, true); return true;
                case "down": if (focus + cols < items.Count) Focus(focus + cols, true); else if (focus / cols < (items.Count - 1) / cols) Focus(items.Count - 1, true); return true;
                case "up":
                    if (focus < cols) { inNav = true; if (cards.TryGetValue(focus, out var c)) c.SetFocused(false); nav.SetFocus(Tab); return true; }
                    Focus(focus - cols, true); return true;
                case "enter":
                case "play":
                    A.Push(new DetailsScreen(items[focus], key == "play"));
                    return true;
            }
            return false;
        }

        public override void OnClose() { nav?.Stop(); bd?.Stop(); }
    }
}
