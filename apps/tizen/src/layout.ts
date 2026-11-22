/**
 * Pixel layout for the 1920x1080 canvas, as pure functions so scrolling is a
 * transform computed in JS (no layout reads, no scrollIntoView) and testable.
 */
import { posterMetrics, type PosterSize, type Prefs, type RailId } from "./prefs";

export const SAFE_X = 96;
export const NAV_H = 132;
/** Rails start here when the hero is on; the hero art itself is full-bleed behind the nav. */
export const HERO_H = 720;
export const CONTENT_W = 1920 - 2 * SAFE_X;
const TEXT_SCALE = { s: 0.9, m: 1, l: 1.14 } as const;

export interface CardMetrics {
  width: number;
  height: number;
  gap: number;
  landscape: boolean;
}

/** Continue Watching uses 16:9 thumbnails with progress, the rest 2:3 posters. */
export function cardMetrics(kind: RailId, prefs: Pick<Prefs, "posterSize">): CardMetrics {
  const poster = posterMetrics(prefs.posterSize);
  if (kind !== "continue") return { ...poster, landscape: false };
  const height = Math.round(poster.height * 0.66);
  return { width: Math.round((height * 16) / 9), height, gap: poster.gap, landscape: true };
}

export function textScale(prefs: Pick<Prefs, "textSize">): number {
  return TEXT_SCALE[prefs.textSize];
}

/** Rail = title + cards + two caption lines + breathing room. */
export function railHeight(kind: RailId, prefs: Pick<Prefs, "posterSize" | "textSize">): number {
  const ts = textScale(prefs);
  return Math.round(34 * ts + 24) + cardMetrics(kind, prefs).height + Math.round(66 * ts + 18) + 40;
}

export function railTops(kinds: readonly RailId[], prefs: Pick<Prefs, "posterSize" | "textSize">): number[] {
  const tops: number[] = [];
  let y = 0;
  for (const kind of kinds) {
    tops.push(y);
    y += railHeight(kind, prefs);
  }
  return tops;
}

/** How many cards fit across the safe area (partial last one excluded). */
export function visibleCards(metrics: CardMetrics): number {
  return Math.max(1, Math.floor((CONTENT_W + metrics.gap) / (metrics.width + metrics.gap)));
}

export interface RailWindow {
  /** Index of the first card at the left edge (the track is shifted by this). */
  scroll: number;
  start: number;
  end: number;
}

/** Keep the focused card second from the left once the rail scrolls; render only a few extra. */
export function railWindow(focusCol: number, count: number, visible: number, overscan = 2): RailWindow {
  const maxScroll = Math.max(0, count - visible);
  const scroll = Math.min(maxScroll, Math.max(0, focusCol - 1));
  return { scroll, start: Math.max(0, scroll - overscan), end: Math.min(count, scroll + visible + overscan) };
}

/** Library grid: rows rendered around the focused row only. */
export function gridWindow(focusIndex: number, count: number, cols: number, visibleRows: number, overscan = 1) {
  const rows = Math.ceil(count / cols);
  const focusRow = Math.floor(Math.max(0, focusIndex) / cols);
  const maxScroll = Math.max(0, rows - visibleRows);
  const scrollRow = Math.min(maxScroll, Math.max(0, focusRow - 1));
  const startRow = Math.max(0, scrollRow - overscan);
  const endRow = Math.min(rows, scrollRow + visibleRows + overscan);
  return { scrollRow, start: startRow * cols, end: Math.min(count, endRow * cols) };
}

/** Home vertical scroll: nav/hero at the top, otherwise the focused rail sits just under the nav. */
export function homeScrollY(row: number | null, tops: readonly number[], heroOn: boolean): number {
  if (row == null || row < 0 || !tops.length) return 0;
  const railsStart = heroOn ? HERO_H : NAV_H + 8;
  return Math.max(0, railsStart + (tops[Math.min(row, tops.length - 1)] ?? 0) - NAV_H - 8);
}

/** Library grid: column count from the poster size, then widths stretched so the grid spans the safe area. */
export function gridMetrics(size: PosterSize): { metrics: CardMetrics; cols: number } {
  const base = posterMetrics(size);
  const cols = Math.max(1, Math.round((CONTENT_W + base.gap) / (base.width + base.gap)));
  const gap = size === "s" ? 24 : 28;
  const width = Math.floor((CONTENT_W - (cols - 1) * gap) / cols);
  return { metrics: { width, height: Math.round(width * 1.5), gap, landscape: false }, cols };
}

/** Library grid row pitch for the current poster/text size. */
export function gridRowHeight(prefs: Pick<Prefs, "posterSize" | "textSize">): number {
  return gridMetrics(prefs.posterSize).metrics.height + Math.round(66 * textScale(prefs) + 18) + 28;
}
