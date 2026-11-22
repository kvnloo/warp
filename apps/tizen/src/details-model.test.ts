import { describe, expect, it } from "vitest";
import type { MediaItem } from "@warp/plex";
import {
  currentChoice,
  detailActions,
  detailRows,
  initialDetails,
  nextEpisode,
  playable,
  subtitleOptions,
  withStream,
} from "./details-model";

const movie: MediaItem = {
  ratingKey: "1", key: "/m/1", type: "movie", title: "Arrival", viewOffset: 60_000, duration: 6_000_000,
  parts: [
    { id: "p1", label: "4K HEVC", key: "/p1", container: "mkv", streams: [
      { id: "a1", streamType: 2, codec: "truehd", displayTitle: "English TrueHD", selected: true },
      { id: "a2", streamType: 2, codec: "aac", displayTitle: "Spanish AAC" },
      { id: "t1", streamType: 3, codec: "srt", displayTitle: "English" },
    ] },
    { id: "p2", label: "1080p H264", key: "/p2", container: "mp4", streams: [] },
  ],
};

describe("details model", () => {
  it("offers resume, restart, version, audio and subtitle buttons", () => {
    const state = { ...initialDetails(movie), loading: false };
    expect(detailActions(state).map((action) => action.id)).toEqual(["play", "restart", "version", "audio", "subs"]);
    expect(detailActions(state)[0].label).toBe("Resume");
    expect(detailActions(state).find((action) => action.id === "subs")?.value).toBe("Off");
    expect(detailRows(state)).toEqual(["actions"]);
  });

  it("switches audio and subtitles in the chosen part only", () => {
    let state = { ...initialDetails(movie), loading: false };
    state = withStream(state, "audio", "a2");
    state = withStream(state, "subs", "t1");
    expect(currentChoice(state, "audio")).toBe("a2");
    expect(currentChoice(state, "subs")).toBe("t1");
    expect(withStream(state, "subs", "0").item.parts[0].streams[2].selected).toBe(false);
    expect(movie.parts[0].streams[1].selected).toBeUndefined();
    expect(subtitleOptions(movie.parts[0])[0]).toEqual({ id: "0", label: "Off" });
  });

  it("plays the chosen version first", () => {
    const state = { ...initialDetails(movie), partIndex: 1 };
    expect(playable(state).parts[0].id).toBe("p2");
    expect(detailActions(state).map((action) => action.id)).not.toContain("audio");
  });

  it("plays the next unwatched episode of a show", () => {
    const ep = (index: number, extra: Partial<MediaItem> = {}): MediaItem => ({ ratingKey: `e${index}`, key: "", type: "episode", title: `E${index}`, index, parentIndex: 1, parts: [], ...extra });
    const episodes = [ep(1, { viewCount: 1 }), ep(2), ep(3)];
    expect(nextEpisode(episodes)?.ratingKey).toBe("e2");
    expect(nextEpisode([ep(1, { viewCount: 1 }), ep(2, { viewCount: 1, viewOffset: 5 })])?.ratingKey).toBe("e2");
    const show = { ...initialDetails({ ratingKey: "s", key: "", type: "show", title: "S", parts: [] }), seasons: [movie], episodes };
    expect(detailActions(show)[0].label).toBe("Play S1 E2");
    expect(detailRows(show)).toEqual(["actions", "seasons", "episodes"]);
  });
});
