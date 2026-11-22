import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import {
  PlexClient,
  decidePlayback,
  playbackUrl,
  type HomeSnapshot,
  type MediaItem,
  type PlaybackDecision,
  type PlexServer,
} from "@warp/plex";
import { PATTERNS, createManifest } from "@warp/display-lab";
import { buildReceipt, gridMove } from "@warp/ui";
import { Backdrop } from "./components/Backdrop";
import { Details } from "./components/Details";
import { Hero } from "./components/Hero";
import { LibraryGrid } from "./components/LibraryGrid";
import { Login } from "./components/Login";
import { NavBar, TABS } from "./components/NavBar";
import { Picker } from "./components/Picker";
import { PlayerOverlay } from "./components/PlayerOverlay";
import { Rail, SkeletonRail } from "./components/Rail";
import { Settings } from "./components/Settings";
import {
  currentChoice,
  currentPart,
  detailActions,
  detailRows,
  initialDetails,
  nextEpisode,
  optionsFor,
  playable,
  withStream,
  type DetailsRow,
  type DetailsState,
  type PickerKind,
} from "./details-model";
import { heroItem, orderRails } from "./home-rails";
import { isBack, isEnter, isPlayPause, keyDirection, registerRemoteKeys } from "./keys";
import { HERO_H, NAV_H, cardMetrics, homeScrollY, railTops, visibleCards } from "./layout";
import { mockSource } from "./mock";
import { loadPrefs, resetPrefs, savePrefs, type Prefs } from "./prefs";
import { createPlayer, playerKind, type PlayerHandle } from "./player";
import { cachedHome, clearCache, loadSession, saveSession, storeHome, type Session } from "./session";
import { buildSettingsRows, settingsKey } from "./settings-model";
import { plexSource } from "./source";
import { applyTheme } from "./theme";

type Screen = "login" | "home" | "library" | "details" | "player" | "settings" | "patterns";

/** Fixture data for screenshots and UI work. Vite replaces this at build time; production is always false. */
const MOCK = import.meta.env.VITE_WARP_MOCK === "1";
const APP_VERSION = "0.2.0";
const MOCK_SESSION: Session = { clientId: "warp-mock", token: "mock", serverUri: "mock://", serverName: "Demo Library" };

const marks: Record<string, number> = { launch: performance.now() };
const longTasks: number[] = [];

function stamp(name: string): void {
  marks[name] = performance.now();
}

