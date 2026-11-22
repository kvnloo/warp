import { describe, expect, it } from "vitest";
import { imageUrl, parseHubs, parseItem, parsePin, parseResources, readHomeCache, versionLabel, writeHomeCache } from "./client";

describe("plex parsers", () => {
  it("reads a PIN and owned local server resources", () => {
    expect(parsePin({ id: 7, code: "ABCD" })).toEqual({ id: 7, code: "ABCD" });
    const servers = parseResources([
      {
        name: "Library",
        provides: "server",
        owned: true,
        clientIdentifier: "srv",
        connections: [{ uri: "http://192.0.2.8:32400", local: true, relay: false }],
      },
      { name: "Player", provides: "player", connections: [] },
    ]);
    expect(servers).toHaveLength(1);
    expect(servers[0].connections[0].local).toBe(true);
  });

  it("parses home hubs without dropping continue-watching items", () => {
    const hubs = parseHubs({
      MediaContainer: {
        Hub: [{
          hubIdentifier: "home.continue",
          title: "Continue Watching",
          Metadata: [{ ratingKey: "1", title: "Arrival", type: "movie", viewOffset: 1000 }],
        }],
      },
    });
    expect(hubs[0].items[0].title).toBe("Arrival");
    expect(hubs[0].items[0].viewOffset).toBe(1000);
  });

  it("returns cached home data without treating expired JSON as fresh", () => {
    const raw = writeHomeCache({ savedAt: 1_000, serverName: "Library", hubs: [] });
    expect(readHomeCache(raw, 500, 1_200)?.serverName).toBe("Library");
    expect(readHomeCache(raw, 500, 2_000)).toBeNull();
    expect(readHomeCache("{", 500, 1_200)).toBeNull();
  });

  it("keeps every Media version with a label and selectable stream ids", () => {
    const item = parseItem({
      ratingKey: "5", title: "Dune", type: "movie", contentRating: "PG-13", audienceRating: 8.3,
      Genre: [{ tag: "Sci-Fi" }],
      Media: [
        { videoResolution: "4k", videoCodec: "hevc", videoDynamicRange: "HDR10", audioChannels: 8,
          Part: [{ id: 11, key: "/p/11", container: "mkv", Stream: [{ id: 1, streamType: 2, codec: "truehd", selected: true, languageTag: "en" }] }] },
        { videoResolution: "1080", videoCodec: "h264", audioChannels: 6, Part: [{ id: 12, key: "/p/12", container: "mp4", Stream: [] }] },
      ],
    });
    expect(item?.parts.map((part) => part.label)).toEqual(["4K HEVC HDR10 · 7.1", "1080p H264 · 5.1"]);
    expect(item?.parts[0].id).toBe("11");
    expect(item?.parts[0].streams[0]).toMatchObject({ id: "1", selected: true, language: "en" });
    expect(item?.genres).toEqual(["Sci-Fi"]);
    expect(item?.audienceRating).toBe(8.3);
    expect(versionLabel({})).toBe("Original");
  });

  it("builds sized image URLs and passes data URIs through", () => {
    expect(imageUrl("http://s:32400/", "t", "/art/1", 1920, 1080)).toContain("width=1920&height=1080");
    expect(imageUrl("http://s", "t", "data:image/svg+xml,x", 1, 1)).toBe("data:image/svg+xml,x");
    expect(imageUrl("http://s", "t", undefined, 1, 1)).toBe("");
  });
});
