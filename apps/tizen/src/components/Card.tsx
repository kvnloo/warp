import { memo, type SyntheticEvent } from "react";
import type { MediaItem } from "@warp/plex";
import type { CardMetrics } from "../layout";

export type ImageFn = (path: string | undefined, width: number, height: number) => string;

export function cardText(item: MediaItem): { title: string; sub: string } {
  if (item.type === "episode") {
    return { title: item.grandparentTitle || item.title, sub: `S${item.parentIndex ?? "?"} · E${item.index ?? "?"}  ${item.title}` };
  }
  if (item.type === "show") return { title: item.title, sub: [item.year, item.leafCount ? `${item.leafCount} episodes` : ""].filter(Boolean).join(" · ") };
  if (item.type === "season") return { title: item.title, sub: item.leafCount ? `${item.leafCount} episodes` : "" };
  return { title: item.title, sub: item.year ? String(item.year) : "" };
}

export function progressOf(item: MediaItem): number {
  if (!item.viewOffset || !item.duration) return 0;
  return Math.min(1, Math.max(0, item.viewOffset / item.duration));
}

function reveal(event: SyntheticEvent<HTMLImageElement>): void {
  event.currentTarget.style.opacity = "1";
}

function hide(event: SyntheticEvent<HTMLImageElement>): void {
  event.currentTarget.style.display = "none";
}

function initials(title: string): string {
  return title.split(/\s+/).filter((word) => /^[A-Za-z0-9]/.test(word)).slice(0, 2).map((word) => word[0]).join("").toUpperCase();
}

/** One poster or 16:9 tile. Positioned once with left/top; focus only flips a class. */
export const Card = memo(function Card({ item, focused, metrics, x, y, image }: {
  item: MediaItem;
  focused: boolean;
  metrics: CardMetrics;
  x: number;
  y: number;
  image: ImageFn;
}) {
  const path = metrics.landscape ? (item.type === "episode" ? item.thumb : item.art || item.thumb) : item.thumb;
  const src = image(path, metrics.width, metrics.height);
  const { title, sub } = cardText(item);
  const progress = progressOf(item);
  const watched = !progress && (item.viewCount ?? 0) > 0;
  return (
    <div className={focused ? "card is-focused" : "card"} style={{ left: x, top: y, width: metrics.width }}>
      <div className="card-art" style={{ width: metrics.width, height: metrics.height }}>
        <span className="card-ph">{initials(title)}</span>
        {src && <img alt="" src={src} decoding="async" onLoad={reveal} onError={hide} />}
        {progress > 0 && <span className="card-progress"><i style={{ transform: `scaleX(${progress.toFixed(3)})` }} /></span>}
        {watched && <span className="card-badge">✓</span>}
      </div>
      <div className="card-title">{title}</div>
      {sub && <div className="card-sub">{sub}</div>}
    </div>
  );
});
