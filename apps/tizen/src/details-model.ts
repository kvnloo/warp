/** Details screen as data: which buttons exist, which streams are chosen, what Play does. */
import type { MediaItem, Part, Stream } from "@warp/plex";

export interface DetailsState {
  item: MediaItem;
  /** True while full metadata is loading; the rail item is shown meanwhile. */
  loading: boolean;
  partIndex: number;
  seasons: MediaItem[];
  seasonIndex: number;
  episodes: MediaItem[];
}

export type DetailsRow = "actions" | "seasons" | "episodes";
export type ActionId = "play" | "restart" | "version" | "audio" | "subs";
export type PickerKind = "version" | "audio" | "subs";

export interface DetailAction {
  id: ActionId;
  label: string;
  value?: string;
}

export interface PickOption {
  id: string;
  label: string;
}

export function initialDetails(item: MediaItem): DetailsState {
  return { item, loading: true, partIndex: 0, seasons: [], seasonIndex: 0, episodes: [] };
}

export function currentPart(state: DetailsState): Part | undefined {
  return state.item.parts[state.partIndex] ?? state.item.parts[0];
}

const byType = (part: Part | undefined, type: 2 | 3): Stream[] => (part?.streams ?? []).filter((stream) => stream.streamType === type);

function streamLabel(stream: Stream): string {
  return stream.displayTitle || [stream.language, stream.codec.toUpperCase()].filter(Boolean).join(" ") || "Unknown";
}

export function audioOptions(part: Part | undefined): PickOption[] {
  return byType(part, 2).map((stream, index) => ({ id: stream.id || String(index), label: streamLabel(stream) }));
}

export function subtitleOptions(part: Part | undefined): PickOption[] {
  return [{ id: "0", label: "Off" }, ...byType(part, 3).map((stream, index) => ({ id: stream.id || `s${index}`, label: streamLabel(stream) }))];
}

export function versionOptions(item: MediaItem): PickOption[] {
  return item.parts.map((part, index) => ({ id: String(index), label: part.label || `Version ${index + 1}` }));
}

export function selectedId(part: Part | undefined, kind: "audio" | "subs"): string {
  const options = kind === "audio" ? audioOptions(part) : subtitleOptions(part);
  const streams = byType(part, kind === "audio" ? 2 : 3);
  const index = streams.findIndex((stream) => stream.selected);
  if (index < 0) return kind === "audio" ? options[0]?.id ?? "" : "0";
  return kind === "audio" ? options[index].id : options[index + 1].id;
}

export function optionsFor(state: DetailsState, kind: PickerKind): PickOption[] {
  if (kind === "version") return versionOptions(state.item);
  return kind === "audio" ? audioOptions(currentPart(state)) : subtitleOptions(currentPart(state));
}

export function currentChoice(state: DetailsState, kind: PickerKind): string {
  return kind === "version" ? String(state.partIndex) : selectedId(currentPart(state), kind);
}

/** Mark one audio or subtitle stream selected (subtitle "0" clears all) in the current part only. */
export function withStream(state: DetailsState, kind: "audio" | "subs", id: string): DetailsState {
  const type = kind === "audio" ? 2 : 3;
  const options = kind === "audio" ? audioOptions(currentPart(state)) : subtitleOptions(currentPart(state)).slice(1);
  const parts = state.item.parts.map((part, partIndex) => {
    if (partIndex !== state.partIndex) return part;
    let n = -1;
    return {
      ...part,
      streams: part.streams.map((stream) => {
        if (stream.streamType !== type) return stream;
        n += 1;
        return { ...stream, selected: options[n]?.id === id };
      }),
    };
  });
  return { ...state, item: { ...state.item, parts } };
}

export function nextEpisode(episodes: readonly MediaItem[]): MediaItem | undefined {
  return episodes.find((episode) => episode.viewOffset) ?? episodes.find((episode) => !episode.viewCount) ?? episodes[0];
}

/** "English (TrueHD 7.1)" -> "English" for the compact button; the picker shows the full label. */
export function shortStream(label: string | undefined): string | undefined {
  return label?.replace(/\s*\(.*\)\s*$/, "") || label;
}

export function detailActions(state: DetailsState): DetailAction[] {
  const { item } = state;
  if (item.type === "show" || item.type === "season") {
    const next = nextEpisode(state.episodes);
    return [{ id: "play", label: next ? `Play S${next.parentIndex ?? state.seasonIndex + 1} E${next.index ?? 1}` : "Play" }];
  }
  const actions: DetailAction[] = [{ id: "play", label: item.viewOffset ? "Resume" : "Play" }];
  if (item.viewOffset) actions.push({ id: "restart", label: "From start" });
  const part = currentPart(state);
  if (item.parts.length > 1) actions.push({ id: "version", label: "Version", value: (part?.label || "").split(" · ")[0] });
  const audio = audioOptions(part);
  if (audio.length) actions.push({ id: "audio", label: "Audio", value: shortStream(audio.find((option) => option.id === selectedId(part, "audio"))?.label) });
  if (byType(part, 3).length) actions.push({ id: "subs", label: "Subtitles", value: shortStream(subtitleOptions(part).find((option) => option.id === selectedId(part, "subs"))?.label) });
  return actions;
}

export function detailRows(state: DetailsState): DetailsRow[] {
  return state.seasons.length ? ["actions", "seasons", "episodes"] : ["actions"];
}

/** The item Play starts, with the chosen version first so the playback decision uses it. */
export function playable(state: DetailsState): MediaItem {
  const part = currentPart(state);
  if (!part) return state.item;
  return { ...state.item, parts: [part, ...state.item.parts.filter((other) => other !== part)] };
}
