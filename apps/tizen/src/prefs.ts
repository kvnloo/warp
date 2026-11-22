/**
 * Appearance preferences: pure data, parsing, migration and CSS variables.
 * No DOM access here so every rule is unit-testable; theme.ts applies the result.
 */

export const PREFS_KEY = "warp.prefs.v1";
export const PREFS_VERSION = 1;

export const ACCENTS = {
  gold: { label: "Gold", hex: "#e5a00d" },
  ember: { label: "Ember", hex: "#ff6b3d" },
  rose: { label: "Rose", hex: "#ff5d7a" },
  teal: { label: "Teal", hex: "#2ec4b6" },
  sky: { label: "Sky", hex: "#4aa8ff" },
  lime: { label: "Lime", hex: "#a3d936" },
  ice: { label: "Ice", hex: "#e8eef5" },
} as const;

export type AccentId = keyof typeof ACCENTS;
export type BackgroundStyle = "solid" | "gradient" | "backdrop";
export type PosterSize = "s" | "m" | "l";
export type Radius = "sharp" | "soft" | "round";
export type FocusStyle = "ring" | "glow" | "lift";
export type TextSize = "s" | "m" | "l";
export type RailId = "continue" | "recent-movies" | "recent-shows" | "recent" | "more";

export interface RailPref {
  id: RailId;
  visible: boolean;
}

export interface Prefs {
  accent: AccentId;
  background: BackgroundStyle;
  posterSize: PosterSize;
  radius: Radius;
  focusStyle: FocusStyle;
  textSize: TextSize;
  hero: boolean;
  clock: boolean;
  reducedMotion: boolean;
  rails: RailPref[];
}

export const RAIL_LABELS: Record<RailId, string> = {
  continue: "Continue Watching",
  "recent-movies": "Recently Added Movies",
  "recent-shows": "Recently Added Shows",
  recent: "Recently Added",
  more: "More from your server",
};

export const RAIL_IDS: readonly RailId[] = ["continue", "recent-movies", "recent-shows", "recent", "more"];

export const BACKGROUNDS: readonly BackgroundStyle[] = ["solid", "gradient", "backdrop"];
export const POSTER_SIZES: readonly PosterSize[] = ["s", "m", "l"];
export const RADII: readonly Radius[] = ["sharp", "soft", "round"];
export const FOCUS_STYLES: readonly FocusStyle[] = ["ring", "glow", "lift"];
export const TEXT_SIZES: readonly TextSize[] = ["s", "m", "l"];

export const DEFAULT_PREFS: Prefs = Object.freeze({
  accent: "gold",
  background: "backdrop",
  posterSize: "m",
  radius: "soft",
  focusStyle: "glow",
  textSize: "m",
  hero: true,
  clock: true,
  reducedMotion: false,
  rails: RAIL_IDS.map((id) => ({ id, visible: true })),
}) as Prefs;

export function defaultPrefs(): Prefs {
  return { ...DEFAULT_PREFS, rails: DEFAULT_PREFS.rails.map((rail) => ({ ...rail })) };
}

function pick<T extends string>(value: unknown, allowed: readonly T[], fallback: T): T {
  return typeof value === "string" && (allowed as readonly string[]).includes(value) ? value as T : fallback;
}

function bool(value: unknown, fallback: boolean): boolean {
  return typeof value === "boolean" ? value : fallback;
}

/** Keep known rails in stored order, drop unknown or duplicate ids, append rails added since. */
export function normalizeRails(value: unknown): RailPref[] {
  const out: RailPref[] = [];
  const seen = new Set<RailId>();
  if (Array.isArray(value)) {
    for (const entry of value) {
      const row = entry && typeof entry === "object" ? entry as Record<string, unknown> : {};
      const id = row.id;
      if (typeof id !== "string" || !(RAIL_IDS as readonly string[]).includes(id) || seen.has(id as RailId)) continue;
      seen.add(id as RailId);
      out.push({ id: id as RailId, visible: bool(row.visible, true) });
    }
  }
  for (const id of RAIL_IDS) if (!seen.has(id)) out.push({ id, visible: true });
  return out;
}

/** Field-by-field merge: one bad field never throws away the rest of the user's choices. */
export function normalizePrefs(value: unknown): Prefs {
  const raw = value && typeof value === "object" ? value as Record<string, unknown> : {};
  const d = DEFAULT_PREFS;
  return {
    accent: pick(raw.accent, Object.keys(ACCENTS) as AccentId[], d.accent),
    background: pick(raw.background, BACKGROUNDS, d.background),
    posterSize: pick(raw.posterSize, POSTER_SIZES, d.posterSize),
    radius: pick(raw.radius, RADII, d.radius),
    focusStyle: pick(raw.focusStyle, FOCUS_STYLES, d.focusStyle),
    textSize: pick(raw.textSize, TEXT_SIZES, d.textSize),
    hero: bool(raw.hero, d.hero),
    clock: bool(raw.clock, d.clock),
    reducedMotion: bool(raw.reducedMotion, d.reducedMotion),
    rails: normalizeRails(raw.rails),
  };
}

