/**
 * The Settings screen as data: rows, their choices, and D-pad behaviour.
 * Option rows are segmented controls: Left/Right moves AND applies (live preview).
 * Rail and action rows only act on Enter.
 */
import {
  ACCENTS,
  BACKGROUNDS,
  FOCUS_STYLES,
  POSTER_SIZES,
  RADII,
  RAIL_LABELS,
  TEXT_SIZES,
  moveRail,
  toggleRail,
  type AccentId,
  type Prefs,
  type RailId,
} from "./prefs";

export interface Choice {
  value: string;
  label: string;
  swatch?: string;
}

export type SettingsRow =
  | { kind: "option"; id: keyof Prefs; group: string; label: string; hint: string; choices: Choice[] }
  | { kind: "rail"; id: RailId; group: string; label: string; visible: boolean; index: number; count: number }
  | { kind: "action"; id: string; group: string; label: string; hint: string; buttons: Choice[] };

export interface SettingsPos {
  row: number;
  col: number;
}

export type SettingsKey = "up" | "down" | "left" | "right" | "enter";

export interface SettingsResult {
  pos: SettingsPos;
  prefs: Prefs;
  action?: string;
}

const ON_OFF: Choice[] = [{ value: "on", label: "On" }, { value: "off", label: "Off" }];
const SIZE_LABEL: Record<string, string> = { s: "Small", m: "Medium", l: "Large" };

const OPTION_ROWS: Array<Extract<SettingsRow, { kind: "option" }>> = [
  {
    kind: "option", id: "accent", group: "Appearance", label: "Accent colour", hint: "Focus ring, buttons and progress bars.",
    choices: (Object.keys(ACCENTS) as AccentId[]).map((id) => ({ value: id, label: ACCENTS[id].label, swatch: ACCENTS[id].hex })),
  },
  {
    kind: "option", id: "background", group: "Appearance", label: "Background", hint: "Backdrop follows the focused title, dimmed.",
    choices: BACKGROUNDS.map((value) => ({ value, label: value === "solid" ? "Solid" : value === "gradient" ? "Gradient" : "Backdrop" })),
  },
  {
    kind: "option", id: "posterSize", group: "Appearance", label: "Poster size", hint: "Larger posters show fewer per row.",
    choices: POSTER_SIZES.map((value) => ({ value, label: SIZE_LABEL[value] })),
  },
  {
    kind: "option", id: "radius", group: "Appearance", label: "Corners",
    hint: "Corner rounding on posters and buttons.",
    choices: RADII.map((value) => ({ value, label: value === "sharp" ? "Sharp" : value === "soft" ? "Soft" : "Round" })),
  },
  {
    kind: "option", id: "focusStyle", group: "Appearance", label: "Focus style", hint: "How the selected poster stands out.",
    choices: FOCUS_STYLES.map((value) => ({ value, label: value === "ring" ? "Ring" : value === "glow" ? "Glow" : "Lift" })),
  },
  {
    kind: "option", id: "textSize", group: "Appearance", label: "Text size", hint: "Scales every label on screen.",
    choices: TEXT_SIZES.map((value) => ({ value, label: SIZE_LABEL[value] })),
  },
  { kind: "option", id: "hero", group: "Appearance", label: "Featured title", hint: "Large banner at the top of Home.", choices: ON_OFF },
  { kind: "option", id: "clock", group: "Appearance", label: "Clock", hint: "Time in the top bar.", choices: ON_OFF },
  { kind: "option", id: "reducedMotion", group: "Appearance", label: "Reduce motion", hint: "Focus changes snap instead of easing.", choices: ON_OFF },
];

export const RAIL_BUTTONS: Choice[] = [
  { value: "toggle", label: "Shown" },
  { value: "up", label: "Move up" },
  { value: "down", label: "Move down" },
];

