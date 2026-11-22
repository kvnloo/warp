import { memo } from "react";
import type { MediaItem } from "@warp/plex";
import { cardMetrics, textScale } from "../layout";
import { ACCENTS, type Prefs } from "../prefs";
import { RAIL_BUTTONS, optionValue, type SettingsPos, type SettingsRow } from "../settings-model";
import { Card, type ImageFn } from "./Card";

const ROW_H = 112;
const GROUP_H = 84;

/** Row tops in px, group headers included; scroll math and rendering share this. */
export function settingsLayout(rows: readonly SettingsRow[], ts: number): { tops: number[]; groups: Array<{ label: string; top: number }>; height: number } {
  const tops: number[] = [];
  const groups: Array<{ label: string; top: number }> = [];
  let y = 0;
  let group = "";
  for (const row of rows) {
    if (row.group !== group) {
      group = row.group;
      groups.push({ label: group, top: y });
      y += Math.round(GROUP_H * ts);
    }
    tops.push(y);
    y += Math.round(ROW_H * ts);
  }
  return { tops, groups, height: y };
}

function Choices({ row, prefs, focusCol }: { row: SettingsRow; prefs: Prefs; focusCol: number }) {
  if (row.kind === "option") {
    const current = optionValue(prefs, row.id);
    return (
      <div className={row.id === "accent" ? "choices swatches" : "choices"}>
        {row.choices.map((choice, index) => {
          const cls = `choice${choice.value === current ? " is-selected" : ""}${index === focusCol ? " is-focused" : ""}`;
          return choice.swatch
            ? <span key={choice.value} className={`${cls} swatch`} style={{ background: choice.swatch }} />
            : <span key={choice.value} className={cls}>{choice.label}</span>;
        })}
      </div>
    );
  }
  if (row.kind === "rail") {
    return (
      <div className="choices">
        {RAIL_BUTTONS.map((button, index) => {
          const disabled = (button.value === "up" && row.index === 0) || (button.value === "down" && row.index === row.count - 1);
          const label = button.value === "toggle" ? (row.visible ? "Shown" : "Hidden") : button.value === "up" ? "▲" : "▼";
          return <span key={button.value} className={`choice${button.value === "toggle" && row.visible ? " is-selected" : ""}${disabled ? " is-disabled" : ""}${index === focusCol ? " is-focused" : ""}`}>{label}</span>;
        })}
      </div>
    );
  }
  return (
    <div className="choices">
      {row.buttons.map((button, index) => <span key={button.value} className={`choice choice-action${index === focusCol ? " is-focused" : ""}`}>{button.label}</span>)}
    </div>
  );
}

function hintOf(row: SettingsRow, prefs: Prefs): string {
  if (row.kind === "option" && row.id === "accent") return `${ACCENTS[prefs.accent].label} · ${row.hint}`;
  if (row.kind === "rail") return row.visible ? `Row ${row.index + 1} on Home` : "Hidden from Home";
  return row.hint;
}

export const Settings = memo(function Settings({ rows, pos, prefs, active, server, preview, image }: {
  rows: SettingsRow[];
  pos: SettingsPos;
  prefs: Prefs;
  /** False while focus is in the nav bar. */
  active: boolean;
  server: string;
  preview: MediaItem[];
  image: ImageFn;
}) {
  const ts = textScale(prefs);
  const layout = settingsLayout(rows, ts);
  const viewport = 1080 - 300;
  const focusTop = layout.tops[pos.row] ?? 0;
  const scroll = Math.max(0, Math.min(layout.height - viewport + 40, focusTop - viewport / 3));
  const poster = cardMetrics("recent", prefs);
  return (
    <section className="settings">
      <div className="settings-head">
        <h1 className="page-title">Settings</h1>
        <p className="page-sub">Changes apply as you move. {server ? `Connected to ${server}.` : ""}</p>
      </div>
      <div className="settings-viewport">
        <div className="settings-list" style={{ height: layout.height, transform: `translate3d(0,${-scroll}px,0)` }}>
          {layout.groups.map((group) => <h2 key={group.label} className="settings-group" style={{ top: group.top }}>{group.label}</h2>)}
          {rows.map((row, index) => (
            <div key={`${row.kind}:${row.id}`} className={`srow${active && index === pos.row ? " is-focused" : ""}`} style={{ top: layout.tops[index] }}>
              <div className="srow-text">
                <div className="srow-label">{row.label}</div>
                <div className="srow-hint">{hintOf(row, prefs)}</div>
              </div>
              <Choices row={row} prefs={prefs} focusCol={active && index === pos.row ? pos.col : -1} />
            </div>
          ))}
        </div>
      </div>
      <aside className="preview">
        <p className="overline">Preview</p>
        <div className="preview-cards" style={{ height: poster.height }}>
          {preview.slice(0, 2).map((item, index) => (
            <Card key={item.ratingKey} item={item} focused={index === 1} metrics={poster} x={index * (poster.width + poster.gap)} y={0} image={image} />
          ))}
        </div>
        <div className="preview-actions">
          <span className="btn btn-primary is-focused"><i className="icon-play" />Play</span>
          <span className="btn">More info</span>
        </div>
      </aside>
    </section>
  );
});
