import { describe, expect, it } from "vitest";
import type { Hub, MediaItem } from "@warp/plex";
import { defaultPrefs, toggleRail, moveRail } from "./prefs";
import { heroItem, orderRails, railKind } from "./home-rails";

const item = (ratingKey: string): MediaItem => ({ ratingKey, key: `/m/${ratingKey}`, type: "movie", title: ratingKey, parts: [] });
const hub = (identifier: string, title: string, keys: string[]): Hub => ({ identifier, title, items: keys.map(item) });

const hubs = [
  hub("movie.recentlyadded", "Recently Added Movies", ["m1"]),
  hub("home.continue", "Continue Watching", ["c1", "c2"]),
  hub("tv.recentlyadded", "Recently Added TV", ["s1"]),
  hub("home.playlists", "Playlists", ["p1"]),
  hub("home.empty", "Recently Added Music", []),
];

describe("home rails", () => {
  it("classifies Plex hubs", () => {
    expect(hubs.map(railKind)).toEqual(["recent-movies", "continue", "recent-shows", "more", "recent"]);
    expect(railKind(hub("home.ondeck", "On Deck", []))).toBe("continue");
  });

  it("orders by user preference and drops hidden or empty rails", () => {
    const prefs = defaultPrefs();
    expect(orderRails(hubs, prefs.rails).map((rail) => rail.hub.title)).toEqual([
      "Continue Watching", "Recently Added Movies", "Recently Added TV", "Playlists",
    ]);
    const rails = toggleRail(moveRail(prefs.rails, "recent-shows", -1), "continue");
    expect(orderRails(hubs, rails).map((rail) => rail.kind)).toEqual(["recent-shows", "recent-movies", "more"]);
  });

  it("features the first in-progress title", () => {
    expect(heroItem(hubs)?.ratingKey).toBe("c1");
    expect(heroItem([hubs[0]])?.ratingKey).toBe("m1");
    expect(heroItem([])).toBeUndefined();
  });
});
