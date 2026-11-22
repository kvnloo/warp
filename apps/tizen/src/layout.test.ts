import { describe, expect, it } from "vitest";
import { defaultPrefs } from "./prefs";
import { HERO_H, NAV_H, cardMetrics, gridWindow, homeScrollY, railHeight, railTops, railWindow, visibleCards } from "./layout";

describe("layout", () => {
  it("uses 16:9 cards for Continue Watching and 2:3 posters elsewhere", () => {
    const prefs = defaultPrefs();
    const wide = cardMetrics("continue", prefs);
    expect(wide.landscape).toBe(true);
    expect(Math.round((wide.width / wide.height) * 9)).toBe(16);
    expect(cardMetrics("recent", prefs)).toMatchObject({ width: 228, height: 342, landscape: false });
  });

  it("stacks rail tops by each rail's own height", () => {
    const prefs = defaultPrefs();
    const tops = railTops(["continue", "recent"], prefs);
    expect(tops).toEqual([0, railHeight("continue", prefs)]);
    expect(railHeight("recent", { ...prefs, textSize: "l" })).toBeGreaterThan(railHeight("recent", prefs));
  });

  it("renders only a window of cards around focus", () => {
    const visible = visibleCards(cardMetrics("recent", defaultPrefs()));
    expect(visible).toBe(6);
    expect(railWindow(0, 50, visible)).toEqual({ scroll: 0, start: 0, end: 8 });
    expect(railWindow(20, 50, visible)).toEqual({ scroll: 19, start: 17, end: 27 });
    expect(railWindow(49, 50, visible).scroll).toBe(44);
    expect(railWindow(3, 4, visible)).toEqual({ scroll: 0, start: 0, end: 4 });
  });

  it("windows a 10k library grid to a few rows", () => {
    const win = gridWindow(6000, 10_000, 6, 3);
    expect(win.end - win.start).toBeLessThanOrEqual(6 * 5);
    expect(win.start).toBeLessThanOrEqual(6000);
    expect(win.end).toBeGreaterThan(6000);
  });

  it("scrolls Home so the focused rail sits under the nav", () => {
    const tops = [0, 500, 1000];
    expect(homeScrollY(null, tops, true)).toBe(0);
    expect(homeScrollY(0, tops, true)).toBe(HERO_H - NAV_H - 8);
    expect(homeScrollY(2, tops, true)).toBe(HERO_H + 1000 - NAV_H - 8);
    expect(homeScrollY(0, tops, false)).toBe(0);
    expect(homeScrollY(9, tops, false)).toBe(1000);
  });
});
