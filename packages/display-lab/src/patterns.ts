export interface Pattern {
  id: string;
  title: string;
  kind: "field" | "steps" | "gradient" | "checker" | "windows";
  background: string;
  patches?: { label: string; color: string }[];
}

const fields: Array<[string, string, string]> = [
  ["field-black", "Full field 0%", "#000000"],
  ["field-5", "Full field 5%", "#0d0d0d"],
  ["field-10", "Full field 10%", "#1a1a1a"],
  ["field-25", "Full field 25%", "#404040"],
  ["field-50", "Full field 50%", "#808080"],
  ["field-75", "Full field 75%", "#bfbfbf"],
  ["field-100", "Full field 100%", "#ffffff"],
  ["near-black-1", "Near-black 1/255", "#010101"],
  ["near-black-4", "Near-black 4/255", "#040404"],
  ["near-white-251", "Near-white 251/255", "#fbfbfb"],
  ["near-white-254", "Near-white 254/255", "#fefefe"],
];

export const PATTERNS: readonly Pattern[] = [
  ...fields.map(([id, title, background]) => ({ id, title, kind: "field" as const, background })),
  {
    id: "gray-steps-16",
    title: "16 grayscale steps",
    kind: "steps",
    background: "#000",
    patches: Array.from({ length: 16 }, (_, index) => {
      const value = Math.round((index * 255) / 15);
      const hex = value.toString(16).padStart(2, "0");
      return { label: String(value), color: `#${hex}${hex}${hex}` };
    }),
  },
  {
    id: "rgb-cmy",
    title: "RGB/CMY patches",
    kind: "steps",
    background: "#000",
    patches: ["#ff0000", "#00ff00", "#0000ff", "#00ffff", "#ff00ff", "#ffff00"].map((color) => ({
      label: color,
      color,
    })),
  },
  {
    id: "saturation-sweep",
    title: "Saturation sweep",
    kind: "steps",
    background: "#202020",
    patches: [0, 20, 40, 60, 80, 100].map((amount) => ({
      label: `${amount}%`,
      color: `hsl(210  ${amount}% 50%)`,
    })),
  },
  { id: "gradient-h", title: "Horizontal gradient", kind: "gradient", background: "linear-gradient(90deg,#000,#fff)" },
  { id: "checker-8", title: "8px checkerboard", kind: "checker", background: "#000" },
  { id: "windows-10", title: "Local-dimming 10% window", kind: "windows", background: "#000" },
  { id: "windows-25", title: "Local-dimming 25% window", kind: "windows", background: "#000" },
];

export function patternById(id: string): Pattern | undefined {
  return PATTERNS.find((pattern) => pattern.id === id);
}