/** Parse the stored string. Corrupt JSON or a wrong shape falls back to defaults. */
export function parsePrefs(raw: string | null): Prefs {
  if (!raw) return defaultPrefs();
  try {
    const parsed = JSON.parse(raw) as unknown;
    if (!parsed || typeof parsed !== "object") return defaultPrefs();
    const envelope = parsed as Record<string, unknown>;
    // v1 envelope: { v: 1, prefs: {...} }. A bare object (pre-envelope) is migrated field by field.
    const body = "prefs" in envelope ? envelope.prefs : envelope;
    return normalizePrefs(body);
  } catch {
    return defaultPrefs();
  }
}

export function serializePrefs(prefs: Prefs): string {
  return JSON.stringify({ v: PREFS_VERSION, prefs: normalizePrefs(prefs) });
}

interface StorageLike {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

export function loadPrefs(storage: StorageLike): Prefs {
  try {
    return parsePrefs(storage.getItem(PREFS_KEY));
  } catch {
    return defaultPrefs();
  }
}

export function savePrefs(storage: StorageLike, prefs: Prefs): void {
  try {
    storage.setItem(PREFS_KEY, serializePrefs(prefs));
  } catch {
    /* quota or private mode: keep running with in-memory prefs */
  }
}

export function resetPrefs(storage: StorageLike): Prefs {
  try { storage.removeItem(PREFS_KEY); } catch { /* ignore */ }
  return defaultPrefs();
}

/** Move a rail up (-1) or down (+1). Out-of-range moves return the same order. */
export function moveRail(rails: readonly RailPref[], id: RailId, delta: -1 | 1): RailPref[] {
  const next = rails.map((rail) => ({ ...rail }));
  const index = next.findIndex((rail) => rail.id === id);
  const target = index + delta;
  if (index < 0 || target < 0 || target >= next.length) return next;
  [next[index], next[target]] = [next[target], next[index]];
  return next;
}

export function toggleRail(rails: readonly RailPref[], id: RailId): RailPref[] {
  return rails.map((rail) => rail.id === id ? { ...rail, visible: !rail.visible } : { ...rail });
}

export interface PosterMetrics {
  width: number;
  height: number;
  gap: number;
}

const POSTER_WIDTH: Record<PosterSize, number> = { s: 188, m: 228, l: 276 };

export function posterMetrics(size: PosterSize): PosterMetrics {
  const width = POSTER_WIDTH[size];
  return { width, height: Math.round(width * 1.5), gap: size === "s" ? 24 : 32 };
}

/** Columns that fit the 1920 px layout inside a 5% safe area on each side. */
export function gridColumns(size: PosterSize, contentWidth = 1920 - 2 * 96): number {
  const { width, gap } = posterMetrics(size);
  return Math.max(1, Math.floor((contentWidth + gap) / (width + gap)));
}

function hexToRgb(hex: string): [number, number, number] {
  const value = parseInt(hex.slice(1), 16);
  return [(value >> 16) & 255, (value >> 8) & 255, value & 255];
}

/** Readable ink for text placed on the accent (focused buttons). */
export function accentInk(hex: string): string {
  const [r, g, b] = hexToRgb(hex).map((c) => {
    const s = c / 255;
    return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
  });
  const luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
  return luminance > 0.35 ? "#0b0d10" : "#ffffff";
}

const RADIUS_PX: Record<Radius, number> = { sharp: 2, soft: 12, round: 22 };
const TEXT_SCALE: Record<TextSize, number> = { s: 0.9, m: 1, l: 1.14 };

/** CSS custom properties for :root. Plain px/rgb values only (Chromium 85 safe). */
export function cssVars(prefs: Prefs): Record<string, string> {
  const accent = ACCENTS[prefs.accent].hex;
  const poster = posterMetrics(prefs.posterSize);
  return {
    "--accent": accent,
    "--accent-rgb": hexToRgb(accent).join(","),
    "--accent-ink": accentInk(accent),
    "--radius": `${RADIUS_PX[prefs.radius]}px`,
    "--radius-sm": `${Math.max(2, Math.round(RADIUS_PX[prefs.radius] / 2))}px`,
    "--poster-w": `${poster.width}px`,
    "--poster-h": `${poster.height}px`,
    "--poster-gap": `${poster.gap}px`,
    "--ts": String(TEXT_SCALE[prefs.textSize]),
    "--dur": prefs.reducedMotion ? "0ms" : "160ms",
  };
}

/** data-* attributes on <html>: selectors switch whole styles without recomputing vars. */
export function rootAttributes(prefs: Prefs): Record<string, string> {
  return {
    "data-focus": prefs.focusStyle,
    "data-bg": prefs.background,
    "data-motion": prefs.reducedMotion ? "reduced" : "full",
  };
}
