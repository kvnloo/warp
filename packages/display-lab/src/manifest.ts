export interface MeasurementManifest {
  patternId: string;
  timestamp: string;
  tv: {
    model: string;
    pictureMode: string;
    brightness?: number;
    contrast?: number;
    color?: number;
    backlight?: number;
    signal: "sdr" | "hdr" | "unknown";
  };
  camera: {
    model: string;
    lens?: string;
    iso?: number;
    exposure?: string;
    aperture?: string;
    focus?: string;
    whiteBalance?: string;
    rawFilename?: string;
  };
  note: string;
}

export function createManifest(input: MeasurementManifest): MeasurementManifest {
  if (!input.patternId) throw new Error("patternId required");
  if (!input.timestamp) throw new Error("timestamp required");
  return {
    ...input,
    note: input.note || "Relative spatial characterization only. Not absolute colorimetry.",
  };
}
