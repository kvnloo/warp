import type { HomeSnapshot, Hub, MediaItem, Part, PinStart, PlexServer, Stream } from "./types";

export interface PlexHeaders {
  product: string;
  version: string;
  clientId: string;
  platform: string;
  device: string;
  deviceName: string;
}

export function plexHeaders(headers: PlexHeaders, token?: string): Record<string, string> {
  const out: Record<string, string> = {
    Accept: "application/json",
    "X-Plex-Product": headers.product,
    "X-Plex-Version": headers.version,
    "X-Plex-Client-Identifier": headers.clientId,
    "X-Plex-Platform": headers.platform,
    "X-Plex-Device": headers.device,
    "X-Plex-Device-Name": headers.deviceName,
    "X-Plex-Provides": "player,client",
  };
  if (token) out["X-Plex-Token"] = token;
  return out;
}

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === "object" ? value as Record<string, unknown> : {};
}

function num(value: unknown): number | undefined {
  if (typeof value === "number" && Number.isFinite(value)) return value;
  if (typeof value === "string" && value && Number.isFinite(Number(value))) return Number(value);
  return undefined;
}

function str(value: unknown): string {
  return typeof value === "string" ? value : value == null ? "" : String(value);
}

function streamOf(raw: Record<string, unknown>): Stream {
  const type = num(raw.streamType) || 0;
  const streamType: Stream["streamType"] = type === 2 || type === 3 ? type : 1;
  return {
    streamType,
    codec: str(raw.codec),
    bitrate: num(raw.bitrate),
    width: num(raw.width),
    height: num(raw.height),
    displayTitle: str(raw.displayTitle || raw.extendedDisplayTitle),
    channels: num(raw.channels),
    colorTrc: str(raw.colorTrc),
    doviPresent: raw.DOVIPresent === true || raw.doviPresent === true,
    doviProfile: num(raw.doviProfile || asRecord(raw.DOVI).profile),
  };
}

export function parseItem(raw: unknown): MediaItem | null {
  const row = asRecord(raw);
  const ratingKey = str(row.ratingKey);
  const title = str(row.title);
  if (!ratingKey || !title) return null;
  const media = Array.isArray(row.Media) ? row.Media : [];
  const parts: Part[] = [];
  for (const entry of media) {
    const partRows = Array.isArray(asRecord(entry).Part) ? asRecord(entry).Part as unknown[] : [];
    for (const partRaw of partRows) {
      const part = asRecord(partRaw);
      const streams = Array.isArray(part.Stream) ? part.Stream.map((stream) => streamOf(asRecord(stream))) : [];
      parts.push({
        key: str(part.key),
        container: str(part.container),
        duration: num(part.duration),
        streams,
      });
    }
  }
  return {
    ratingKey,
    key: str(row.key) || `/library/metadata/${ratingKey}`,
    type: str(row.type),
    title,
    year: num(row.year),
    summary: str(row.summary),
    thumb: str(row.thumb),
    art: str(row.art),
    duration: num(row.duration),
    viewOffset: num(row.viewOffset),
    grandparentTitle: str(row.grandparentTitle),
    parentTitle: str(row.parentTitle),
    index: num(row.index),
    parentIndex: num(row.parentIndex),
    parts,
  };
}

export function parseHubs(payload: unknown): Hub[] {
  const container = asRecord(asRecord(payload).MediaContainer);
  const hubs = Array.isArray(container.Hub) ? container.Hub : Array.isArray(payload) ? payload : [];
  return hubs.flatMap((hubRaw) => {
    const hub = asRecord(hubRaw);
    const items = (Array.isArray(hub.Metadata) ? hub.Metadata : [])
      .map(parseItem)
      .filter((item): item is MediaItem => item != null);
    const title = str(hub.title);
    if (!title) return [];
    return [{ identifier: str(hub.hubIdentifier || hub.identifier || title), title, items }];
  });
}

export function parsePin(payload: unknown): PinStart | null {
  const row = asRecord(payload);
  const id = num(row.id);
  const code = str(row.code);
  if (!id || !code) return null;
  return { id, code };
}

export function parsePinToken(payload: unknown): string {
  return str(asRecord(payload).authToken);
}

export function parseResources(payload: unknown): PlexServer[] {
  const rows = Array.isArray(payload) ? payload : [];
  return rows.flatMap((raw) => {
    const row = asRecord(raw);
    if (!str(row.provides).split(",").includes("server")) return [];
    const connections = (Array.isArray(row.connections) ? row.connections : []).map((connection) => {
      const item = asRecord(connection);
      return {
        uri: str(item.uri),
        local: item.local === true,
        relay: item.relay === true,
      };
    }).filter((connection) => connection.uri);
    return [{
      name: str(row.name),
      clientIdentifier: str(row.clientIdentifier),
      owned: row.owned === true,
      connections,
    }];
  });
}

