// Drive the mock build with real D-pad key events over CDP and save 1920x1080 screenshots.
// Usage: node scripts/proof-screens.mjs <mock-dist-dir> <out-dir>   (CHROME=/usr/bin/chromium by default)
// Uses an already-installed Chromium; downloads nothing. Not part of the TV bundle.
import { spawn } from "node:child_process";
import { mkdirSync, mkdtempSync, writeFileSync } from "node:fs";
import path from "node:path";

const [distDir, outDir] = process.argv.slice(2).map((p) => path.resolve(p));
if (!distDir || !outDir) throw new Error("usage: proof-screens.mjs <mock-dist> <out>");
mkdirSync(outDir, { recursive: true });
const chrome = process.env.CHROME || "/usr/bin/chromium";
mkdirSync(path.join(path.dirname(outDir), ".cache"), { recursive: true });
const profile = mkdtempSync(path.join(path.dirname(outDir), ".cache", "chrome-proof-"));
const port = 9300 + Math.floor(Math.random() * 500);
const proc = spawn(chrome, [
  "--headless=new", `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`,
  "--window-size=1920,1080", "--force-device-scale-factor=1", "--hide-scrollbars",
  "--disable-gpu", "--no-first-run", "--allow-file-access-from-files", "about:blank",
], { stdio: ["ignore", "ignore", "pipe"] });

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
let ws;
for (let attempt = 0; attempt < 50 && !ws; attempt += 1) {
  await sleep(200);
  try {
    const list = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
    const page = list.find((entry) => entry.type === "page");
    if (page) ws = new WebSocket(page.webSocketDebuggerUrl);
  } catch { /* not up yet */ }
}
if (!ws) throw new Error("chromium did not start");
await new Promise((resolve, reject) => { ws.onopen = resolve; ws.onerror = reject; });
let id = 0;
const pending = new Map();
const consoleLines = [];
ws.onmessage = (event) => {
  const msg = JSON.parse(event.data);
  if (msg.id && pending.has(msg.id)) { pending.get(msg.id)(msg); pending.delete(msg.id); }
  if (msg.method === "Runtime.exceptionThrown") consoleLines.push(`EXCEPTION ${msg.params.exceptionDetails.text} ${msg.params.exceptionDetails.exception?.description ?? ""}`);
  if (msg.method === "Runtime.consoleAPICalled" && msg.params.type === "error") consoleLines.push(`console.error ${msg.params.args.map((a) => a.value ?? a.description).join(" ")}`);
};
const send = (method, params = {}) => new Promise((resolve) => { const n = ++id; pending.set(n, resolve); ws.send(JSON.stringify({ id: n, method, params })); });

await send("Runtime.enable");
await send("Page.enable");
await send("Emulation.setDeviceMetricsOverride", { width: 1920, height: 1080, deviceScaleFactor: 1, mobile: false });
await send("Page.navigate", { url: `file://${distDir}/index.html` });
await sleep(1500);

const KEYS = { left: [37, "ArrowLeft"], up: [38, "ArrowUp"], right: [39, "ArrowRight"], down: [40, "ArrowDown"], enter: [13, "Enter"], back: [8, "Backspace"] };
async function key(name, times = 1, gap = 140) {
  for (let n = 0; n < times; n += 1) {
    const [code, keyName] = KEYS[name];
    await send("Input.dispatchKeyEvent", { type: "rawKeyDown", windowsVirtualKeyCode: code, nativeVirtualKeyCode: code, key: keyName, code: keyName });
    await send("Input.dispatchKeyEvent", { type: "keyUp", windowsVirtualKeyCode: code, nativeVirtualKeyCode: code, key: keyName, code: keyName });
    await sleep(gap);
  }
}
const shots = [];
async function shot(name, settle = 700) {
  await sleep(settle);
  const { result } = await send("Page.captureScreenshot", { format: "png" });
  const file = path.join(outDir, `${name}.png`);
  writeFileSync(file, Buffer.from(result.data, "base64"));
  shots.push(file);
}
async function evaluate(expression) {
  const { result } = await send("Runtime.evaluate", { expression, returnByValue: true, awaitPromise: true });
  return result?.result?.value;
}

// Key-to-focus timing in this headless desktop browser (NOT a TV number).
async function keyTiming(name, samples = 12) {
  const times = [];
  for (let n = 0; n < samples; n += 1) {
    await evaluate(`window.__t0 = performance.now(); 0`);
    await key(name, 1, 0);
    times.push(await evaluate(`new Promise(r => requestAnimationFrame(() => r(performance.now() - window.__t0)))`));
    await sleep(60);
  }
  times.sort((a, b) => a - b);
  return { median: Math.round(times[Math.floor(times.length / 2)] * 10) / 10, max: Math.round(times[times.length - 1] * 10) / 10 };
}

await shot("01-home-hero", 1200);
await key("down");
await key("right", 2);
await shot("02-home-continue-rail");
await key("down");
await key("right", 3);
await shot("03-home-movies-rail");
const railTiming = await keyTiming("right", 10);
await key("left", 10, 60);
await key("enter");
await shot("04-details-movie", 1200);
await key("right", 2);
await key("enter");
await shot("05-details-audio-picker");
await key("back");
await key("back");
await key("down");
await key("enter");
await sleep(800);
await key("down", 2);
await key("right", 1);
await shot("06-details-show-episodes", 1200);
await key("back");
await key("up", 4);
await key("right", 1);
await key("enter");
await shot("07-library-movies", 1200);
const gridTiming = await keyTiming("down", 10);
await key("up", 30, 40);
await key("right", 2);
await key("enter");
await shot("08-settings-appearance");
// customise: accent -> Teal, background -> Gradient, poster -> Large, focus -> Ring
await key("right", 3);
await key("down");
await key("left");
await key("down");
await key("right");
await key("down");
await key("down");
await key("left");
await shot("09-settings-customised");
await key("back");
await key("down");
await key("down");
await key("right", 1);
await shot("10-home-customised", 1200);

const stored = await evaluate(`localStorage.getItem("warp.prefs.v1")`);
// Home rows: hide Continue Watching, move Recently Added TV above Movies.
await key("up", 3);
await key("right", 3);
await key("enter");
await key("down", 9);
await key("left", 2);
await key("enter");
await key("down", 2);
await key("right");
await key("enter");
await shot("11-settings-home-rows");
await key("back");
await shot("12-home-reordered", 1200);
await key("up");
await key("right", 3);
await key("enter");
await key("down", 14);
await key("enter");
await shot("13-settings-after-reset");
const afterReset = await evaluate(`localStorage.getItem("warp.prefs.v1")`);
const rootVars = await evaluate(`getComputedStyle(document.documentElement).getPropertyValue("--accent").trim() + " " + document.documentElement.getAttribute("data-focus")`);
const receipt = { shots, railTiming, gridTiming, storedPrefs: stored, afterReset, rootVars, console: consoleLines, note: "headless desktop Chromium on groot, not QN85B" };
writeFileSync(path.join(outDir, "receipt.json"), JSON.stringify(receipt, null, 2));
console.log(JSON.stringify({ ...receipt, shots: shots.length }, null, 2));
ws.close();
proc.kill("SIGTERM");
