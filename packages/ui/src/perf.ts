export interface PerfDeltas {
  coldLaunchToFirstPaint?: number;
  launchToHome?: number;
  keyToFocus?: number;
  focusToImage?: number;
  detailNav?: number;
  playToFirstFrame?: number;
}

export interface PerfReceipt {
  device: string;
  chromiumTarget: number;
  marks: Record<string, number>;
  deltas: PerfDeltas;
  longTasks: number[];
  note: string;
}

export function delta(marks: Record<string, number>, from: string, to: string): number | undefined {
  const start = marks[from];
  const end = marks[to];
  if (start == null || end == null) return undefined;
  return Math.round((end - start) * 10) / 10;
}

export function buildReceipt(input: {
  device: string;
  chromiumTarget: number;
  marks: Record<string, number>;
  longTasks: number[];
  note: string;
}): PerfReceipt {
  return {
    device: input.device,
    chromiumTarget: input.chromiumTarget,
    marks: input.marks,
    longTasks: input.longTasks,
    note: input.note,
    deltas: {
      coldLaunchToFirstPaint: delta(input.marks, "launch", "first-paint"),
      launchToHome: delta(input.marks, "launch", "home-usable"),
      keyToFocus: delta(input.marks, "keydown", "focus-moved"),
      focusToImage: delta(input.marks, "focus-moved", "image-visible"),
      detailNav: delta(input.marks, "detail-open", "detail-ready"),
      playToFirstFrame: delta(input.marks, "play", "first-frame"),
    },
  };
}
