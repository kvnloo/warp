import { memo } from "react";
import type { PickOption } from "../details-model";

/** Side sheet for versions, audio and subtitles. Solid panel: no backdrop-filter on this GPU. */
export const Picker = memo(function Picker({ title, options, index, current }: {
  title: string;
  options: PickOption[];
  index: number;
  current: string;
}) {
  const start = Math.max(0, Math.min(options.length - 8, index - 3));
  return (
    <div className="picker-layer">
      <aside className="picker">
        <p className="overline">Choose</p>
        <h2 className="picker-title">{title}</h2>
        <ul>
          {options.slice(start, start + 8).map((option, offset) => (
            <li key={option.id} className={`${start + offset === index ? "is-focused" : ""}${option.id === current ? " is-selected" : ""}`}>
              <span className="tick">{option.id === current ? "✓" : ""}</span>{option.label}
            </li>
          ))}
        </ul>
        <p className="picker-hint">OK to choose · Back to close</p>
      </aside>
    </div>
  );
});
