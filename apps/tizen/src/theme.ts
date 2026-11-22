import { cssVars, rootAttributes, type Prefs } from "./prefs";

let applied: Record<string, string> = {};

/** Write only the properties that changed, so a live preview costs one style recalc. */
export function applyTheme(prefs: Prefs, root: HTMLElement = document.documentElement): void {
  const next = { ...cssVars(prefs), ...rootAttributes(prefs) };
  for (const [name, value] of Object.entries(next)) {
    if (applied[name] === value) continue;
    if (name.startsWith("--")) root.style.setProperty(name, value);
    else root.setAttribute(name, value);
  }
  applied = next;
}
