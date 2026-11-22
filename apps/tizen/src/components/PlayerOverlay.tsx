import type { MediaItem, PlaybackDecision } from "@warp/plex";

function clock(ms: number): string {
  const total = Math.max(0, Math.round(ms / 1000));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  return `${h ? `${h}:` : ""}${h ? String(m).padStart(2, "0") : m}:${String(s).padStart(2, "0")}`;
}

export function PlayerOverlay({ item, decision, timeMs, paused }: {
  item: MediaItem | null;
  decision: PlaybackDecision | null;
  timeMs: number;
  paused: boolean;
}) {
  const duration = decision?.durationMs || item?.duration || 0;
  const progress = duration ? Math.min(1, timeMs / duration) : 0;
  return (
    <section className={paused ? "player-ui is-paused" : "player-ui"}>
      <div className="player-bar">
        <p className="overline">{paused ? "Paused" : "Playing"} · {decision?.mode ?? ""} · {decision?.videoCodec ?? ""} · {decision?.hdr ?? ""}</p>
        <h2 className="player-title">{item?.type === "episode" ? `${item.grandparentTitle} · ${item.title}` : item?.title}</h2>
        <div className="scrub"><i style={{ transform: `scaleX(${progress.toFixed(4)})` }} /></div>
        <div className="player-times"><span>{clock(timeMs)}</span><span>{decision?.reason ?? ""}</span><span>-{clock(duration - timeMs)}</span></div>
      </div>
    </section>
  );
}
