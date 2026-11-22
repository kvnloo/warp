import { memo } from "react";
import type { MediaItem } from "@warp/plex";
import { SAFE_X, gridMetrics, gridRowHeight, gridWindow } from "../layout";
import type { PosterSize, TextSize } from "../prefs";
import { Card, type ImageFn } from "./Card";

const GRID_TOP = 300;

/** Virtualized poster grid: a 10k library still mounts only ~4 rows of cards. */
export const LibraryGrid = memo(function LibraryGrid({ title, items, index, active, loading, posterSize, textSize, image }: {
  title: string;
  items: MediaItem[];
  index: number;
  active: boolean;
  loading: boolean;
  posterSize: PosterSize;
  textSize: TextSize;
  image: ImageFn;
}) {
  const { metrics, cols } = gridMetrics(posterSize);
  const rowH = gridRowHeight({ posterSize, textSize });
  const rows = Math.max(1, Math.ceil((1080 - GRID_TOP) / rowH));
  const win = gridWindow(index, items.length, cols, rows);
  const step = metrics.width + metrics.gap;
  const cards = [];
  if (loading) {
    for (let n = 0; n < cols * 2; n += 1) {
      cards.push(
        <div key={n} className="card" style={{ left: (n % cols) * step, top: Math.floor(n / cols) * rowH, width: metrics.width }}>
          <div className="card-art sk-block" style={{ width: metrics.width, height: metrics.height }} />
          <div className="card-title"><span className="sk-line" style={{ width: "70%" }} /></div>
        </div>,
      );
    }
  } else {
    for (let n = win.start; n < win.end; n += 1) {
      const item = items[n];
      cards.push(<Card key={item.ratingKey} item={item} focused={active && n === index} metrics={metrics} x={(n % cols) * step} y={Math.floor(n / cols) * rowH} image={image} />);
    }
  }
  return (
    <section className="library">
      <div className="library-head" style={{ left: SAFE_X }}>
        <h1 className="page-title">{title}</h1>
        <p className="page-sub">{loading ? "Loading…" : `${items.length.toLocaleString()} titles${items.length ? ` · ${Math.min(items.length, index + 1).toLocaleString()} of ${items.length.toLocaleString()}` : ""}`}</p>
      </div>
      <div className="library-viewport" style={{ top: GRID_TOP - 40 }}>
        <div className="library-track" style={{ transform: `translate3d(0,${-win.scrollRow * rowH}px,0)` }}>{cards}</div>
      </div>
    </section>
  );
});
