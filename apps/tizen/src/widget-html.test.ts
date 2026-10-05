import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { toClassicWidgetHtml, widgetLaunchBlockers } from "./widget-html";

const broken = `<script type="module" crossorigin src="./assets/app.js"></script>`;

describe("Tizen 6.5 widget launch", () => {
  it("rejects the Vite module script that dies on the TV", () => {
    expect(widgetLaunchBlockers(broken)).toEqual([
      "type=module does not run from a file:// Tizen widget",
      "crossorigin blocks the file:// script",
      "head script without defer runs before #root exists",
    ]);
  });

  it("rewrites that script into a deferred classic script", () => {
    const fixed = toClassicWidgetHtml(broken);
    expect(widgetLaunchBlockers(fixed)).toEqual([]);
    expect(fixed).toContain('<script defer src="./assets/app.js"></script>');
  });

  it("the packaged index.html can execute on the QN85B", () => {
    const html = readFileSync("apps/tizen/dist/index.html", "utf8");
    expect(widgetLaunchBlockers(html)).toEqual([]);
  });
});
