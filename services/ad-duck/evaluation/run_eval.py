"""Closed-loop synthetic evaluation: precision/recall of duck and restore onsets.

    python -m evaluation.run_eval            # held-out test seeds
    python -m evaluation.run_eval --dev      # tuning seeds

Matching (one-to-one, greedy in time):
* duck onset is a hit if it lands in [pod_start, pod_start + DUCK_LATE_S];
* restore onset is a hit if it lands in [pod_end - RESTORE_EARLY_S,
  pod_end + RESTORE_LATE_S]. The late tolerance is the tighter one, because
  ads coming back on is an annoyance but the show staying quiet is worse.
Every unmatched duck/restore is a false positive; every unmatched pod
boundary is a miss. Also reported: seconds of programme ducked, share of ad
time ducked, and restore lateness.
"""

from __future__ import annotations

import argparse
import json
import sys
from dataclasses import dataclass
from typing import Dict, List, Optional, Sequence, Tuple

import numpy as np

from adduck.detect import AdDetector, DetectorConfig
from adduck.engine import Engine
from adduck.ringbuffer import RingBuffer
from adduck.state import AdStateMachine, StateConfig
from adduck.tv import Ducker, DuckerConfig, FakeTV

from .synth import BLOCK, SCENARIOS, SR, Room, Scenario, build_timeline

DUCK_LATE_S = 12.0
RESTORE_EARLY_S = 4.0
RESTORE_LATE_S = 4.0
DEV_SEEDS = (0, 1, 2)
TEST_SEEDS = (100, 101, 102, 103, 104)


@dataclass
class RunResult:
    scenario: str
    seed: int
    pods: List[Tuple[float, float]]
    ducks: List[float]
    restores: List[float]
    ducked_intervals: List[Tuple[float, float]]
    duration: float


def simulate(sc: Scenario, seed: int, det_cfg: Optional[DetectorConfig] = None, st_cfg: Optional[StateConfig] = None) -> RunResult:
    rng = np.random.default_rng(seed)
    tl = build_timeline(sc, rng)
    room = Room(tl, sc, reference_volume=20, rng=rng)
    tv = FakeTV(volume=20)
    engine = Engine(AdDetector(det_cfg), AdStateMachine(st_cfg), Ducker(tv, DuckerConfig()))
    ring = RingBuffer(SR)
    ducks: List[float] = []
    restores: List[float] = []
    t = 0.0
    while t < tl.duration:
        for f in ring.push(room.block(t, tv.volume)):
            step = engine.step(f, t)
            if step.action == "duck":
                ducks.append(step.ts)
            elif step.action and (step.action.startswith("restore") or step.action.startswith("cancel")):
                restores.append(step.ts)
        t += BLOCK / SR
    if engine.ducker.ducked:
        restores.append(t)
        engine.fail_safe(t)
    intervals = list(zip(ducks, restores))
    return RunResult(sc.name, seed, tl.pods, ducks, restores, intervals, tl.duration)


def _match(events: Sequence[float], targets: Sequence[float], early: float, late: float) -> Tuple[int, List[float]]:
    used = set()
    hits = 0
    offsets: List[float] = []
    for e in events:
        best = None
        for i, tgt in enumerate(targets):
            if i in used:
                continue
            if tgt - early <= e <= tgt + late and (best is None or abs(e - tgt) < abs(e - targets[best])):
                best = i
        if best is not None:
            used.add(best)
            hits += 1
            offsets.append(e - targets[best])
    return hits, offsets


def _overlap(a: Sequence[Tuple[float, float]], b: Sequence[Tuple[float, float]]) -> float:
    return sum(max(0.0, min(x1, y1) - max(x0, y0)) for x0, x1 in a for y0, y1 in b)


def score(results: Sequence[RunResult]) -> Dict[str, float]:
    d_hit = d_n = r_hit = r_n = n_pods = 0
    lat_duck: List[float] = []
    lat_rest: List[float] = []
    prog_ducked = ad_total = ad_ducked = 0.0
    for r in results:
        starts = [p[0] for p in r.pods]
        ends = [p[1] for p in r.pods]
        h, off = _match(r.ducks, starts, 0.0, DUCK_LATE_S)
        d_hit += h
        d_n += len(r.ducks)
        lat_duck += off
        h, off = _match(r.restores, ends, RESTORE_EARLY_S, RESTORE_LATE_S)
        r_hit += h
        r_n += len(r.restores)
        lat_rest += off
        n_pods += len(r.pods)
        ducked = _overlap(r.ducked_intervals, [(0.0, r.duration)])
        in_ad = _overlap(r.ducked_intervals, r.pods)
        ad_total += sum(e - s for s, e in r.pods)
        ad_ducked += in_ad
        prog_ducked += ducked - in_ad
    div = lambda a, b: (a / b) if b else float("nan")
    return {
        "pods": n_pods,
        "duck_precision": div(d_hit, d_n),
        "duck_recall": div(d_hit, n_pods),
        "restore_precision": div(r_hit, r_n),
        "restore_recall": div(r_hit, n_pods),
        "duck_latency_median_s": float(np.median(lat_duck)) if lat_duck else float("nan"),
        "restore_offset_median_s": float(np.median(lat_rest)) if lat_rest else float("nan"),
        "restore_offset_max_s": float(np.max(lat_rest)) if lat_rest else float("nan"),
        "ad_time_ducked_share": div(ad_ducked, ad_total),
        "programme_s_ducked": prog_ducked,
        "ducks": d_n,
        "restores": r_n,
    }


def main(argv: Optional[Sequence[str]] = None) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--dev", action="store_true", help="use tuning seeds")
    ap.add_argument("--json", action="store_true")
    args = ap.parse_args(argv)
    seeds = DEV_SEEDS if args.dev else TEST_SEEDS
    report: Dict[str, Dict[str, float]] = {}
    everything: List[RunResult] = []
    for sc in SCENARIOS:
        runs = [simulate(sc, s) for s in seeds]
        everything += runs
        report[sc.name] = score(runs)
    report["ALL"] = score(everything)
    if args.json:
        json.dump(report, sys.stdout, indent=2)
        print()
    else:
        for name, m in report.items():
            print(
                f"{name:14s} pods={m['pods']:3d} duckP={m['duck_precision']:.2f} duckR={m['duck_recall']:.2f} "
                f"restP={m['restore_precision']:.2f} restR={m['restore_recall']:.2f} "
                f"duckLat={m['duck_latency_median_s']:.1f}s restOff(med/max)={m['restore_offset_median_s']:.1f}/{m['restore_offset_max_s']:.1f}s "
                f"adDucked={m['ad_time_ducked_share']:.2f} progDucked={m['programme_s_ducked']:.0f}s"
            )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
