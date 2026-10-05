export function widgetLaunchBlockers(html: string): string[] {
  const problems: string[] = [];
  if (html.includes('type="module"')) {
    problems.push("type=module does not run from a file:// Tizen widget");
  }
  if (/\scrossorigin(?:=|\s|>)/.test(html)) {
    problems.push("crossorigin blocks the file:// script");
  }
  if (!html.includes("./assets/app.js")) {
    problems.push("missing relative app script");
  }
  const deferred = /<script[^>]*\bdefer\b[^>]*src="\.\/assets\/app\.js"|<script[^>]*src="\.\/assets\/app\.js"[^>]*\bdefer\b/.test(html);
  if (html.includes("./assets/app.js") && !deferred) {
    problems.push("head script without defer runs before #root exists");
  }
  return problems;
}

export function toClassicWidgetHtml(html: string): string {
  const classic = html
    .replace(/\s+type="module"/g, "")
    .replace(/\s+crossorigin(?:="[^"]*")?/g, "")
    .replace(/<link rel="modulepreload"[^>]*>\s*/g, "");
  return classic.replace(
    /<script(?![^>]*\bdefer\b)([^>]*src="\.\/assets\/app\.js"[^>]*)><\/script>/,
    "<script defer$1></script>",
  );
}
