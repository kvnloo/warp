import { readHomeCache, writeHomeCache, type HomeSnapshot } from "@warp/plex";

const SESSION_KEY = "warp.session.v1";
const HOME_KEY = "warp.home.v1";
const HOME_MAX_AGE_MS = 1000 * 60 * 60 * 12;

export interface Session {
  clientId: string;
  token: string;
  serverUri: string;
  serverName: string;
}

function newClientId(): string {
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0")).join("");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

export function loadSession(): Session {
  const raw = localStorage.getItem(SESSION_KEY);
  if (raw) {
    try {
      const parsed = JSON.parse(raw) as Session;
      if (parsed.clientId) return parsed;
    } catch {
      /* replace a corrupt session */
    }
  }
  const created: Session = { clientId: newClientId(), token: "", serverUri: "", serverName: "" };
  localStorage.setItem(SESSION_KEY, JSON.stringify(created));
  return created;
}

export function saveSession(session: Session): void {
  localStorage.setItem(SESSION_KEY, JSON.stringify(session));
}

export function cachedHome(now: number): HomeSnapshot | null {
  return readHomeCache(localStorage.getItem(HOME_KEY), HOME_MAX_AGE_MS, now);
}

export function storeHome(snapshot: HomeSnapshot): void {
  localStorage.setItem(HOME_KEY, writeHomeCache(snapshot));
}

export function clearCache(): void {
  localStorage.removeItem(HOME_KEY);
}
