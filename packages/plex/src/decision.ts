import type { Engine, MediaItem, Part, PlaybackDecision, Stream } from "./types";
import { QN85B_PROFILE } from "./types";

const HTML5_VIDEO: Record<string, true> = { h264: true };
const HTML5_AUDIO: Record<string, true> = { aac: true, mp3: true };
const HTML5_CONTAINER: Record<string, true> = { mp4: true, m4v: true, mov: true };

function norm(value: string | undefined): string {
  return (value || "").trim().toLowerCase();
}

function videoStream(part: Part): Stream | undefined {
  return part.streams.find((stream) => stream.streamType === 1);
}

function audioStream(part: Part): Stream | undefined {
  return part.streams.find((stream) => stream.streamType === 2);
}

export function hdrOf(stream: Stream | undefined): string {
  if (!stream) return "unknown";
  if (stream.doviPresent && stream.doviProfile === 5) return "dolby-vision-profile5";
  if (stream.doviPresent) return "dolby-vision-with-base";
  const trc = norm(stream.colorTrc);
  if (trc === "smpte2084" || trc === "pq") return "hdr10";
  if (trc === "arib-std-b67") return "hlg";
  const title = norm(stream.displayTitle);
  if (title.includes("hdr10+")) return "hdr10+";
  if (title.includes("hdr")) return "hdr10";
  if (title.includes("hlg")) return "hlg";
  return "sdr";
}

function resolution(stream: Stream | undefined): string {
  if (!stream?.width || !stream.height) return "unknown";
  return `${stream.width}x${stream.height}`;
}

function bitrate(part: Part, video: Stream | undefined): number {
  return video?.bitrate || part.streams.reduce((sum, stream) => sum + (stream.bitrate || 0), 0);
}

export function decidePlayback(item: MediaItem, engine: Engine): PlaybackDecision {
  const part = item.parts[0];
  const video = part ? videoStream(part) : undefined;
  const audio = part ? audioStream(part) : undefined;
  const container = norm(part?.container);
  const videoCodec = norm(video?.codec);
  const audioCodec = norm(audio?.codec);
  const hdr = hdrOf(video);
  const base = {
    videoCodec: videoCodec || "unknown",
    audioCodec: audioCodec || "none",
    hdr,
    resolution: resolution(video),
    bitrate: part ? bitrate(part, video) : 0,
    container: container || "unknown",
    partKey: part?.key || "",
    ratingKey: item.ratingKey,
    startMs: item.viewOffset || 0,
    durationMs: item.duration || part?.duration || 0,
  };


  const videoOk = engine === "avplay"
    ? (QN85B_PROFILE.video as readonly string[]).includes(videoCodec)
    : HTML5_VIDEO[videoCodec] === true;
  const audioOk = !audioCodec || (engine === "avplay"
    ? (QN85B_PROFILE.audio as readonly string[]).includes(audioCodec)
    : HTML5_AUDIO[audioCodec] === true);
  const containerOk = engine === "avplay"
    ? (QN85B_PROFILE.containers as readonly string[]).includes(container)
    : HTML5_CONTAINER[container] === true;
  const hdrOk = engine === "html5" || hdr !== "dolby-vision-profile5";

  if (!videoOk || !hdrOk) {
    const why = !videoOk
      ? `${videoCodec} is outside the ${engine} profile`
      : "Dolby Vision profile 5 has no HDR10 base on this panel";
    return { ...base, mode: "transcode", reason: why };
  }
  if (!containerOk || !audioOk) {
    const why = !containerOk
      ? `${container || "unknown"} container needs remux`
      : `${audioCodec} audio needs a compatible stream`;
    return { ...base, mode: "direct-stream", reason: why };
  }
  return {
    ...base,
    mode: "direct-play",
    reason: `${engine} profile matches container, video, audio, and HDR`,
  };
}

export function playbackUrl(
  serverUri: string,
  token: string,
  clientId: string,
  decision: PlaybackDecision,
): string {
  const base = serverUri.replace(/\/$/, "");
  const common = `X-Plex-Token=${encodeURIComponent(token)}&X-Plex-Client-Identifier=${encodeURIComponent(clientId)}&X-Plex-Platform=Tizen&X-Plex-Product=Warp`;
  if (decision.mode === "direct-play") {
    return `${base}${decision.partKey}?${common}`;
  }
  const directStream = decision.mode === "direct-stream" ? "1" : "0";
  const path = `/library/metadata/${decision.ratingKey}`;
  return `${base}/video/:/transcode/universal/start.mkv?hasMDE=1&path=${encodeURIComponent(path)}&mediaIndex=0&partIndex=0&protocol=http&directPlay=0&directStream=${directStream}&copyts=1&subtitleSize=100&audioBoost=100&session=${encodeURIComponent(clientId)}&${common}`;
}