async function firstReachable(servers: PlexServer[], token: string): Promise<{ name: string; uri: string } | null> {
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

interface LibraryState {
  tab: number;
  title: string;
  items: MediaItem[];
  index: number;
  loading: boolean;
}

interface PickerState {
  kind: PickerKind;
  index: number;
}

const PICKER_TITLES: Record<PickerKind, string> = { version: "Version", audio: "Audio", subs: "Subtitles" };

export function App() {
  const [session, setSession] = useState<Session>(() => (MOCK ? MOCK_SESSION : loadSession()));
  const [prefs, setPrefsState] = useState<Prefs>(() => loadPrefs(localStorage));
  const [screen, setScreen] = useState<Screen>(session.token && session.serverUri ? "home" : "login");
  const [pin, setPin] = useState("");
  const [status, setStatus] = useState("Waiting for a code from plex.tv");
  const [home, setHome] = useState<HomeSnapshot | null>(() => (MOCK ? null : cachedHome(Date.now())));
  const [navFocus, setNavFocus] = useState(-1);
  const [homeZone, setHomeZone] = useState<"hero" | "rail">("hero");
  const [heroCol, setHeroCol] = useState(0);
  const [railRow, setRailRow] = useState(0);
  const [railCols, setRailCols] = useState<Record<string, number>>({});
  const [library, setLibrary] = useState<LibraryState>({ tab: 1, title: "", items: [], index: 0, loading: false });
  const [details, setDetails] = useState<DetailsState | null>(null);
  const [detailsRow, setDetailsRow] = useState<DetailsRow>("actions");
  const [detailsCol, setDetailsCol] = useState(0);
  const [picker, setPicker] = useState<PickerState | null>(null);
  const [settingsPos, setSettingsPos] = useState({ row: 0, col: 0 });
  const [decisionOverride, setDecisionOverride] = useState<PlaybackDecision | null>(null);
  const [paused, setPaused] = useState(false);
  const [timeMs, setTimeMs] = useState(0);
  const [debug, setDebug] = useState(false);
  const [patternIndex, setPatternIndex] = useState(0);
  const playerRef = useRef<PlayerHandle | null>(null);
  const videoRef = useRef<HTMLVideoElement>(null);
  const retried = useRef(false);
  const detailsStack = useRef<Array<{ state: DetailsState; row: DetailsRow; col: number }>>([]);
  const returnTo = useRef<Screen>("home");
  const headers = useMemo(() => ({
    product: "Warp",
    version: APP_VERSION,
    clientId: session.clientId,
    platform: "Tizen",
    device: "QN85B",
    deviceName: "Warp",
  }), [session.clientId]);
  const client = useMemo(() => new PlexClient(fetch, headers), [headers]);
  const source = useMemo(() => (MOCK ? mockSource() : plexSource(client, session)), [client, session]);
  const image = source.image;

  useLayoutEffect(() => applyTheme(prefs), [prefs]);
  const setPrefs = useCallback((next: Prefs) => {
    setPrefsState(next);
    savePrefs(localStorage, next);
  }, []);

  useEffect(() => {
    // AVPlay draws under the web layer, so the page must be transparent while playing.
    document.documentElement.style.background = screen === "player" ? "transparent" : "";
    document.documentElement.setAttribute("data-screen", screen);
  }, [screen]);

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
    if (MOCK || screen !== "login" || session.token) return undefined;
    let stopped = false;
    let pinId = 0;
    const tick = async () => {
      try {
        if (!pinId) {
          const started = await client.startPin();
          if (stopped) return;
          pinId = started.id;
          setPin(started.code);
          setStatus("Waiting for you to enter the code…");
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
        const [hubs, sections] = await Promise.all([source.hubs(), source.sections()]);
        if (stopped) return;
        const snapshot: HomeSnapshot = { savedAt: Date.now(), serverName: session.serverName, hubs, ...sections };
        if (!MOCK) storeHome(snapshot);
        setHome(snapshot);
        stamp("home-usable");
      } catch (error) {
        if (!stopped) setStatus(error instanceof Error ? error.message : "Home refresh failed");
      }
    })();
    return () => { stopped = true; };
  }, [source, screen, session]);

  const decision = useMemo(
    () => decisionOverride ?? (details && details.item.parts.length ? decidePlayback(playable(details), playerKind()) : null),
    [details, decisionOverride],
  );
  const selected = details?.item ?? null;

  useEffect(() => {
    if (screen !== "player" || !selected || !decision) return undefined;
    const timer = window.setInterval(() => {
      const handle = playerRef.current;
      if (!handle || paused) return;
      void client.timeline(session.serverUri, session.token, selected.ratingKey, "playing", handle.timeMs(), decision.durationMs);
    }, 10_000);
    return () => window.clearInterval(timer);
  }, [client, decision, paused, screen, selected, session]);

  // ---- derived Home layout -------------------------------------------------
  const rails = useMemo(() => orderRails(home?.hubs ?? [], prefs.rails), [home, prefs.rails]);
  const featured = useMemo(() => (prefs.hero ? heroItem(home?.hubs ?? []) : undefined), [home, prefs.hero]);
  const tops = useMemo(() => railTops(rails.map((rail) => rail.kind), prefs), [rails, prefs]);
  const zone = prefs.hero ? homeZone : "rail";
  const row = Math.min(railRow, Math.max(0, rails.length - 1));
  const focusedRail = rails[row];
  const focusedItem = focusedRail?.hub.items[railCols[focusedRail.hub.identifier] ?? 0];
  const settingsRows = useMemo(() => buildSettingsRows(prefs, { debug }), [prefs, debug]);
  const activeTab = screen === "home" ? 0 : screen === "library" ? library.tab : screen === "settings" ? 3 : -1;

  // ---- navigation helpers --------------------------------------------------
  async function openLibrary(tab: number): Promise<void> {
    const sectionId = tab === 1 ? home?.movieSectionId : home?.showSectionId;
    const title = tab === 1 ? "Movies" : "Shows";
    setLibrary({ tab, title, items: [], index: 0, loading: true });
    setScreen("library");
    setNavFocus(-1);
    if (!sectionId) {
      setLibrary({ tab, title, items: [], index: 0, loading: false });
      return;
    }
    try {
      const items = await source.sectionAll(sectionId, tab === 1 ? 1 : 2);
      setLibrary((current) => (current.tab === tab ? { ...current, items, loading: false } : current));
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Library failed to load");
      setLibrary((current) => ({ ...current, loading: false }));
    }
  }

  function goTab(tab: number): void {
    setNavFocus(-1);
    if (tab === 0) {
      setScreen("home");
      setHomeZone("hero");
      setRailRow(0);
    } else if (tab === 3) {
      setScreen("settings");
      setSettingsPos({ row: 0, col: 0 });
    } else {
      void openLibrary(tab);
    }
  }

  async function loadEpisodes(seasonKey: string): Promise<void> {
    const episodes = await source.children(seasonKey);
    setDetails((current) => (current && current.seasons.some((s, i) => i === current.seasonIndex && s.ratingKey === seasonKey)
      ? { ...current, episodes }
      : current));
  }

  async function openDetails(item: MediaItem): Promise<void> {
    stamp("detail-open");
    if (screen === "details" && details) detailsStack.current.push({ state: details, row: detailsRow, col: detailsCol });
    else {
      detailsStack.current = [];
      returnTo.current = screen === "player" ? "home" : screen;
    }
    setDecisionOverride(null);
    setDetails(initialDetails(item));
    setDetailsRow("actions");
    setDetailsCol(0);
    setScreen("details");
    stamp("detail-ready");
    try {
      const full = await source.metadata(item.ratingKey);
      let seasons: MediaItem[] = [];
      if (full.type === "show") seasons = await source.children(full.ratingKey);
      if (full.type === "season") seasons = [full];
      setDetails((current) => (current && current.item.ratingKey === item.ratingKey
        ? { ...current, item: full, loading: false, seasons, seasonIndex: 0 }
        : current));
      if (seasons[0]) void loadEpisodes(seasons[0].ratingKey);
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Details failed to load");
      setDetails((current) => (current ? { ...current, loading: false } : current));
    }
  }

  function closeDetails(): void {
    const previous = detailsStack.current.pop();
    setDecisionOverride(null);
    if (previous) {
      setDetails(previous.state);
      setDetailsRow(previous.row);
      setDetailsCol(previous.col);
      return;
    }
    setScreen(returnTo.current);
  }

  async function playByKey(item: MediaItem, fromStart = false): Promise<void> {
    const full = item.parts.length ? item : await source.metadata(item.ratingKey);
    if (!details || details.item.ratingKey !== full.ratingKey) {
      if (screen === "details" && details) detailsStack.current.push({ state: details, row: detailsRow, col: detailsCol });
      else returnTo.current = screen;
      setDetails({ ...initialDetails(full), loading: false });
    }
    void startPlayback(fromStart ? { ...full, viewOffset: 0 } : full);
  }

  async function startPlayback(item: MediaItem, forced?: PlaybackDecision): Promise<void> {
    const video = videoRef.current;
    if (!video) return;
    const next = forced ?? decidePlayback(item, playerKind());
    setDecisionOverride(next);
    setScreen("player");
    setPaused(false);
    stamp("play");
    if (MOCK) return;
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

  async function stopPlayback(): Promise<void> {
    const handle = playerRef.current;
    if (handle && selected && decision) {
      await client.timeline(session.serverUri, session.token, selected.ratingKey, "stopped", handle.timeMs(), decision.durationMs);
      handle.stop();
    }
    playerRef.current = null;
    retried.current = false;
    setDecisionOverride(null);
  }

  function seek(deltaMs: number): void {
    const handle = playerRef.current;
    if (!handle || !selected || !decision) return;
    handle.seekBy(deltaMs);
    const next = Math.max(0, handle.timeMs() + deltaMs);
    setTimeMs(next);
    void client.timeline(session.serverUri, session.token, selected.ratingKey, paused ? "paused" : "playing", next, decision.durationMs);
  }

  function onPlayerKey(code: number): void {
    if (isBack(code)) {
      void stopPlayback();
      setScreen("details");
      return;
    }
    const handle = playerRef.current;
    if (!handle || !selected || !decision) return;
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

  function settingsAction(action: string): void {
    if (action === "reset") {
      setPrefsState(resetPrefs(localStorage));
      setSettingsPos({ row: 0, col: 0 });
    } else if (action === "sign-out") {
      if (MOCK) return;
      const next = { ...session, token: "", serverUri: "", serverName: "" };
      saveSession(next);
      setSession(next);
      setScreen("login");
    } else if (action === "debug") {
      setDebug((value) => !value);
    } else if (action === "patterns") {
      setScreen("patterns");
    } else if (action === "clear-cache") {
      clearCache();
      setHome(null);
      setStatus("Cache cleared");
    }
  }

  // ---- keys ----------------------------------------------------------------
  function onHomeKey(direction: ReturnType<typeof keyDirection>, enter: boolean, back: boolean): void {
    if (zone === "hero") {
      if (direction === "left" || direction === "right") setHeroCol((col) => (direction === "right" ? Math.min(1, col + 1) : Math.max(0, col - 1)));
      else if (direction === "up") setNavFocus(0);
      else if (direction === "down" && rails.length) { setHomeZone("rail"); setRailRow(0); }
      else if (enter && featured) void (heroCol === 0 ? playByKey(featured) : openDetails(featured));
      else if (back) setNavFocus(0);
      return;
    }
    if (!focusedRail) {
      if (direction === "up" || back) setNavFocus(0);
      return;
    }
    const id = focusedRail.hub.identifier;
    const col = railCols[id] ?? 0;
    if (direction === "left" || direction === "right") {
      const next = Math.min(focusedRail.hub.items.length - 1, Math.max(0, col + (direction === "right" ? 1 : -1)));
      if (next !== col) setRailCols((cols) => ({ ...cols, [id]: next }));
    } else if (direction === "up") {
      if (row > 0) setRailRow(row - 1);
      else if (prefs.hero && featured) setHomeZone("hero");
      else setNavFocus(0);
    } else if (direction === "down") {
      setRailRow(Math.min(rails.length - 1, row + 1));
    } else if (enter && focusedItem) {
      void openDetails(focusedItem);
    } else if (back) {
      if (row > 0) setRailRow(0);
      else if (prefs.hero && featured) setHomeZone("hero");
      else setNavFocus(0);
    }
  }

  function onLibraryKey(direction: ReturnType<typeof keyDirection>, enter: boolean, back: boolean): void {
    const cols = visibleCards(cardMetrics("recent", prefs));
    if (direction === "up" && library.index < cols) { setNavFocus(library.tab); return; }
    if (direction) {
      setLibrary((current) => ({ ...current, index: gridMove(current.index, current.items.length, cols, direction) }));
      return;
    }
    if (enter && library.items[library.index]) void openDetails(library.items[library.index]);
    if (back) goTab(0);
  }

  function onSettingsKey(direction: ReturnType<typeof keyDirection>, enter: boolean, back: boolean): void {
    if (back) { goTab(0); return; }
    if (direction === "up" && settingsPos.row === 0) { setNavFocus(3); return; }
    const key = direction ?? (enter ? "enter" : null);
    if (!key) return;
    const result = settingsKey(settingsRows, prefs, settingsPos, key);
    setSettingsPos(result.pos);
    if (result.prefs !== prefs) setPrefs(result.prefs);
    if (result.action) settingsAction(result.action);
  }

  function onPickerKey(direction: ReturnType<typeof keyDirection>, enter: boolean, back: boolean): void {
    if (!picker || !details) return;
    const options = optionsFor(details, picker.kind);
    if (back) { setPicker(null); return; }
    if (direction === "up" || direction === "down") {
      setPicker({ ...picker, index: Math.min(options.length - 1, Math.max(0, picker.index + (direction === "down" ? 1 : -1))) });
      return;
    }
    if (!enter) return;
    const choice = options[picker.index];
    setPicker(null);
    if (!choice) return;
    if (picker.kind === "version") {
      setDetails({ ...details, partIndex: Number(choice.id) });
      return;
    }
    const next = withStream(details, picker.kind, choice.id);
    setDetails(next);
    const partId = currentPart(details)?.id;
    if (partId) {
      const body = picker.kind === "audio" ? { audioStreamID: choice.id } : { subtitleStreamID: choice.id };
      source.selectStreams(partId, body).catch((error: unknown) => setStatus(error instanceof Error ? error.message : "Stream choice not saved"));
    }
  }

  function onDetailsKey(direction: ReturnType<typeof keyDirection>, enter: boolean, back: boolean): void {
    if (!details) return;
    if (back) { closeDetails(); return; }
    const rowsList = detailRows(details);
    const rowIndex = Math.max(0, rowsList.indexOf(detailsRow));
    const actions = detailActions(details);
    const width = detailsRow === "actions" ? actions.length : detailsRow === "seasons" ? details.seasons.length : details.episodes.length;
    if (direction === "left" || direction === "right") {
      const next = Math.min(Math.max(0, width - 1), Math.max(0, detailsCol + (direction === "right" ? 1 : -1)));
      setDetailsCol(next);
      if (detailsRow === "seasons" && next !== details.seasonIndex && details.seasons[next]) {
        setDetails({ ...details, seasonIndex: next, episodes: [] });
        const key = details.seasons[next].ratingKey;
        window.setTimeout(() => void loadEpisodes(key), 0);
      }
      return;
    }
    if (direction === "up" || direction === "down") {
      const target = rowsList[rowIndex + (direction === "down" ? 1 : -1)];
      if (!target) return;
      setDetailsRow(target);
      setDetailsCol(target === "seasons" ? details.seasonIndex : 0);
      return;
    }
    if (!enter) return;
    if (detailsRow === "episodes") {
      const episode = details.episodes[detailsCol];
      if (episode) void openDetails(episode);
      return;
    }
    if (detailsRow !== "actions") return;
    const action = actions[detailsCol];
    if (!action) return;
    if (action.id === "play") {
      if (details.item.type === "show" || details.item.type === "season") {
        const episode = nextEpisode(details.episodes);
        if (episode) void playByKey(episode);
      } else if (details.item.parts.length) {
        void startPlayback(playable(details));
      }
    } else if (action.id === "restart") {
      void startPlayback({ ...playable(details), viewOffset: 0 });
    } else {
      const kind = action.id as PickerKind;
      const options = optionsFor(details, kind);
      setPicker({ kind, index: Math.max(0, options.findIndex((option) => option.id === currentChoice(details, kind))) });
    }
  }

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      stamp("keydown");
      const code = event.keyCode;
      if (code === 403 || (event.key === "d" && !MOCK)) {
        setDebug((value) => !value);
        return;
      }
      const direction = keyDirection(code);
      const enter = isEnter(code);
      const back = isBack(code);
      if (direction || enter || back) event.preventDefault();
      if (screen === "player") { onPlayerKey(code); return; }
      if (screen === "patterns") {
        if (back) setScreen("settings");
        if (code === 39) setPatternIndex((index) => (index + 1) % PATTERNS.length);
        if (code === 37) setPatternIndex((index) => (index + PATTERNS.length - 1) % PATTERNS.length);
        return;
      }
      if (screen === "login") return;
      if (picker) { onPickerKey(direction, enter, back); return; }
      if (navFocus >= 0 && (screen === "home" || screen === "library" || screen === "settings")) {
        if (direction === "left") setNavFocus(Math.max(0, navFocus - 1));
        else if (direction === "right") setNavFocus(Math.min(TABS.length - 1, navFocus + 1));
        else if (direction === "down") setNavFocus(-1);
        else if (enter) goTab(navFocus);
        else if (back && screen !== "home") goTab(0);
        if (direction) stamp("focus-moved");
        return;
      }
      if (screen === "home") onHomeKey(direction, enter, back);
      else if (screen === "library") onLibraryKey(direction, enter, back);
      else if (screen === "settings") onSettingsKey(direction, enter, back);
      else if (screen === "details") onDetailsKey(direction, enter, back);
      if (direction) stamp("focus-moved");
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  });

  // ---- render --------------------------------------------------------------
  const homeY = navFocus >= 0 || zone === "hero" ? 0 : homeScrollY(row, tops, prefs.hero);
  const backdropSrc = prefs.background !== "backdrop" ? ""
    : screen === "home" && zone === "rail" && navFocus < 0 && focusedItem ? image(focusedItem.art || focusedItem.grandparentArt, 1280, 720)
      : screen === "library" && library.items[library.index] ? image(library.items[library.index].art, 1280, 720)
        : "";
  const heroLabel = featured?.viewOffset ? "Continue Watching" : "Recently Added";
  const showNav = screen === "home" || screen === "library" || screen === "settings";

  return (
    <main className="app">
      <video ref={videoRef} className="player-video" />
      {prefs.background === "backdrop" && (screen === "home" || screen === "library") && <Backdrop src={backdropSrc} />}
      {screen === "login" && <Login pin={pin} status={status} />}
      {screen === "home" && (
        <div className="home" style={{ transform: `translate3d(0,${-homeY}px,0)` }}>
          {prefs.hero && <Hero item={featured} label={heroLabel} focusCol={zone === "hero" && navFocus < 0 ? heroCol : -1} image={image} />}
          <div className="rails" style={{ top: prefs.hero ? HERO_H : NAV_H + 8 }}>
            {home
              ? rails.map((rail, index) => (
                <Rail
                  key={rail.hub.identifier}
                  kind={rail.kind}
                  title={rail.hub.title}
                  items={rail.hub.items}
                  top={tops[index]}
                  col={railCols[rail.hub.identifier] ?? 0}
                  focused={zone === "rail" && navFocus < 0 && index === row}
                  posterSize={prefs.posterSize}
                  image={image}
                />
              ))
              : [0, 1].map((index) => <SkeletonRail key={index} top={index * 520} landscape={index === 0} posterSize={prefs.posterSize} />)}
            {home && !rails.length && <p className="empty">Nothing on Home yet. Turn rows back on in Settings, or add media to your server.</p>}
          </div>
        </div>
      )}
      {screen === "library" && (
        <LibraryGrid
          title={library.title}
          items={library.items}
          index={library.index}
          active={navFocus < 0}
          loading={library.loading}
          posterSize={prefs.posterSize}
          textSize={prefs.textSize}
          image={image}
        />
      )}
      {screen === "details" && details && (
        <Details state={details} row={detailsRow} col={detailsCol} decision={decision} image={image} posterSize={prefs.posterSize === "l" ? "m" : prefs.posterSize} />
      )}
      {screen === "settings" && (
        <Settings
          rows={settingsRows}
          pos={settingsPos}
          prefs={prefs}
          active={navFocus < 0}
          server={session.serverName}
          preview={(home?.hubs ?? []).flatMap((hub) => hub.items).filter((item) => item.type !== "episode").slice(0, 2)}
          image={image}
        />
      )}
      {screen === "player" && <PlayerOverlay item={selected} decision={decision} timeMs={timeMs} paused={paused} />}
      {screen === "patterns" && <Patterns index={patternIndex} />}
      {showNav && <NavBar active={activeTab} focusIndex={navFocus} clock={prefs.clock} server={session.serverName} />}
      {picker && details && (
        <Picker title={PICKER_TITLES[picker.kind]} options={optionsFor(details, picker.kind)} index={picker.index} current={currentChoice(details, picker.kind)} />
      )}
      {debug && <DebugReceipt />}
    </main>
  );
}

function DebugReceipt() {
  const receipt = buildReceipt({
    device: typeof window === "undefined" ? "node" : playerKind(),
    chromiumTarget: 85,
    marks,
    longTasks,
    note: "Not a QN85B measurement unless this bundle is running on the TV.",
  });
  return <pre className="debug">{JSON.stringify(receipt, null, 2)}</pre>;
}

function Patterns({ index }: { index: number }) {
  const pattern = PATTERNS[index] ?? PATTERNS[0];
  const manifest = createManifest({
    patternId: pattern.id,
    timestamp: "1970-01-01T00:00:00.000Z",
    tv: { model: "QN85QN85BDFXZA", pictureMode: "unset", signal: "unknown" },
    camera: { model: "Galaxy S25 Ultra" },
    note: "",
  });
  return (
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
  );
}
