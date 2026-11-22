import { describe, expect, it } from "vitest";
import { defaultPrefs } from "./prefs";
import { buildSettingsRows, entryCol, settingsKey } from "./settings-model";

const rows = (prefs = defaultPrefs()) => buildSettingsRows(prefs, { debug: false });

describe("settings D-pad model", () => {
  it("lands on the current choice when entering an option row", () => {
    const prefs = defaultPrefs();
    const all = rows(prefs);
    const accentRow = all.findIndex((row) => row.id === "accent");
    const result = settingsKey(all, prefs, { row: accentRow, col: 0 }, "down");
    const target = all[result.pos.row];
    expect(target.id).toBe("background");
    expect(result.pos.col).toBe(entryCol(target, prefs, 0));
    expect(result.pos.col).toBe(2); // backdrop is the default
  });

  it("applies option choices live on Left/Right", () => {
    const prefs = defaultPrefs();
    const all = rows(prefs);
    const row = all.findIndex((r) => r.id === "accent");
    const result = settingsKey(all, prefs, { row, col: 0 }, "right");
    expect(result.prefs.accent).toBe("ember");
    const clamped = settingsKey(all, prefs, { row, col: 0 }, "left");
    expect(clamped.pos.col).toBe(0);
  });

  it("toggles booleans through On/Off choices", () => {
    const prefs = defaultPrefs();
    const all = rows(prefs);
    const row = all.findIndex((r) => r.id === "hero");
    expect(settingsKey(all, prefs, { row, col: 0 }, "right").prefs.hero).toBe(false);
  });

  it("reorders rails with focus following the moved rail", () => {
    const prefs = defaultPrefs();
    const all = rows(prefs);
    const row = all.findIndex((r) => r.kind === "rail" && r.id === "recent-movies");
    const moved = settingsKey(all, prefs, { row, col: 1 }, "enter");
    expect(moved.prefs.rails[0].id).toBe("recent-movies");
    expect(moved.pos.row).toBe(row - 1);
    const hidden = settingsKey(all, prefs, { row, col: 0 }, "enter");
    expect(hidden.prefs.rails.find((rail) => rail.id === "recent-movies")?.visible).toBe(false);
  });

  it("returns actions for buttons without changing prefs", () => {
    const prefs = defaultPrefs();
    const all = rows(prefs);
    const row = all.findIndex((r) => r.id === "reset");
    const result = settingsKey(all, prefs, { row, col: 0 }, "enter");
    expect(result.action).toBe("reset");
    expect(result.prefs).toBe(prefs);
  });

  it("stops at the top and bottom", () => {
    const prefs = defaultPrefs();
    const all = rows(prefs);
    expect(settingsKey(all, prefs, { row: 0, col: 0 }, "up").pos.row).toBe(0);
    expect(settingsKey(all, prefs, { row: all.length - 1, col: 0 }, "down").pos.row).toBe(all.length - 1);
  });
});
