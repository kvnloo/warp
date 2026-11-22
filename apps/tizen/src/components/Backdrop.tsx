import { memo, useEffect, useState } from "react";

/**
 * Dimmed art of the focused title behind everything. Swaps only after focus rests
 * for 320 ms, so fast D-pad scrubbing never decodes a backdrop per keypress.
 */
export const Backdrop = memo(function Backdrop({ src }: { src: string }) {
  const [shown, setShown] = useState(src);
  useEffect(() => {
    if (src === shown) return undefined;
    const timer = window.setTimeout(() => setShown(src), 320);
    return () => window.clearTimeout(timer);
  }, [src, shown]);
  return (
    <div className="backdrop">
      {shown && <img key={shown} alt="" src={shown} decoding="async" onLoad={(event) => { event.currentTarget.style.opacity = "1"; }} />}
      <div className="backdrop-scrim" />
    </div>
  );
});
