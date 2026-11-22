import type { MediaItem } from "@warp/plex";

export function runtime(ms: number | undefined): string {
  if (!ms) return "";
  const minutes = Math.round(ms / 60_000);
  const hours = Math.floor(minutes / 60);
  return hours ? `${hours}h ${minutes % 60}m` : `${minutes}m`;
}

export function remaining(item: MediaItem): string {
  if (!item.viewOffset || !item.duration) return "";
  return `${runtime(item.duration - item.viewOffset)} left`;
}

/** "2024 · PG-13 · 2h 14m · ★ 8.1" — skips whatever the server did not send. */
export function metaLine(item: MediaItem): string {
  const parts: string[] = [];
  if (item.type === "episode") parts.push(`S${item.parentIndex ?? "?"} E${item.index ?? "?"} · ${item.title}`);
  if (item.year) parts.push(String(item.year));
  if (item.contentRating) parts.push(item.contentRating);
  if (item.duration) parts.push(remaining(item) || runtime(item.duration));
  if (item.type === "show" && item.leafCount) parts.push(`${item.leafCount} episodes`);
  if (item.audienceRating) parts.push(`★ ${item.audienceRating.toFixed(1)}`);
  return parts.join("  ·  ");
}
