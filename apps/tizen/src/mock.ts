/**
 * warp-mock fixture: a fake library with generated SVG artwork so screens can be
 * screenshotted without a Plex server. Only bundled when VITE_WARP_MOCK=1.
 */
import type { Hub, MediaItem, Part, Stream } from "@warp/plex";
import type { LibrarySource } from "./source";

const MOVIES = [
  "Northern Lights", "The Quiet Harbor", "Glass Meridian", "Paper Satellites", "Ember & Ash", "The Long Field",
  "Midnight Cartography", "Saltwater Hymn", "A Room of Echoes", "Tidal", "The Ninth Lantern", "Copperline",
  "Hollow Sun", "The Lighthouse Keeper", "Static Bloom", "Far From Kestrel", "Orchard", "Cold Open",
  "Wildflower Road", "The Archivist", "Blue Hour", "Ironwood", "Second Light", "Parallax",
];
const SHOWS = ["Low Orbit", "Saltwater", "The Understudy", "Night Shift", "Fathom", "Westbound", "Kingfisher", "Signal & Noise", "Harbor Lights", "The Cartographers", "Driftwood", "Polar"];
const GENRES = ["Drama", "Sci-Fi", "Thriller", "Adventure", "Mystery", "Comedy", "Romance"];
const RATINGS = ["PG", "PG-13", "R", "TV-MA", "TV-14"];

function hue(seed: number): number {
  return (seed * 47) % 360;
}

