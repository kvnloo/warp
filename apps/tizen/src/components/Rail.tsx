import { memo } from "react";
import type { MediaItem } from "@warp/plex";
import { cardMetrics, railWindow, visibleCards } from "../layout";
import type { PosterSize, RailId } from "../prefs";
import { Card, type ImageFn } from "./Card";

/** Horizontal row: the track moves by transform, and only ~10 cards exist at once. */
export const Rail = memo(function Rail({ kind, title, items, top, col, focused, posterSize, image }: {
  kind: RailId;
  title: string;
  items: MediaItem[];
  top: number;
  col: number;
  focused: boolean;
  posterSize: PosterSize;
  image: ImageFn;
}) {
  const metrics = cardMetrics(kind, { posterSize });
  const step = metrics.width + metrics.gap;
  const win = railWindow(col, items.length, visibleCards(metrics));
  const cards = [];
  for (let index = win.start; index < win.end; index += 1) {
    const item = items[index];
    cards.push(<Card key={item.ratingKey} item={item} focused={focused && index === col} metrics={metrics} x={index * step} y={0} image={image} />);
  }
  return (
    <section className={focused ? "rail is-focused" : "rail"} style={{ top }}>
      <h2 className="rail-title">{title}<span className="rail-count">{focused ? `${col + 1} / ${items.length}` : ""}</span></h2>
      <div className="rail-track" style={{ height: metrics.height, transform: `translate3d(${-win.scroll * step}px,0,0)` }}>{cards}</div>
    </section>
  );
});

export function SkeletonRail({ top, landscape, posterSize }: { top: number; landscape: boolean; posterSize: PosterSize }) {
  const metrics = cardMetrics(landscape ? "continue" : "recent", { posterSize });
  const count = visibleCards(metrics) + 1;
  return (
    <section className="rail skeleton" style={{ top }}>
      <h2 className="rail-title"><span className="sk-line" style={{ width: 280 }} /></h2>
      <div className="rail-track" style={{ height: metrics.height }}>
        {Array.from({ length: count }, (_, index) => (
          <div key={index} className="card" style={{ left: index * (metrics.width + metrics.gap), top: 0, width: metrics.width }}>
            <div className="card-art sk-block" style={{ width: metrics.width, height: metrics.height }} />
            <div className="card-title"><span className="sk-line" style={{ width: "70%" }} /></div>
          </div>
        ))}
      </div>
    </section>
  );
}
