import { useEffect, useMemo, useRef, useState } from "react";
import {
  PlexClient,
  decidePlayback,
  playbackUrl,
  thumbUrl,
  type HomeSnapshot,
  type Hub,
  type MediaItem,
  type PlaybackDecision,
  type PlexServer,
} from "@warp/plex";
import { PATTERNS, createManifest } from "@warp/display-lab";
import { moveFocus, type FocusTarget } from "@warp/ui";
import { buildReceipt } from "@warp/ui";
import { isBack, isEnter, isPlayPause, keyDirection, registerRemoteKeys } from "./keys";
import { createPlayer, playerKind, type PlayerHandle } from "./player";
import { cachedHome, clearCache, loadSession, saveSession, storeHome, type Session } from "./session";

type Screen = "login" | "home" | "library" | "details" | "player" | "settings" | "patterns";

const marks: Record<string, number> = { launch: performance.now() };
const longTasks: number[] = [];

function stamp(name: string): void {
  marks[name] = performance.now();
}

function homeTargets(hubs: Hub[]): FocusTarget[] {
  const targets: FocusTarget[] = [
    { id: "hero", row: 0, col: 0 },
    { id: "movies", row: 0, col: 1 },
    { id: "shows", row: 0, col: 2 },
    { id: "settings", row: 0, col: 3 },
  ];
  hubs.forEach((hub, row) => {
    hub.items.forEach((item, col) => {
      targets.push({ id: `hub:${row}:${item.ratingKey}`, row: row + 1, col });
    });
  });
  return targets;
}

function findHubItem(hubs: Hub[], id: string): MediaItem | undefined {
  const [, row, ratingKey] = id.split(":");
  return hubs[Number(row)]?.items.find((item) => item.ratingKey === ratingKey);
}

async function firstReachable(
  servers: PlexServer[],
  token: string,
): Promise<{ name: string; uri: string } | null> {
  for (const server of servers) {
    const connections = [...server.connections].sort((left, right) => Number(right.local) - Number(left.local));
    for (const connection of connections) {
      if (connection.relay) continue;
      try {
        const response = await fetch(`${connection.uri.replace(/\/$/, "")}/identity`, {
          headers: { Accept: "application/json", "X-Plex-Token": token },
        });
        if (response.ok) return { name: server.name, uri: connection.uri };
      } catch {
        /* try the next connection */
      }
    }
  }
  return null;
}