export function parseSections(payload: unknown): { movieSectionId?: string; showSectionId?: string } {
  const container = asRecord(asRecord(payload).MediaContainer);
  const rows = Array.isArray(container.Directory) ? container.Directory : [];
  let movieSectionId: string | undefined;
  let showSectionId: string | undefined;
  for (const raw of rows) {
    const row = asRecord(raw);
    const type = str(row.type);
    const key = str(row.key);
    if (type === "movie" && !movieSectionId) movieSectionId = key;
    if (type === "show" && !showSectionId) showSectionId = key;
  }
  return { movieSectionId, showSectionId };
}

export function readHomeCache(raw: string | null, maxAgeMs: number, now: number): HomeSnapshot | null {
  if (!raw) return null;
  try {
    const parsed = JSON.parse(raw) as HomeSnapshot;
    if (!parsed || !Array.isArray(parsed.hubs) || typeof parsed.savedAt !== "number") return null;
    if (now - parsed.savedAt > maxAgeMs) return null;
    return parsed;
  } catch {
    return null;
  }
}

export function writeHomeCache(snapshot: HomeSnapshot): string {
  return JSON.stringify(snapshot);
}

export function thumbUrl(serverUri: string, token: string, path: string | undefined, width: number): string {
  if (!path) return "";
  const base = serverUri.replace(/\/$/, "");
  const url = encodeURIComponent(path);
  return `${base}/photo/:/transcode?width=${width}&height=${Math.round(width * 1.5)}&minSize=1&upscale=0&url=${url}&X-Plex-Token=${encodeURIComponent(token)}`;
}

export class PlexClient {
  constructor(
    private readonly fetchImpl: typeof fetch,
    private readonly headers: PlexHeaders,
  ) {}

  private async json(url: string, token?: string, method = "GET"): Promise<unknown> {
    const response = await this.fetchImpl(url, { method, headers: plexHeaders(this.headers, token) });
    if (!response.ok) throw new Error(`${method} ${url} failed: ${response.status}`);
    return response.json();
  }

  async startPin(): Promise<PinStart> {
    const pin = parsePin(await this.json("https://plex.tv/api/v2/pins?strong=true", undefined, "POST"));
    if (!pin) throw new Error("Plex did not return a PIN");
    return pin;
  }

  async pollPin(id: number): Promise<string> {
    return parsePinToken(await this.json(`https://plex.tv/api/v2/pins/${id}`));
  }

  async resources(token: string): Promise<PlexServer[]> {
    return parseResources(await this.json(`https://plex.tv/api/v2/resources?includeHttps=1&includeRelay=1`, token));
  }

  async hubs(serverUri: string, token: string): Promise<Hub[]> {
    return parseHubs(await this.json(`${serverUri.replace(/\/$/, "")}/hubs`, token));
  }

  async sections(serverUri: string, token: string): Promise<{ movieSectionId?: string; showSectionId?: string }> {
    return parseSections(await this.json(`${serverUri.replace(/\/$/, "")}/library/sections`, token));
  }

  async metadata(serverUri: string, token: string, ratingKey: string): Promise<MediaItem> {
    const payload = await this.json(`${serverUri.replace(/\/$/, "")}/library/metadata/${ratingKey}`, token);
    const container = asRecord(asRecord(payload).MediaContainer);
    const first = Array.isArray(container.Metadata) ? parseItem(container.Metadata[0]) : null;
    if (!first) throw new Error(`metadata ${ratingKey} missing`);
    return first;
  }

  async sectionAll(serverUri: string, token: string, sectionId: string, type: number): Promise<MediaItem[]> {
    const payload = await this.json(
      `${serverUri.replace(/\/$/, "")}/library/sections/${sectionId}/all?type=${type}&includeGuids=0`,
      token,
    );
    const container = asRecord(asRecord(payload).MediaContainer);
    return (Array.isArray(container.Metadata) ? container.Metadata : [])
      .map(parseItem)
      .filter((item): item is MediaItem => item != null);
  }

  async timeline(
    serverUri: string,
    token: string,
    ratingKey: string,
    state: "playing" | "paused" | "stopped",
    timeMs: number,
    durationMs: number,
  ): Promise<void> {
    const base = serverUri.replace(/\/$/, "");
    const query = new URLSearchParams({
      ratingKey,
      key: `/library/metadata/${ratingKey}`,
      state,
      time: String(Math.max(0, Math.round(timeMs))),
      duration: String(Math.max(0, Math.round(durationMs))),
    });
    await this.json(`${base}/:/timeline?${query.toString()}`, token, "POST");
  }
}
