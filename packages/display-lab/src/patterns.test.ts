import { describe, expect, it } from "vitest";
import { createManifest } from "./manifest";
import { PATTERNS, patternById } from "./patterns";

describe("display lab", () => {
  it("keeps stable pattern ids for grayscale, near-black, and windows", () => {
    expect(patternById("field-10")?.title).toContain("10%");
    expect(patternById("near-black-1")?.background).toBe("#010101");
    expect(patternById("windows-10")?.kind).toBe("windows");
    expect(new Set(PATTERNS.map((pattern) => pattern.id)).size).toBe(PATTERNS.length);
  });

  it("refuses a manifest without a pattern id and does not claim colorimetry", () => {
    expect(() => createManifest({
      patternId: "",
      timestamp: "2026-10-05T00:00:00Z",
      tv: { model: "QN85B", pictureMode: "Movie", signal: "sdr" },
      camera: { model: "Galaxy S25 Ultra" },
      note: "",
    })).toThrow(/patternId/);
    const manifest = createManifest({
      patternId: "field-50",
      timestamp: "2026-10-05T00:00:00Z",
      tv: { model: "QN85QN85BDFXZA", pictureMode: "Filmmaker", signal: "hdr" },
      camera: { model: "Galaxy S25 Ultra", rawFilename: "capture.dng" },
      note: "",
    });
    expect(manifest.note).toContain("Not absolute colorimetry");
    expect(manifest.camera.rawFilename).toBe("capture.dng");
  });
});