export function App() {
  const [session, setSession] = useState<Session>(() => loadSession());
  const [screen, setScreen] = useState<Screen>(session.token && session.serverUri ? "home" : "login");
  const [pin, setPin] = useState("");
  const [status, setStatus] = useState("Connect a Plex account");
  const [home, setHome] = useState<HomeSnapshot | null>(() => cachedHome(Date.now()));
  const [library, setLibrary] = useState<MediaItem[]>([]);
  const [libraryTitle, setLibraryTitle] = useState("");
  const [selected, setSelected] = useState<MediaItem | null>(null);
  const [decision, setDecision] = useState<PlaybackDecision | null>(null);
  const [focus, setFocus] = useState("hero");
  const [paused, setPaused] = useState(false);
  const [timeMs, setTimeMs] = useState(0);
  const [debug, setDebug] = useState(false);
  const [patternIndex, setPatternIndex] = useState(0);
  const playerRef = useRef<PlayerHandle | null>(null);
  const videoRef = useRef<HTMLVideoElement>(null);
  const retried = useRef(false);
  const screenRef = useRef(screen);
  screenRef.current = screen;
  const headers = useMemo(() => ({
    product: "Warp",
    version: "0.1.0",
    clientId: session.clientId,
    platform: "Tizen",
    device: "QN85B",
    deviceName: "Warp",
  }), [session.clientId]);
  const client = useMemo(() => new PlexClient(fetch, headers), [headers]);

  useEffect(() => {
    registerRemoteKeys();
    stamp("first-paint");
    if (typeof PerformanceObserver !== "function") return undefined;
    const observer = new PerformanceObserver((list) => {
      for (const entry of list.getEntries()) longTasks.push(Math.round(entry.duration));
    });
    try { observer.observe({ entryTypes: ["longtask"] }); } catch { /* Chromium 85 may reject the type */ }
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    if (screen !== "login" || session.token) return undefined;
    let stopped = false;
    let pinId = 0;
    const tick = async () => {
      try {
        if (!pinId) {
          const started = await client.startPin();
          if (stopped) return;
          pinId = started.id;
          setPin(started.code);
          setStatus("Open https://plex.tv/link and enter this code");
        }
        const token = await client.pollPin(pinId);
        if (!token || stopped) return;
        const servers = await client.resources(token);
        const owned = servers.filter((server) => server.owned);
        const chosen = await firstReachable(owned.length ? owned : servers, token);
        if (!chosen) {
          setStatus("Signed in, but no reachable Plex server");
          return;
        }
        const next = { ...session, token, serverUri: chosen.uri, serverName: chosen.name };
        saveSession(next);
        setSession(next);
        setScreen("home");
      } catch (error) {
        if (!stopped) setStatus(error instanceof Error ? error.message : "Plex login failed");
      }
    };
    void tick();
    const timer = window.setInterval(() => void tick(), 2000);
    return () => {
      stopped = true;
      window.clearInterval(timer);
    };
  }, [client, screen, session]);

  useEffect(() => {
    if (screen !== "home" || !session.token || !session.serverUri) return undefined;
    let stopped = false;
    void (async () => {
      try {
        const [hubs, sections] = await Promise.all([
          client.hubs(session.serverUri, session.token),
          client.sections(session.serverUri, session.token),
        ]);
        if (stopped) return;
        const snapshot: HomeSnapshot = { savedAt: Date.now(), serverName: session.serverName, hubs, ...sections };
        storeHome(snapshot);
        setHome(snapshot);
        stamp("home-usable");
        setStatus(session.serverName);
      } catch (error) {
        if (!stopped) setStatus(error instanceof Error ? error.message : "Home refresh failed");
      }
    })();
    return () => { stopped = true; };
  }, [client, screen, session]);

  useEffect(() => {
    if (screen !== "player" || !selected || !decision) return undefined;
    const timer = window.setInterval(() => {
      const handle = playerRef.current;
      if (!handle || paused) return;
      void client.timeline(session.serverUri, session.token, selected.ratingKey, "playing", handle.timeMs(), decision.durationMs);
    }, 10_000);
    return () => window.clearInterval(timer);
  }, [client, decision, paused, screen, selected, session]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      stamp("keydown");
      if (event.keyCode === 403 || event.key === "d") {
        setDebug((value) => !value);
        return;
      }
      const current = screenRef.current;
      if (current === "player") {
        onPlayerKey(event.keyCode);
        return;
      }
      if (current === "patterns") {
        if (isBack(event.keyCode)) setScreen("home");
        if (event.keyCode === 39) setPatternIndex((index) => (index + 1) % PATTERNS.length);
        if (event.keyCode === 37) setPatternIndex((index) => (index + PATTERNS.length - 1) % PATTERNS.length);
        return;
      }
      const direction = keyDirection(event.keyCode);
      if (direction && current !== "login") {
        const targets = current === "home"
          ? homeTargets(home?.hubs ?? [])
          : current === "library"
            ? library.map((item, index) => ({ id: `lib:${item.ratingKey}`, row: Math.floor(index / 6), col: index % 6 }))
            : current === "details"
              ? [{ id: "play", row: 0, col: 0 }, { id: "back", row: 0, col: 1 }]
              : [{ id: "back", row: 0, col: 0 }];
        setFocus((value) => moveFocus(targets, value, direction));
        stamp("focus-moved");
        return;
      }
      if (isEnter(event.keyCode)) onActivate();
      if (isBack(event.keyCode)) onBack();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  });

  function onBack(): void {
    if (screen === "player") {
      void stopPlayback();
      setScreen("details");
      return;
    }
    if (screen !== "login" && screen !== "home") {
      setScreen("home");
      setFocus("hero");
    }
  }

  function onActivate(): void {
    if (screen === "home") {
      if (focus === "settings") return setScreen("settings");
      if (focus === "patterns") return setScreen("patterns");
      if (focus === "movies" && home?.movieSectionId) return void openLibrary(home.movieSectionId, 1, "Movies");
      if (focus === "shows" && home?.showSectionId) return void openLibrary(home.showSectionId, 2, "Shows");
      const item = focus === "hero"
        ? home?.hubs.flatMap((hub) => hub.items)[0]
        : findHubItem(home?.hubs ?? [], focus);
      if (item) void openDetails(item.ratingKey);
      return;
    }
    if (screen === "library") {
      const ratingKey = focus.startsWith("lib:") ? focus.slice(4) : library[0]?.ratingKey;
      if (ratingKey) void openDetails(ratingKey);
      return;
    }
    if (screen === "details" && selected && focus !== "back") void startPlayback(selected);
    if (focus === "back") onBack();
  }

  async function openLibrary(sectionId: string, type: number, title: string): Promise<void> {
    const items = await client.sectionAll(session.serverUri, session.token, sectionId, type);
    setLibraryTitle(title);
    setLibrary(items);
    setFocus(items[0] ? `lib:${items[0].ratingKey}` : "hero");
    setScreen("library");
  }

  async function openDetails(ratingKey: string): Promise<void> {
    stamp("detail-open");
    const item = await client.metadata(session.serverUri, session.token, ratingKey);
    setSelected(item);
    setDecision(decidePlayback(item, playerKind()));
    setFocus("play");
    setScreen("details");
    stamp("detail-ready");
  }

  async function startPlayback(item: MediaItem, forced?: PlaybackDecision): Promise<void> {
    const video = videoRef.current;
    if (!video) return;
    const next = forced ?? decidePlayback(item, playerKind());
    setDecision(next);
    setScreen("player");
    setPaused(false);
    stamp("play");
    const handle = createPlayer(video, setTimeMs, () => stamp("first-frame"), (message) => {
      if (!retried.current && next.mode !== "transcode") {
        retried.current = true;
        void startPlayback(item, { ...next, mode: "transcode", reason: `player rejected ${next.mode}: ${message}` });
        return;
      }
      setStatus(message);
    });
    playerRef.current = handle;
    await handle.play(playbackUrl(session.serverUri, session.token, session.clientId, next), next.startMs);
    setTimeMs(next.startMs);
    void client.timeline(session.serverUri, session.token, item.ratingKey, "playing", next.startMs, next.durationMs);
  }

  function onPlayerKey(code: number): void {
    const handle = playerRef.current;
    if (!handle || !selected || !decision) return;
    if (isBack(code)) {
      void stopPlayback();
      setScreen("details");
    }
    if (isPlayPause(code) || isEnter(code)) {
      const state = paused ? "playing" : "paused";
      if (paused) handle.resume();
      else handle.pause();
      setPaused((value) => !value);
      void client.timeline(session.serverUri, session.token, selected.ratingKey, state, handle.timeMs(), decision.durationMs);
      return;
    }
    if (code === 37 || code === 412) seek(-10_000);
    if (code === 39 || code === 417) seek(10_000);
  }

  function seek(deltaMs: number): void {
    const handle = playerRef.current;
    if (!handle || !selected || !decision) return;
    handle.seekBy(deltaMs);
    const next = Math.max(0, handle.timeMs() + deltaMs);
    setTimeMs(next);
    void client.timeline(session.serverUri, session.token, selected.ratingKey, paused ? "paused" : "playing", next, decision.durationMs);
  }

  async function stopPlayback(): Promise<void> {
    const handle = playerRef.current;
    if (handle && selected && decision) {
      await client.timeline(session.serverUri, session.token, selected.ratingKey, "stopped", handle.timeMs(), decision.durationMs);
      handle.stop();
    }
    playerRef.current = null;
    retried.current = false;
  }
  const pattern = PATTERNS[patternIndex] ?? PATTERNS[0];
  const manifest = createManifest({
    patternId: pattern.id,
    timestamp: "1970-01-01T00:00:00.000Z",
    tv: { model: "QN85QN85BDFXZA", pictureMode: "unset", signal: "unknown" },
    camera: { model: "Galaxy S25 Ultra" },
    note: "",
  });
  const receipt = buildReceipt({
    device: typeof window === "undefined" ? "node" : playerKind(),
    chromiumTarget: 85,
    marks,
    longTasks,
    note: "Not a QN85B measurement unless this bundle is running on the TV.",
  });

  return (
    <main className="screen">
      <video ref={videoRef} className="player-video" />
      {screen === "login" && (
        <section className="hero">
          <h1>Warp</h1>
          <p className="pin">{pin || "----"}</p>
          <p className="meta">{status}</p>
        </section>
      )}
      {screen === "home" && (
        <section>
          <div className={focus === "hero" ? "hero focused" : "hero"}>
            <p className="meta">{status || home?.serverName || "Home"}</p>
            <h1>{home?.hubs.flatMap((hub) => hub.items)[0]?.title || "Warp"}</h1>
            <p className="summary">{home?.hubs.flatMap((hub) => hub.items)[0]?.summary || "Cached home appears before artwork finishes."}</p>
          </div>
          <div className="row" style={{ padding: "0 72px 12px" }}>
            <button className={focus === "movies" ? "focused" : ""} type="button">Movies</button>
            <button className={focus === "shows" ? "focused" : ""} type="button">Shows</button>
            <button className={focus === "settings" ? "focused" : ""} type="button">Settings</button>
          </div>
          {(home?.hubs ?? []).map((hub, row) => (
            <div className="rail" key={hub.identifier}>
              <h2>{hub.title}</h2>
              <div className="row">
                {hub.items.slice(0, 8).map((item) => (
                  <Poster key={item.ratingKey} item={item} focused={focus === `hub:${row}:${item.ratingKey}`} serverUri={session.serverUri} token={session.token} />
                ))}
              </div>
            </div>
          ))}
        </section>
      )}
      {screen === "library" && (
        <section>
          <h1 className="hero">{libraryTitle}</h1>
          <div className="grid">
            {library.slice(0, 48).map((item) => (
              <Poster key={item.ratingKey} item={item} focused={focus === `lib:${item.ratingKey}`} serverUri={session.serverUri} token={session.token} />
            ))}
          </div>
        </section>
      )}
      {screen === "details" && selected && (
        <section className="hero">
          <p className="meta">{selected.grandparentTitle || selected.type}</p>
          <h1>{selected.title}</h1>
          <p className="summary">{selected.summary}</p>
          <p className="meta">{decision ? `${decision.mode} · ${decision.videoCodec} · ${decision.audioCodec} · ${decision.hdr} · ${decision.resolution} · ${decision.bitrate}` : ""}</p>
          <div className="row">
            <button className={focus === "play" ? "focused" : ""} type="button">{selected.viewOffset ? "Resume" : "Play"}</button>
            <button className={focus === "back" ? "focused" : ""} type="button">Back</button>
          </div>
        </section>
      )}
      {screen === "player" && (
        <section>
          <div className="overlay">
            <strong>{selected?.title}</strong>
            <p>{paused ? "Paused" : "Playing"} · {Math.round(timeMs / 1000)}s</p>
            <p>{decision?.mode} · {decision?.videoCodec} · {decision?.hdr} · {decision?.reason}</p>
          </div>
        </section>
      )}
      {screen === "settings" && (
        <section className="hero">
          <h1>Settings</h1>
          <p className="meta">{session.serverName || "No server"} · {session.serverUri}</p>
          <div className="row">
            <button className="focused" type="button" onClick={() => { clearCache(); setHome(null); setStatus("Cache cleared"); }}>Clear cache</button>
            <button type="button" onClick={() => {
              const next = { ...session, token: "", serverUri: "", serverName: "" };
              saveSession(next);
              setSession(next);
              setScreen("login");
            }}>Sign out</button>
            <button type="button" onClick={() => setScreen("patterns")}>Display patterns</button>
          </div>
        </section>
      )}
      {screen === "patterns" && (
        <section className={pattern.kind === "checker" ? "checker pattern" : "pattern"} style={pattern.kind === "checker" ? undefined : { background: pattern.background }}>
          {pattern.patches && (
            <div className="patches">
              {pattern.patches.map((patch) => <div key={patch.label} className="patch" style={{ background: patch.color }} />)}
            </div>
          )}
          {pattern.kind === "windows" && <div className="window-box" style={{ width: pattern.id.endsWith("25") ? "25%" : "10%", height: pattern.id.endsWith("25") ? "25%" : "10%" }} />}
          <p className="label">{pattern.id} · {pattern.title}</p>
          <pre className="debug">{JSON.stringify(manifest)}</pre>
        </section>
      )}
      {debug && <pre className="debug">{JSON.stringify(receipt, null, 2)}</pre>}
    </main>
  );
}

function Poster({ item, focused, serverUri, token }: {
  item: MediaItem;
  focused: boolean;
  serverUri: string;
  token: string;
}) {
  const src = thumbUrl(serverUri, token, item.thumb, 180);
  return (
    <div className={focused ? "card focused" : "card"}>
      {src ? <img alt="" src={src} onLoad={() => { marks["image-visible"] = performance.now(); }} /> : <div className="poster-fallback" />}
      <span>{item.title}</span>
    </div>
  );
}
