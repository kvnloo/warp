import { memo } from "react";
import { Clock } from "./Clock";

export const TABS = ["Home", "Movies", "Shows", "Settings"] as const;
export type Tab = (typeof TABS)[number];

export const NavBar = memo(function NavBar({ active, focusIndex, clock, server }: {
  active: number;
  /** -1 when focus is not in the nav. */
  focusIndex: number;
  clock: boolean;
  server: string;
}) {
  return (
    <header className={focusIndex >= 0 ? "nav nav-active" : "nav"}>
      <div className="brand"><i className="brand-mark" />warp</div>
      <nav className="tabs">
        {TABS.map((tab, index) => (
          <span key={tab} className={`tab${index === active ? " is-current" : ""}${index === focusIndex ? " is-focused" : ""}`}>{tab}</span>
        ))}
      </nav>
      <div className="nav-right">
        {server && <span className="server">{server}</span>}
        {clock && <Clock />}
      </div>
    </header>
  );
});