function svg(body: string, width: number, height: number): string {
  return `data:image/svg+xml,${encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}">${body}</svg>`)}`;
}

function escape(text: string): string {
  return text.replace(/&/g, "&amp;").replace(/</g, "&lt;");
}

function poster(title: string, seed: number): string {
  const h = hue(seed);
  const words = title.toUpperCase().split(" ");
  const lines = words.reduce<string[]>((out, word) => {
    const last = out[out.length - 1];
    if (last && (last + " " + word).length <= 10) out[out.length - 1] = `${last} ${word}`;
    else out.push(word);
    return out;
  }, []);
  const text = lines.map((line, i) => `<text x="30" y="${410 - (lines.length - 1 - i) * 46}" font-family="Georgia,serif" font-size="40" font-weight="700" fill="#fff" letter-spacing="2"${line.length > 9 ? ' textLength="240" lengthAdjust="spacingAndGlyphs"' : ""}>${escape(line)}</text>`).join("");
  return svg(
    `<defs><linearGradient id="g" x1="0" y1="0" x2="0.4" y2="1"><stop offset="0" stop-color="hsl(${h},55%,42%)"/><stop offset="1" stop-color="hsl(${(h + 40) % 360},60%,12%)"/></linearGradient>` +
    `<radialGradient id="r" cx="0.7" cy="0.25" r="0.6"><stop offset="0" stop-color="hsl(${(h + 20) % 360},90%,75%)" stop-opacity="0.75"/><stop offset="1" stop-color="#000" stop-opacity="0"/></radialGradient></defs>` +
    `<rect width="300" height="450" fill="url(#g)"/><rect width="300" height="450" fill="url(#r)"/>` +
    `<circle cx="${80 + (seed * 37) % 160}" cy="${120 + (seed * 13) % 90}" r="${40 + (seed % 5) * 12}" fill="hsl(${(h + 180) % 360},70%,60%)" fill-opacity="0.35"/>` +
    `<rect y="300" width="300" height="150" fill="#000" fill-opacity="0.25"/>${text}` +
    `<text x="30" y="440" font-family="Arial,sans-serif" font-size="16" fill="#fff" fill-opacity="0.7" letter-spacing="4">A WARP FIXTURE</text>`,
    300, 450,
  );
}

function backdrop(seed: number): string {
  const h = hue(seed);
  return svg(
    `<defs><linearGradient id="s" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="hsl(${(h + 200) % 360},45%,18%)"/><stop offset="0.6" stop-color="hsl(${h},55%,38%)"/><stop offset="1" stop-color="hsl(${(h + 30) % 360},50%,10%)"/></linearGradient>` +
    `<radialGradient id="sun" cx="0.68" cy="0.42" r="0.32"><stop offset="0" stop-color="hsl(${(h + 40) % 360},100%,82%)"/><stop offset="1" stop-color="hsl(${h},90%,50%)" stop-opacity="0"/></radialGradient></defs>` +
    `<rect width="1600" height="900" fill="url(#s)"/><rect width="1600" height="900" fill="url(#sun)"/>` +
    `<path d="M0 640 L260 520 L480 600 L760 470 L1040 590 L1300 500 L1600 610 L1600 900 L0 900Z" fill="hsl(${(h + 210) % 360},35%,9%)" fill-opacity="0.85"/>` +
    `<path d="M0 720 L340 640 L700 710 L1020 650 L1380 720 L1600 690 L1600 900 L0 900Z" fill="#05070a"/>`,
    1600, 900,
  );
}

function streams(seed: number): Stream[] {
  const out: Stream[] = [
    { id: `${seed}01`, streamType: 1, codec: seed % 3 ? "hevc" : "h264", width: 3840, height: 2160, bitrate: 32_000, colorTrc: "smpte2084", displayTitle: "4K HDR10" },
    { id: `${seed}11`, streamType: 2, codec: "truehd", channels: 8, selected: true, language: "en", displayTitle: "English (TrueHD 7.1)" },
    { id: `${seed}12`, streamType: 2, codec: "ac3", channels: 6, language: "en", displayTitle: "English (AC3 5.1)" },
    { id: `${seed}13`, streamType: 2, codec: "aac", channels: 2, language: "es", displayTitle: "Español (AAC Stereo)" },
    { id: `${seed}21`, streamType: 3, codec: "srt", language: "en", displayTitle: "English (SRT)" },
    { id: `${seed}22`, streamType: 3, codec: "srt", language: "en", displayTitle: "English SDH (SRT)" },
    { id: `${seed}23`, streamType: 3, codec: "pgs", language: "es", displayTitle: "Español (PGS)" },
  ];
  return out;
}

function parts(seed: number, duration: number): Part[] {
  return [
    { id: `${seed}1`, label: "4K HEVC HDR10 · 7.1", key: `/library/parts/${seed}1/file.mkv`, container: "mkv", duration, streams: streams(seed) },
    {
      id: `${seed}2`, label: "1080p H264 · 5.1", key: `/library/parts/${seed}2/file.mp4`, container: "mp4", duration,
      streams: [{ id: `${seed}31`, streamType: 1, codec: "h264", width: 1920, height: 1080, bitrate: 9_000 }, { id: `${seed}32`, streamType: 2, codec: "aac", channels: 6, selected: true, displayTitle: "English (AAC 5.1)" }],
    },
  ];
}

const SUMMARY = "A cartographer mapping the last uncharted coastline finds a town that appears on no chart and remembers her name. Each tide reveals a little more of what she left behind.";

function movie(index: number, withParts = false): MediaItem {
  const title = MOVIES[index % MOVIES.length] + (index >= MOVIES.length ? ` ${Math.floor(index / MOVIES.length) + 1}` : "");
  const duration = (88 + (index * 7) % 60) * 60_000;
  return {
    ratingKey: `m${index}`, key: `/library/metadata/m${index}`, type: "movie", title,
    year: 2026 - (index % 14), summary: SUMMARY, thumb: poster(title, index + 3), art: backdrop(index + 3), duration,
    contentRating: RATINGS[index % 3], audienceRating: 6.4 + ((index * 3) % 30) / 10, genres: [GENRES[index % GENRES.length], GENRES[(index + 2) % GENRES.length]],
    tagline: "Some maps lead home.",
    parts: withParts ? parts(index + 3, duration) : [],
  };
}

function show(index: number): MediaItem {
  const title = SHOWS[index % SHOWS.length] + (index >= SHOWS.length ? ` ${Math.floor(index / SHOWS.length) + 1}` : "");
  return {
    ratingKey: `s${index}`, key: `/library/metadata/s${index}/children`, type: "show", title, year: 2019 + (index % 7),
    summary: "Six strangers crew a salvage tug in the last port at the end of the world, and the sea keeps giving back things it should not have.",
    thumb: poster(title, index + 101), art: backdrop(index + 101), contentRating: RATINGS[3 + (index % 2)], audienceRating: 7.1 + (index % 20) / 10,
    genres: [GENRES[(index + 1) % GENRES.length]], leafCount: 30, viewedLeafCount: 4, parts: [],
  };
}

function season(showIndex: number, n: number): MediaItem {
  const parent = show(showIndex);
  return { ratingKey: `s${showIndex}-${n}`, key: "", type: "season", title: `Season ${n}`, index: n, parentTitle: parent.title, thumb: parent.thumb, art: parent.art, leafCount: 10, parts: [] };
}

const EPISODE_TITLES = ["Pilot", "Undertow", "The Weight of Water", "Lanterns", "Dead Reckoning", "Shoals", "Salt", "Beacon", "Fog Bank", "Landfall"];

function episode(showIndex: number, seasonN: number, n: number, withParts = false): MediaItem {
  const parent = show(showIndex);
  const seed = showIndex * 100 + seasonN * 10 + n;
  const duration = 48 * 60_000;
  return {
    ratingKey: `s${showIndex}-${seasonN}-${n}`, key: "", type: "episode", title: EPISODE_TITLES[(n - 1) % EPISODE_TITLES.length],
    grandparentTitle: parent.title, parentTitle: `Season ${seasonN}`, parentIndex: seasonN, index: n, year: parent.year,
    summary: "The crew hauls up a sealed container that is still warm. Nobody agrees on whether to open it.",
    thumb: backdrop(seed + 7), art: parent.art, grandparentArt: parent.art, duration, contentRating: parent.contentRating,
    viewCount: seasonN === 1 && n < 4 ? 1 : undefined,
    parts: withParts ? parts(seed, duration) : [],
  };
}

function withProgress(item: MediaItem, fraction: number): MediaItem {
  const duration = item.duration || 3_000_000;
  return { ...item, duration, viewOffset: Math.round(duration * fraction) };
}

function hubs(): Hub[] {
  return [
    {
      identifier: "home.continue", title: "Continue Watching",
      items: [withProgress(movie(1), 0.42), withProgress(episode(0, 1, 4), 0.18), withProgress(movie(6), 0.71), withProgress(episode(2, 2, 3), 0.55), withProgress(movie(9), 0.12), withProgress(episode(4, 1, 2), 0.88)],
    },
    { identifier: "movie.recentlyadded", title: "Recently Added Movies", items: Array.from({ length: 18 }, (_, i) => movie(i + 2)) },
    { identifier: "tv.recentlyadded", title: "Recently Added TV", items: Array.from({ length: 12 }, (_, i) => show(i)) },
    { identifier: "movie.toprated", title: "Top Rated", items: Array.from({ length: 14 }, (_, i) => movie(i * 3 + 1)) },
  ];
}

const delay = <T,>(value: T, ms = 60): Promise<T> => new Promise((resolve) => setTimeout(() => resolve(value), ms));

export function mockSource(): LibrarySource {
  return {
    hubs: () => delay(hubs()),
    sections: () => delay({ movieSectionId: "1", showSectionId: "2" }),
    sectionAll: (sectionId) => delay(sectionId === "1" ? Array.from({ length: 1200 }, (_, i) => movie(i)) : Array.from({ length: 96 }, (_, i) => show(i))),
    metadata: (ratingKey) => {
      const [kind, ...rest] = ratingKey.slice(1).split("-");
      if (ratingKey.startsWith("m")) return delay(withProgress(movie(Number(ratingKey.slice(1)), true), ratingKey === "m1" ? 0.42 : 0));
      const showIndex = Number(kind);
      if (rest.length === 2) return delay(episode(showIndex, Number(rest[0]), Number(rest[1]), true));
      if (rest.length === 1) return delay(season(showIndex, Number(rest[0])));
      return delay(show(showIndex));
    },
    children: (ratingKey) => {
      const [kind, ...rest] = ratingKey.slice(1).split("-");
      if (rest.length === 0) return delay([1, 2, 3].map((n) => season(Number(kind), n)));
      return delay(Array.from({ length: 10 }, (_, i) => episode(Number(kind), Number(rest[0]), i + 1)));
    },
    selectStreams: () => delay(undefined),
    image: (path) => path || "",
  };
}
