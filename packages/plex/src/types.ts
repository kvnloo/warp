export type PlaybackMode = "direct-play" | "direct-stream" | "transcode";

export type Engine = "avplay" | "html5";

export interface Stream {
  streamType: 1 | 2 | 3;
  codec: string;
  bitrate?: number;
  width?: number;
  height?: number;
  displayTitle?: string;
  channels?: number;
  colorTrc?: string;
  doviPresent?: boolean;
  doviProfile?: number;
}

export interface Part {
  key: string;
  container: string;
  duration?: number;
  streams: Stream[];
}

export interface MediaItem {
  ratingKey: string;
  key: string;
  type: string;
  title: string;
  year?: number;
  summary?: string;
  thumb?: string;
  art?: string;
  duration?: number;
  viewOffset?: number;
  grandparentTitle?: string;
  parentTitle?: string;
  index?: number;
  parentIndex?: number;
  parts: Part[];
}

export interface Hub {
  identifier: string;
  title: string;
  items: MediaItem[];
}

export interface HomeSnapshot {
  savedAt: number;
  serverName: string;
  hubs: Hub[];
  movieSectionId?: string;
  showSectionId?: string;
}

export interface PlaybackDecision {
  mode: PlaybackMode;
  reason: string;
  videoCodec: string;
  audioCodec: string;
  hdr: string;
  resolution: string;
  bitrate: number;
  container: string;
  partKey: string;
  ratingKey: string;
  startMs: number;
  durationMs: number;
}

export interface ServerConnection {
  uri: string;
  local: boolean;
  relay: boolean;
}

export interface PlexServer {
  name: string;
  clientIdentifier: string;
  owned: boolean;
  connections: ServerConnection[];
}

export interface PinStart {
  id: number;
  code: string;
}

export const QN85B_PROFILE = {
  id: "samsung-qn85b-tizen-6.5",
  chromium: 85,
  video: ["h264", "hevc", "h265", "vp9", "av1", "mpeg4", "mpeg2video", "wmv3", "vc1"],
  audio: ["aac", "mp3", "ac3", "eac3", "mp2", "pcm", "flac", "vorbis", "opus", "dts", "dca", "truehd"],
  containers: ["mp4", "m4v", "mkv", "mov", "ts", "m2ts", "avi", "wmv", "asf"],
  hdr: ["sdr", "hdr10", "hdr10+", "hlg"],
} as const;
