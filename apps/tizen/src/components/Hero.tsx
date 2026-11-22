import { memo } from "react";
import type { MediaItem } from "@warp/plex";
import { progressOf, type ImageFn } from "./Card";
import { metaLine } from "./meta";

/** Featured title over full-bleed art. Art paints after text; the scrims are plain gradients. */
export const Hero = memo(function Hero({ item, label, focusCol, away = false, image }: {
  item: MediaItem | undefined;
  label: string;
  focusCol: number;
  /** Focus is down in the rails: text fades so it never sits under the nav. */
  away?: boolean;
  image: ImageFn;
}) {
  if (!item) {
    return (
      <section className="hero skeleton">
        <div className="hero-body">
          <span className="sk-line" style={{ width: 220, height: 24 }} />
          <span className="sk-line" style={{ width: 760, height: 84, marginTop: 24 }} />
          <span className="sk-line" style={{ width: 980, height: 28, marginTop: 28 }} />
          <span className="sk-line" style={{ width: 860, height: 28, marginTop: 14 }} />
        </div>
      </section>
    );
  }
  const art = image(item.type === "episode" ? item.grandparentArt || item.art : item.art, 1600, 900);
  const progress = progressOf(item);
  const title = item.type === "episode" ? item.grandparentTitle || item.title : item.title;
  return (
    <section className={away ? "hero is-away" : "hero"}>
      {art && <img className="hero-art" alt="" src={art} decoding="async" onLoad={(event) => { event.currentTarget.style.opacity = "1"; }} />}
      <div className="scrim-left" />
      <div className="scrim-bottom" />
      <div className="hero-body">
        <p className="overline">{label}</p>
        <h1 className="hero-title">{title}</h1>
        <p className="meta-line">{metaLine(item)}</p>
        {item.summary && <p className="hero-summary">{item.summary}</p>}
        <div className="actions">
          <span className={`btn btn-primary${focusCol === 0 ? " is-focused" : ""}`}>
            <i className="icon-play" />{progress > 0 ? "Resume" : "Play"}
            {progress > 0 && <span className="btn-progress"><i style={{ transform: `scaleX(${progress.toFixed(3)})` }} /></span>}
          </span>
          <span className={`btn${focusCol === 1 ? " is-focused" : ""}`}>More info</span>
        </div>
      </div>
    </section>
  );
});
