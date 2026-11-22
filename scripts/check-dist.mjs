import { readFileSync } from "node:fs";

const html = readFileSync("apps/tizen/dist/index.html", "utf8");
const config = readFileSync("apps/tizen/dist/config.xml", "utf8");
const js = readFileSync("apps/tizen/dist/assets/app.js", "utf8");
if (html.includes('type="module"')) throw new Error("type=module does not run from a file:// Tizen widget");
if (/\scrossorigin(?:=|\s|>)/.test(html)) throw new Error("crossorigin blocks the file:// script");
if (!html.includes("./assets/app.js")) throw new Error("index.html is not a relative Tizen bundle");
if (!/<script[^>]*\bdefer\b[^>]*src="\.\/assets\/app\.js"|<script[^>]*src="\.\/assets\/app\.js"[^>]*\bdefer\b/.test(html)) {
  throw new Error("head script without defer runs before #root exists");
}
if (js.includes("Promise.withResolvers")) throw new Error("bundle uses Promise.withResolvers, which Chromium 85 lacks");
if (js.includes("A WARP FIXTURE") || js.includes("warp-mock")) throw new Error("mock fixture leaked into the production bundle");
if (!/version="0\.2\.0"/.test(config)) throw new Error("config.xml version is not 0.2.0");
if (/backdrop-filter|:has\(/.test(readFileSync("apps/tizen/dist/assets/app.css", "utf8"))) throw new Error("CSS uses a feature Chromium 85 lacks");
if (!js.includes("direct-play")) throw new Error("playback decision was tree-shaken out");
console.log("dist ok", js.length);
