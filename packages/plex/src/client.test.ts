import { describe, expect, it } from "vitest";
import { parseHubs, parsePin, parseResources, readHomeCache, writeHomeCache } from "./client";

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
});
