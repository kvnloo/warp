import { describe, expect, it } from "vitest";
import { decidePlayback, playbackUrl } from "./decision";
import type { MediaItem } from "./types";

function item(partial: Partial<MediaItem> & { codec: string; audio?: string; container?: string; hdr?: "hdr10" | "dv5" | "sdr" }): MediaItem {
  return {
    ratingKey: "42",
    key: "/library/metadata/42",
    type: "movie",
    title: "Fixture",
    duration: 6_000_000,
    viewOffset: 12_000,
    parts: [{
      key: "/library/parts/9/file",
      container: partial.container ?? "mkv",
      streams: [
        {
          streamType: 1,
          codec: partial.codec,
          width: 3840,
          height: 2160,
          bitrate: 28_000,
          colorTrc: partial.hdr === "hdr10" ? "smpte2084" : undefined,
          doviPresent: partial.hdr === "dv5",
          doviProfile: partial.hdr === "dv5" ? 5 : undefined,
        },
        { streamType: 2, codec: partial.audio ?? "eac3", bitrate: 768 },
      ],
    }],
    ...partial,
  };
}

describe("QN85B playback decision", () => {
  it("direct-plays 4K HDR10 HEVC MKV instead of transcoding", () => {
    const decision = decidePlayback(item({ codec: "hevc", hdr: "hdr10" }), "avplay");
    expect(decision.mode).toBe("direct-play");
    expect(decision.reason).toContain("profile matches");
    expect(decision.hdr).toBe("hdr10");
    expect(decision.resolution).toBe("3840x2160");
    expect(decision.bitrate).toBe(28_000);
    expect(decision.startMs).toBe(12_000);
  });

  it("remuxes when only the container is outside the HTML5 profile", () => {
    const decision = decidePlayback(item({ codec: "h264", audio: "aac", container: "mkv", hdr: "sdr" }), "html5");
    expect(decision.mode).toBe("direct-stream");
    expect(decision.reason).toContain("remux");
  });

  it("transcodes only when the video codec is outside the TV profile", () => {
    const decision = decidePlayback(item({ codec: "theora" }), "avplay");
    expect(decision.mode).toBe("transcode");
    expect(decision.reason).toContain("theora");
  });

  it("does not transcode a supported MKV just because HTML5 cannot play it on AVPlay", () => {
    const decision = decidePlayback(item({ codec: "hevc", container: "mkv" }), "avplay");
    expect(decision.mode).not.toBe("transcode");
  });

  it("builds a part URL for direct play and a transcode URL otherwise", () => {
    const direct = decidePlayback(item({ codec: "hevc" }), "avplay");
    expect(playbackUrl("http://192.0.2.10:32400", "tok", "cid", direct)).toContain("/library/parts/9/file?");
    const transcode = decidePlayback(item({ codec: "theora" }), "avplay");
    expect(playbackUrl("http://192.0.2.10:32400/", "tok", "cid", transcode)).toContain("directPlay=0");
    expect(playbackUrl("http://192.0.2.10:32400/", "tok", "cid", transcode)).toContain("directStream=0");
  });
});
