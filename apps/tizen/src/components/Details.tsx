import { memo } from "react";
import type { PlaybackDecision } from "@warp/plex";
import { detailActions, type DetailsRow, type DetailsState } from "../details-model";
import type { PosterSize } from "../prefs";
import type { ImageFn } from "./Card";
import { metaLine } from "./meta";
import { Rail } from "./Rail";

function chips(decision: PlaybackDecision | null): string[] {
  if (!decision || decision.videoCodec === "unknown") return [];
  const out: string[] = [];
  const height = Number(decision.resolution.split("x")[1] || 0);
  if (height >= 2000) out.push("4K");
  else if (height >= 1000) out.push("HD");
  if (decision.hdr === "hdr10" || decision.hdr === "hdr10+" || decision.hdr === "hlg") out.push(decision.hdr.toUpperCase());
  if (decision.hdr.startsWith("dolby")) out.push("DOLBY VISION");
  out.push(decision.videoCodec.toUpperCase());
  if (decision.audioCodec !== "none") out.push(decision.audioCodec.toUpperCase());
  out.push(decision.mode === "direct-play" ? "DIRECT PLAY" : decision.mode === "direct-stream" ? "DIRECT STREAM" : "TRANSCODE");
  return out;
}

export const Details = memo(function Details({ state, row, col, decision, image, posterSize }: {
  state: DetailsState;
  row: DetailsRow;
  col: number;
  decision: PlaybackDecision | null;
  image: ImageFn;
  posterSize: PosterSize;
}) {
  const { item } = state;
  const isShow = state.seasons.length > 0;
  const art = image(item.type === "episode" ? item.grandparentArt || item.art : item.art, 1600, 900);
  const actions = detailActions(state);
  const season = state.seasons[state.seasonIndex];
  const overline = item.type === "episode" ? item.grandparentTitle : item.type === "show" ? "Series" : item.type === "movie" ? "Movie" : item.type;
  return (
    <section className={isShow ? "details details-show" : "details"}>
      {art && <img className="details-art" alt="" src={art} decoding="async" onLoad={(event) => { event.currentTarget.style.opacity = "1"; }} />}
      <div className="scrim-left" />
      <div className="scrim-bottom" />
      <div className="details-body">
        <p className="overline">{overline}</p>
        <h1 className="details-title">{item.title}</h1>
        <p className="meta-line">{metaLine(item)}</p>
        <div className="chips">
          {chips(decision).map((chip) => <span key={chip} className="chip">{chip}</span>)}
          {(item.genres ?? []).slice(0, 3).map((genre) => <span key={genre} className="chip chip-quiet">{genre}</span>)}
        </div>
        {item.tagline && !isShow && <p className="tagline">{item.tagline}</p>}
        {item.summary ? <p className="details-summary">{item.summary}</p> : state.loading ? <span className="sk-line" style={{ width: 900, height: 28 }} /> : null}
        <div className="actions">
          {actions.map((action, index) => (
            <span key={action.id} className={`btn${index === 0 ? " btn-primary" : ""}${row === "actions" && index === col ? " is-focused" : ""}`}>
              {index === 0 && <i className="icon-play" />}
              {action.label}
              {action.value && <em className="btn-value">{action.value}</em>}
            </span>
          ))}
        </div>
      </div>
      {isShow && (
        <div className="seasons">
          {state.seasons.map((entry, index) => (
            <span key={entry.ratingKey} className={`tab${index === state.seasonIndex ? " is-current" : ""}${row === "seasons" && index === col ? " is-focused" : ""}`}>{entry.title}</span>
          ))}
        </div>
      )}
      {isShow && (
        <div className="episodes">
          {state.episodes.length ? (
            <Rail
              kind="continue"
              title={`${season?.title ?? "Episodes"} · ${state.episodes.length} episodes`}
              items={state.episodes}
              top={0}
              col={row === "episodes" ? col : 0}
              focused={row === "episodes"}
              inSeason
              posterSize={posterSize}
              image={image}
            />
          ) : <p className="meta-line">Loading episodes…</p>}
        </div>
      )}
    </section>
  );
});
