export interface FocusTarget {
  id: string;
  row: number;
  col: number;
}

export type Direction = "left" | "right" | "up" | "down";

export function moveFocus(items: readonly FocusTarget[], currentId: string, direction: Direction): string {
  const current = items.find((item) => item.id === currentId) ?? items[0];
  if (!current) return currentId;
  const sameRow = items.filter((item) => item.row === current.row);
  if (direction === "left" || direction === "right") {
    const ordered = [...sameRow].sort((a, b) => a.col - b.col);
    const index = ordered.findIndex((item) => item.id === current.id);
    const next = ordered[index + (direction === "right" ? 1 : -1)];
    return next?.id ?? current.id;
  }
  const rowDelta = direction === "down" ? 1 : -1;
  const candidates = items.filter((item) => item.row === current.row + rowDelta);
  if (candidates.length === 0) return current.id;
  candidates.sort((a, b) => Math.abs(a.col - current.col) - Math.abs(b.col - current.col) || a.col - b.col);
  return candidates[0].id;
}

export function visibleWindow<T>(items: readonly T[], focusIndex: number, radius: number): T[] {
  const start = Math.max(0, focusIndex - radius);
  const end = Math.min(items.length, focusIndex + radius + 1);
  return items.slice(start, end);
}
