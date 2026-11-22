import { describe, expect, it } from "vitest";
import {
  DEFAULT_PREFS,
  PREFS_KEY,
  accentInk,
  cssVars,
  defaultPrefs,
  gridColumns,
  loadPrefs,
  moveRail,
  normalizeRails,
  parsePrefs,
  resetPrefs,
  rootAttributes,
  savePrefs,
  serializePrefs,
  toggleRail,
} from "./prefs";

function memoryStorage(initial: Record<string, string> = {}) {
  const data = { ...initial };
  return {
    data,
    getItem: (key: string) => (key in data ? data[key] : null),
    setItem: (key: string, value: string) => { data[key] = value; },
    removeItem: (key: string) => { delete data[key]; },
  };
}

describe("prefs storage", () => {
  it("uses a versioned key and round-trips every field", () => {
    expect(PREFS_KEY).toBe("warp.prefs.v1");
    const storage = memoryStorage();
    const prefs = { ...defaultPrefs(), accent: "teal" as const, posterSize: "l" as const, hero: false, reducedMotion: true };
    savePrefs(storage, prefs);
    expect(JSON.parse(storage.data[PREFS_KEY]).v).toBe(1);
    expect(loadPrefs(storage)).toEqual(prefs);
  });

  it("falls back to defaults on missing, corrupt or wrong-shape data", () => {
    expect(parsePrefs(null)).toEqual(DEFAULT_PREFS);
    expect(parsePrefs("{not json")).toEqual(DEFAULT_PREFS);
    expect(parsePrefs("42")).toEqual(DEFAULT_PREFS);
    expect(parsePrefs("null")).toEqual(DEFAULT_PREFS);
    expect(loadPrefs({ getItem: () => { throw new Error("denied"); }, setItem() {}, removeItem() {} })).toEqual(DEFAULT_PREFS);
  });

  it("keeps valid fields when one field is corrupt", () => {
    const prefs = parsePrefs(JSON.stringify({ v: 1, prefs: { accent: "nope", posterSize: "s", clock: "yes", focusStyle: "lift" } }));
    expect(prefs.accent).toBe(DEFAULT_PREFS.accent);
    expect(prefs.clock).toBe(DEFAULT_PREFS.clock);
    expect(prefs.posterSize).toBe("s");
    expect(prefs.focusStyle).toBe("lift");
  });

  it("never throws when storage is full", () => {
    expect(() => savePrefs({ getItem: () => null, setItem() { throw new Error("quota"); }, removeItem() {} }, defaultPrefs())).not.toThrow();
  });

  it("reset removes the stored key and returns fresh defaults", () => {
    const storage = memoryStorage({ [PREFS_KEY]: serializePrefs({ ...defaultPrefs(), accent: "rose" }) });
    const prefs = resetPrefs(storage);
    expect(storage.data[PREFS_KEY]).toBeUndefined();
    expect(prefs).toEqual(DEFAULT_PREFS);
    prefs.rails[0].visible = false;
    expect(DEFAULT_PREFS.rails[0].visible).toBe(true);
  });
});

describe("home rails prefs", () => {
  it("drops unknown and duplicate rails and appends missing ones", () => {
    const rails = normalizeRails([{ id: "recent", visible: false }, { id: "bogus" }, { id: "recent", visible: true }, "x"]);
    expect(rails[0]).toEqual({ id: "recent", visible: false });
    expect(rails.map((rail) => rail.id).sort()).toEqual(["continue", "more", "recent", "recent-movies", "recent-shows"]);
  });

  it("moves and toggles without mutating the input", () => {
    const rails = defaultPrefs().rails;
    const moved = moveRail(rails, "recent-shows", -1);
    expect(moved.map((rail) => rail.id).slice(0, 3)).toEqual(["continue", "recent-shows", "recent-movies"]);
    expect(moveRail(rails, "continue", -1)).toEqual(rails);
    expect(toggleRail(rails, "continue")[0].visible).toBe(false);
    expect(rails[0].visible).toBe(true);
  });
});

describe("css variables", () => {
  it("emits plain values the Chromium 85 runtime understands", () => {
    const vars = cssVars({ ...defaultPrefs(), accent: "sky", radius: "round", posterSize: "l", textSize: "l", reducedMotion: true });
    expect(vars["--accent"]).toBe("#4aa8ff");
    expect(vars["--accent-rgb"]).toBe("74,168,255");
    expect(vars["--radius"]).toBe("22px");
    expect(vars["--poster-w"]).toBe("276px");
    expect(vars["--ts"]).toBe("1.14");
    expect(vars["--dur"]).toBe("0ms");
    for (const value of Object.values(vars)) expect(value).not.toMatch(/color-mix|oklch|clamp\(|min\(|max\(/);
  });

  it("switches focus and background styles via root attributes", () => {
    expect(rootAttributes({ ...defaultPrefs(), focusStyle: "ring", background: "solid" })).toEqual({ "data-focus": "ring", "data-bg": "solid", "data-motion": "full" });
  });

  it("picks dark ink on light accents and white on dark ones", () => {
    expect(accentInk("#e8eef5")).toBe("#0b0d10");
    expect(accentInk("#e5a00d")).toBe("#0b0d10");
    expect(accentInk("#1a237e")).toBe("#ffffff");
  });

  it("fits fewer grid columns as posters grow", () => {
    expect(gridColumns("s")).toBeGreaterThan(gridColumns("m"));
    expect(gridColumns("m")).toBeGreaterThan(gridColumns("l"));
    expect(gridColumns("m")).toBe(6);
  });
});
