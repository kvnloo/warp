import { describe, expect, it } from "vitest";
import { gridMove, moveFocus, visibleWindow } from "./focus";
import { buildReceipt } from "./perf";

const items = [
  { id: "a", row: 0, col: 0 },
  { id: "b", row: 0, col: 1 },
  { id: "c", row: 1, col: 0 },
  { id: "d", row: 1, col: 2 },
];

describe("focus", () => {
  it("moves horizontally and picks the nearest item on the next row", () => {
    expect(moveFocus(items, "a", "right")).toBe("b");
    expect(moveFocus(items, "b", "down")).toBe("c");
    expect(moveFocus(items, "a", "left")).toBe("a");
  });

  it("windows a large library around the focused index", () => {
    const library = Array.from({ length: 100 }, (_, index) => index);
    expect(visibleWindow(library, 40, 2)).toEqual([38, 39, 40, 41, 42]);
  });
});

describe("gridMove", () => {
  it("moves in a row-major grid and clamps at edges", () => {
    expect(gridMove(0, 20, 6, "right")).toBe(1);
    expect(gridMove(5, 20, 6, "right")).toBe(5);
    expect(gridMove(6, 20, 6, "left")).toBe(6);
    expect(gridMove(7, 20, 6, "up")).toBe(1);
    expect(gridMove(1, 20, 6, "up")).toBe(1);
    expect(gridMove(1, 20, 6, "down")).toBe(7);
  });

  it("drops into a short last row instead of getting stuck", () => {
    expect(gridMove(17, 20, 6, "down")).toBe(19);
    expect(gridMove(19, 20, 6, "down")).toBe(19);
    expect(gridMove(0, 0, 6, "down")).toBe(0);
  });
});

describe("receipt", () => {
  it("computes key-to-focus and play-to-frame deltas", () => {
    const receipt = buildReceipt({
      device: "node",
      chromiumTarget: 85,
      marks: { launch: 0, "first-paint": 18, keydown: 100, "focus-moved": 112, play: 400, "first-frame": 910 },
      longTasks: [],
      note: "synthetic",
    });
    expect(receipt.deltas.coldLaunchToFirstPaint).toBe(18);
    expect(receipt.deltas.keyToFocus).toBe(12);
    expect(receipt.deltas.playToFirstFrame).toBe(510);
  });
});