export function buildSettingsRows(prefs: Prefs, extra: { debug: boolean }): SettingsRow[] {
  const rows: SettingsRow[] = [...OPTION_ROWS];
  prefs.rails.forEach((rail, index) => {
    rows.push({ kind: "rail", id: rail.id, group: "Home rows", label: RAIL_LABELS[rail.id], visible: rail.visible, index, count: prefs.rails.length });
  });
  rows.push({
    kind: "action", id: "reset", group: "Home rows", label: "Reset appearance", hint: "Restore every appearance and Home row setting.",
    buttons: [{ value: "reset", label: "Reset to defaults" }],
  });
  rows.push({
    kind: "action", id: "account", group: "Server & account", label: "Plex account", hint: "Sign out to link a different account or server.",
    buttons: [{ value: "sign-out", label: "Sign out" }],
  });
  rows.push({
    kind: "action", id: "playback", group: "Playback & display", label: "Diagnostics", hint: "Timing receipt overlay and TV calibration patterns.",
    buttons: [
      { value: "debug", label: extra.debug ? "Hide timings" : "Show timings" },
      { value: "patterns", label: "Display patterns" },
    ],
  });
  rows.push({
    kind: "action", id: "cache", group: "Cache", label: "Home cache", hint: "Cached Home loads instantly at launch.",
    buttons: [{ value: "clear-cache", label: "Clear cache" }],
  });
  return rows;
}

export function optionValue(prefs: Prefs, id: keyof Prefs): string {
  const value = prefs[id];
  if (typeof value === "boolean") return value ? "on" : "off";
  return String(value);
}

function setOption(prefs: Prefs, id: keyof Prefs, value: string): Prefs {
  const current = prefs[id];
  if (typeof current === "boolean") return { ...prefs, [id]: value === "on" };
  return { ...prefs, [id]: value };
}

export function rowWidth(row: SettingsRow): number {
  if (row.kind === "option") return row.choices.length;
  if (row.kind === "rail") return RAIL_BUTTONS.length;
  return row.buttons.length;
}

/** The column focus lands on when entering a row vertically. */
export function entryCol(row: SettingsRow, prefs: Prefs, fromCol: number): number {
  if (row.kind === "option") {
    const index = row.choices.findIndex((choice) => choice.value === optionValue(prefs, row.id));
    return Math.max(0, index);
  }
  return Math.min(Math.max(0, fromCol), rowWidth(row) - 1);
}

export function settingsKey(rows: SettingsRow[], prefs: Prefs, pos: SettingsPos, key: SettingsKey): SettingsResult {
  const row = rows[pos.row];
  if (!row) return { pos: { row: 0, col: 0 }, prefs };
  if (key === "up" || key === "down") {
    const target = pos.row + (key === "down" ? 1 : -1);
    if (target < 0 || target >= rows.length) return { pos, prefs };
    return { pos: { row: target, col: entryCol(rows[target], prefs, pos.col) }, prefs };
  }
  if (key === "left" || key === "right") {
    const col = Math.min(rowWidth(row) - 1, Math.max(0, pos.col + (key === "right" ? 1 : -1)));
    if (row.kind === "option") {
      return { pos: { row: pos.row, col }, prefs: setOption(prefs, row.id, row.choices[col].value) };
    }
    return { pos: { row: pos.row, col }, prefs };
  }
  // enter
  if (row.kind === "option") {
    return { pos, prefs: setOption(prefs, row.id, row.choices[pos.col]?.value ?? row.choices[0].value) };
  }
  if (row.kind === "rail") {
    const button = RAIL_BUTTONS[pos.col]?.value;
    if (button === "toggle") return { pos, prefs: { ...prefs, rails: toggleRail(prefs.rails, row.id) } };
    const delta = button === "up" ? -1 : 1;
    const rails = moveRail(prefs.rails, row.id, delta);
    const moved = rails.findIndex((rail) => rail.id === row.id) - prefs.rails.findIndex((rail) => rail.id === row.id);
    // Focus follows the moved rail so repeated Enter keeps moving it.
    return { pos: { row: pos.row + moved, col: pos.col }, prefs: { ...prefs, rails } };
  }
  return { pos, prefs, action: row.buttons[pos.col]?.value };
}
