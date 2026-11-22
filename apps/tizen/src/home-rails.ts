import type { Hub, MediaItem } from "@warp/plex";
import type { RailId, RailPref } from "./prefs";

export interface HomeRail {
  kind: RailId;
  hub: Hub;
}

/** Map a Plex hub to one of the rail kinds the user can show, hide and reorder. */
export function railKind(hub: Hub): RailId {
  const id = `${hub.identifier} ${hub.title}`.toLowerCase();
  if (/continue|ondeck|on deck/.test(id)) return "continue";
  if (/recent/.test(id) && /movie/.test(id)) return "recent-movies";
  if (/recent/.test(id) && /(tv|show|episode)/.test(id)) return "recent-shows";
  if (/recent/.test(id)) return "recent";
  return "more";
}

/** Hubs grouped by kind, ordered by the user's rail order, hidden kinds and empty hubs removed. */
export function orderRails(hubs: readonly Hub[], rails: readonly RailPref[]): HomeRail[] {
  const out: HomeRail[] = [];
  for (const rail of rails) {
    if (!rail.visible) continue;
    for (const hub of hubs) {
      if (hub.items.length && railKind(hub) === rail.id) out.push({ kind: rail.id, hub });
    }
  }
  return out;
}

/** The featured title: first in-progress item, else the newest item on Home. */
export function heroItem(hubs: readonly Hub[]): MediaItem | undefined {
  for (const hub of hubs) {
    if (railKind(hub) === "continue" && hub.items[0]) return hub.items[0];
  }
  return hubs.find((hub) => hub.items.length)?.items[0];
}
