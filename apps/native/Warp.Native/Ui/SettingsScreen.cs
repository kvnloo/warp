// Settings: Appearance (live apply + persisted), Home rows (show/hide/reorder), Reset, account, diagnostics.
// Left/Right on an option row applies immediately: prefs are saved and the page is rebuilt with the new theme.
using System;
using System.Collections.Generic;
using System.Linq;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Warp.Core;

namespace Warp.Ui
{
    public class SettingsScreen : Screen
    {
        public override string Name => "settings";
        List<SettingsRow> rows;
        SettingsPos pos = new SettingsPos(0, -1);
        bool inNav;
        NavBar nav;
        View list;
        readonly List<float> rowY = new List<float>();
        readonly List<View> rowBg = new List<View>();
        readonly List<View> rowBar = new List<View>();
        readonly List<List<(View cell, TextLabel label, bool selected)>> cells = new List<List<(View, TextLabel, bool)>>();
        const float ListTop = 290, ListX = 96, ListW = 1070;

        public override void Build()
        {
            var t = A.Theme;
            rows = SettingsModel.Build(A.Prefs, A.Debug, A.Source?.IsMock ?? false);
            if (pos.Col < 0) pos = new SettingsPos(0, SettingsModel.EntryCol(rows[0], A.Prefs, 0));
            pos.Row = Math.Max(0, Math.Min(rows.Count - 1, pos.Row));
            new Backdrop(A, Root);
            Root.Add(Theme.At(Theme.Label("Settings", t.Px(60), Theme.Text, true), ListX, 140, 1000, t.Px(76)));
            Root.Add(Theme.At(Theme.Label($"Changes apply as you move. Connected to {A.Source?.Name ?? "nothing yet"}.", t.Px(24), Theme.Muted), ListX, 140 + t.Px(80), 1100, t.Px(34)));
            var clip = Theme.At(new View { ClippingMode = ClippingModeType.ClipChildren }, 0, ListTop, 1200, App.H - ListTop);
            Root.Add(clip);
            list = Theme.At(new View(), 0, 0, 1200, 10);
            clip.Add(list);
            rowY.Clear(); rowBg.Clear(); rowBar.Clear(); cells.Clear();
            float y = 0; string group = null;
            float rh = t.Px(92) + 18;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r.Group != group)
                {
                    group = r.Group;
                    if (i > 0) y += 26;
                    list.Add(Theme.At(Theme.Label(group.ToUpperInvariant(), t.Px(21), t.Accent, true), ListX, y, 800, t.Px(34)));
                    y += t.Px(44);
                }
                rowY.Add(y);
                var bg = Theme.At(Theme.Box(Color.Transparent, t.Rs), ListX, y, ListW, rh - 8);
                list.Add(bg); rowBg.Add(bg);
                var bar = Theme.At(Theme.Box(t.Accent), ListX, y, 5, rh - 8);
                bar.Hide(); list.Add(bar); rowBar.Add(bar);
                string label = r.Kind == "rail" ? $"{r.Index + 1}. {r.Label}" : r.Label;
                list.Add(Theme.At(Theme.Label(label, t.Px(29), r.Kind == "rail" && !r.Visible ? Theme.Dim : Theme.Text, true), ListX + 24, y + 8, 560, t.Px(40)));
                string hint = r.Kind == "option" && r.Id == "accent" ? $"{PrefsLogic.AccentOf(A.Prefs.Accent).Label} · {r.Hint}" : r.Kind == "rail" ? (r.Visible ? "Shown on Home" : "Hidden from Home") : r.Hint;
                var hl = Theme.Label(hint, t.Px(20), Theme.Dim);
                hl.Ellipsis = true;
                list.Add(Theme.At(hl, ListX + 24, y + 8 + t.Px(42), 520, t.Px(30)));
                cells.Add(BuildCells(r, y, rh - 8));
                y += rh;
            }
            BuildPreview(t);
            nav = new NavBar(A, 3, Root);
            ApplyFocus(false);
            if (inNav) nav.SetFocus(3);
        }

        List<(View, TextLabel, bool)> BuildCells(SettingsRow r, float y, float h)
        {
            var t = A.Theme;
            var outp = new List<(View, TextLabel, bool)>();
            string current = r.Kind == "option" ? A.Prefs.Get(r.Id) : null;
            float right = ListX + ListW - 18;
            if (r.Kind == "option" && r.Id == "accent")
            {
                float d = 46, gap = 14, x = right - r.Choices.Count * (d + gap) + gap;
                foreach (var c in r.Choices)
                {
                    bool sel = c.Value == current;
                    var cell = Theme.At(new View { BackgroundColor = Color.Transparent, CornerRadius = (d + 12) / 2, BorderlineWidth = sel ? 3 : 0, BorderlineColor = Theme.Hex(0xffffff) }, x - 6, y + h / 2 - d / 2 - 6, d + 12, d + 12);
                    cell.PivotPoint = PivotPoint.Center;
                    cell.Add(Theme.At(Theme.Box(Theme.Hex(c.Swatch ?? 0), d / 2), 6, 6, d, d));
                    list.Add(cell);
                    outp.Add((cell, null, sel));
                    x += d + gap;
                }
                return outp;
            }
            float px = t.Px(24);
            var widths = r.Choices.Select(c => 36 + c.Label.Length * px * 0.56f).ToList();
            float total = widths.Sum() + (widths.Count - 1) * 10, cx = right - total, ch = t.Px(54);
            for (int i = 0; i < r.Choices.Count; i++)
            {
                var c = r.Choices[i];
                bool sel = r.Kind == "option" && c.Value == current;
                var cell = Theme.At(new View { BackgroundColor = sel ? t.AccentSoft : Theme.Surface, CornerRadius = t.Rs, BorderlineWidth = sel ? 2 : 0, BorderlineColor = t.Accent }, cx, y + h / 2 - ch / 2, widths[i], ch);
                cell.PivotPoint = PivotPoint.Center;
                var l = Theme.Label(c.Label, px, sel ? Theme.Text : Theme.Muted, sel, HorizontalAlignment.Center);
                cell.Add(Theme.At(l, 0, 0, widths[i], ch));
                list.Add(cell);
                outp.Add((cell, l, sel));
                cx += widths[i] + 10;
            }
            return outp;
        }

        void BuildPreview(Theme t)
        {
            var panel = Theme.At(Theme.Box(Theme.Hex(0x14181e, 0.92f), t.R), 1236, 300, 588, 690);
            Root.Add(panel);
            panel.Add(Theme.At(Theme.Label("PREVIEW", t.Px(21), t.Accent, true), 32, 26, 400, t.Px(32)));
            var m = Layout.Card("more", A.Prefs.PosterSize);
            float scale = Math.Min(1f, 250f / m.Width);
            var cm = new CardMetrics { Width = (int)(m.Width * scale), Height = (int)(m.Height * scale), Gap = 24 };
            var a = new CardView(A, MockSource.Movie(1), cm, false);
            var b = new CardView(A, MockSource.Movie(6), cm, false);
            a.Root.Position = new Position(36, 84); b.Root.Position = new Position(36 + cm.Width + 24, 84);
            panel.Add(a.Root); panel.Add(b.Root);
            a.SetFocused(true);
            var play = new Pill(t, "play", "Play", null, true, 30, 84 + cm.Height + t.Px(84) + 30, "▶");
            play.SetFocused(true);
            var info = new Pill(t, "info", "More info", null, false, 30 + play.Width + 16, play.Root.PositionY);
            panel.Add(play.Root); panel.Add(info.Root);
        }

        void ApplyFocus(bool animate)
        {
            var t = A.Theme;
            for (int i = 0; i < rows.Count; i++)
            {
                bool rf = !inNav && i == pos.Row;
                rowBg[i].BackgroundColor = rf ? Theme.Hex(0xffffff, 0.07f) : Color.Transparent;
                if (rf) rowBar[i].Show(); else rowBar[i].Hide();
                for (int j = 0; j < cells[i].Count; j++)
                {
                    var (cell, label, sel) = cells[i][j];
                    bool f = rf && j == pos.Col;
                    if (label == null) // swatch
                    {
                        cell.BorderlineWidth = f ? 4 : sel ? 3 : 0;
                        cell.BorderlineColor = f ? Theme.Hex(0xffffff) : Theme.Hex(0xffffff, 0.8f);
                        cell.Scale = f ? new Vector3(1.15f, 1.15f, 1) : new Vector3(1, 1, 1);
                        continue;
                    }
                    cell.BackgroundColor = f ? t.Accent : sel ? t.AccentSoft : Theme.Surface;
                    label.TextColor = f ? t.Ink : sel ? Theme.Text : Theme.Muted;
                    cell.Scale = f ? new Vector3(1.06f, 1.06f, 1) : new Vector3(1, 1, 1);
                }
            }
            float target = -Math.Max(0, rowY[pos.Row] - 220);
            if (animate) Theme.Animate(list, "PositionY", target, t.Motion); else list.PositionY = target;
            Detail = $"row={pos.Row}:{rows[pos.Row].Id} col={pos.Col} accent={A.Prefs.Accent} poster={A.Prefs.PosterSize} focus={A.Prefs.FocusStyle}";
        }

        public override bool OnKey(string key)
        {
            if (key == "back") return Tabs.Back(A, 3);
            if (inNav)
            {
                switch (key)
                {
                    case "left": nav.SetFocus(Math.Max(0, nav.Focus - 1)); return true;
                    case "right": nav.SetFocus(Math.Min(NavBar.Tabs.Length - 1, nav.Focus + 1)); return true;
                    case "enter": return Tabs.Open(A, nav.Focus, 3);
                    case "down": inNav = false; nav.SetFocus(-1); ApplyFocus(true); return true;
                }
                return true;
            }
            if (key == "up" && pos.Row == 0) { inNav = true; ApplyFocus(true); nav.SetFocus(3); return true; }
            if (key != "up" && key != "down" && key != "left" && key != "right" && key != "enter") return false;
            var res = SettingsModel.Key(rows, A.Prefs, pos, key);
            pos = res.Pos;
            if (res.Action != null) { Act(res.Action); return true; }
            if (!res.Prefs.Equals(A.Prefs))
            {
                Telemetry.Report("prefs", Telemetry.F("row", rows[Math.Min(pos.Row, rows.Count - 1)].Id, "json", PrefsLogic.Serialize(res.Prefs)));
                A.SetPrefs(res.Prefs); // saves + rebuilds this page with the new theme
                return true;
            }
            ApplyFocus(true);
            return true;
        }

        void Act(string action)
        {
            Telemetry.Report("settings_action", Telemetry.F("action", action));
            switch (action)
            {
                case "reset":
                    A.SetPrefs(PrefsLogic.Reset(A.Store));
                    break;
                case "sign-out": A.SignOut(); break;
                case "debug": A.SetDebug(!A.Debug); A.Rebuild(this); break;
                case "patterns": A.Push(new PatternsScreen()); break;
                case "clear-cache": try { A.Store.Remove(HomeCache.Key); } catch (Exception) { } break;
            }
        }

        public override void OnClose() { nav?.Stop(); }
    }
}
