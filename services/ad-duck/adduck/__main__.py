"""Run the ducker against the room microphone.

    python -m adduck            # dry run: prints intended duck/restore only

Only actions and confidences are printed - never audio, never features.
A real TV backend (Samsung, branch feat/tv-voice) plugs in through
``adduck.tv.TVBackend``; until it is wired here this entry point is dry-run
only.
"""

from __future__ import annotations

import argparse

from .capture import mic_features
from .detect import AdDetector
from .engine import Engine
from .state import AdStateMachine
from .tv import DryRunTV, Ducker, DuckerConfig


def main() -> int:
    ap = argparse.ArgumentParser(prog="adduck")
    ap.add_argument("--volume", type=int, default=20, help="pretend starting TV volume (dry run)")
    ap.add_argument("--duck-level", type=int, default=3)
    args = ap.parse_args()
    tv = DryRunTV(volume=args.volume)
    engine = Engine(AdDetector(), AdStateMachine(), Ducker(tv, DuckerConfig(duck_level=args.duck_level)))
    ts = 0.0
    try:
        for f in mic_features():
            step = engine.step(f, ts)
            ts += 0.5
            if step.action:
                print(f"t={step.ts:8.1f}s {step.action:16s} confidence={step.confidence:.2f} tv_volume={tv.volume}")
    except KeyboardInterrupt:
        pass
    finally:
        engine.fail_safe(ts)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
