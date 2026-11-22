import { memo, useEffect, useState } from "react";

export function formatClock(date: Date): string {
  const hours = date.getHours();
  const h12 = hours % 12 || 12;
  return `${h12}:${String(date.getMinutes()).padStart(2, "0")} ${hours < 12 ? "AM" : "PM"}`;
}

/** Owns its own timer so a minute tick re-renders only this text node. */
export const Clock = memo(function Clock() {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    let timer = 0;
    const schedule = () => {
      const date = new Date();
      timer = window.setTimeout(() => { setNow(new Date()); schedule(); }, 60_000 - date.getSeconds() * 1000 - date.getMilliseconds() + 50);
    };
    schedule();
    return () => window.clearTimeout(timer);
  }, []);
  return <span className="clock">{formatClock(now)}</span>;
});
