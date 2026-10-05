import { readFileSync } from "node:fs";

const html = readFileSync("apps/tizen/dist/index.html", "utf8");
const config = readFileSync("apps/tizen/dist/config.xml", "utf8");
const js = readFileSync("apps/tizen/dist/assets/app.js", "utf8");
if (!html.includes("./assets/app.js")) throw new Error("index.html is not a relative Tizen bundle");
if (html.includes("http://localhost")) throw new Error("bundle points at localhost");
if (!config.includes("WarpTV2022.Warp")) throw new Error("config.xml missing app id");
if (js.includes("Promise.withResolvers")) throw new Error("bundle uses Promise.withResolvers, which Chromium 85 lacks");
if (!js.includes("direct-play")) throw new Error("playback decision was tree-shaken out");
console.log("dist ok", js.length);
