"""Rule-based ad detector over ``Features`` only (never raw audio).

Cues, each a vote in [0, 1]:

* loudness - short-term level vs a slow rolling programme baseline. The
  baseline freezes while the level is elevated, so a hot pod cannot drag it
  up, and unfreezes if the elevation outlasts any plausible pod.
* crest    - peak-to-RMS drop vs the programme baseline (compressed ads);
  worth at most half a vote so a music bed cannot duck by itself.
* silence  - a quiet gap (pod boundary) gives a short decaying boost.
* grid     - quiet gaps spaced on the 15/30/45/60 s ad-unit grid hold the
  vote up through a pod; it never ducks alone.
* clock    - optional prior: breaks tend to recur at a channel's interval.

The structure (frozen baseline, crest half-vote, duck compensation, unit
grid) follows AdHush (MIT, (c) 2026 Paul D. Pruitt): src/adhush/detect/
loudness.py, silence.py, ad_units.py, clock.py, fusion.py. Re-implemented
here over 2 Hz feature records; see NOTICE.md.

Duck compensation: a room microphone hears the TV get quieter the moment we
duck it, which would read as "programme is back". When told about a duck the
detector freezes its vote for one window, measures how far the room level
fell, and adds that back to later readings. If the ducked set is buried in
room noise, loudness goes inert and the ceiling / grid decide.
"""

from __future__ import annotations

import math
from collections import deque
from dataclasses import dataclass, field
from typing import Deque, List, Optional

from .features import BLOCK_S, Features

AD_UNITS_S = (15.0, 30.0, 45.0, 60.0)


@dataclass
class DetectorConfig:
    window_blocks: int = 6  # 3 s short-term window
    baseline_tau_s: float = 60.0
    warmup_s: float = 20.0
    delta_db: float = 3.0  # loudness jump that is "most of a vote"
    full_conf_factor: float = 1.4
    crest_drop_db: float = 2.0
    crest_share: float = 0.5
    max_elevated_s: float = 300.0
    quiet_below_baseline_db: float = 18.0
    silence_gate_db: float = -50.0
    separator_merge_s: float = 2.0
    silence_boost: float = 0.2
    silence_decay_s: float = 4.0
    grid_tolerance_s: float = 1.25
    grid_weight: float = 0.35
    grid_hold_s: float = 20.0
    duck_settle_s: float = 4.0
    max_duck_offset_db: float = 35.0
    buried_headroom_db: float = 6.0
    clock_period_s: Optional[float] = None  # e.g. 900 for "every ~15 min"
    clock_window_s: float = 90.0
    clock_bias: float = 0.1


@dataclass
class Vote:
    ts: float
    confidence: float
    loudness: float = 0.0
    crest: float = 0.0
    silence: float = 0.0
    grid: float = 0.0
    clock: float = 0.0
    reason: str = ""


def _power(db: float) -> float:
    return 10.0 ** (db / 10.0)


def unit_fit(gap_s: float, tolerance_s: float) -> Optional[float]:
    for unit in AD_UNITS_S:
        if abs(gap_s - unit) <= tolerance_s:
            return unit
    return None


@dataclass
class _DuckState:
    active: bool = False
    pre_level: Optional[float] = None
    settle_until: Optional[float] = None
    frozen: Optional[float] = None
    offset_db: float = 0.0
    buried: bool = False


class AdDetector:
    def __init__(self, config: Optional[DetectorConfig] = None) -> None:
        self.cfg = config or DetectorConfig()
        self._win: Deque[Features] = deque(maxlen=self.cfg.window_blocks)
        self.baseline_db: Optional[float] = None
        self.baseline_crest: Optional[float] = None
        self._observed_s = 0.0
        self._elevated_s = 0.0
        self._short_db = -90.0
        self._short_crest = 0.0
        self._quiet_since: Optional[float] = None
        self._separators: Deque[float] = deque(maxlen=16)
        self._grid_fits = 0
        self._pod_starts: List[float] = []
        self._duck = _DuckState()
        self._skip_baseline_until = -math.inf
        self.now = 0.0

    # -- inputs -----------------------------------------------------------
    def on_duck(self, ts: float, ducked: bool) -> None:
        """The engine changed the TV volume; compensate for what the mic hears."""
        if not ducked:
            self._duck = _DuckState()
            self._skip_baseline_until = ts + self.cfg.window_blocks * BLOCK_S
            self._win.clear()
            return
        if self._duck.active:
            return
        vote = self.vote()
        self._pod_starts.append(ts)
        self._duck = _DuckState(
            active=True,
            pre_level=self._short_db,
            settle_until=ts + self.cfg.duck_settle_s,
            frozen=vote.confidence,
        )

    def observe(self, f: Features, ts: float) -> Vote:
        cfg = self.cfg
        self.now = ts + BLOCK_S
        self._observed_s += BLOCK_S
        self._win.append(f)
        mean_p = sum(_power(x.loudness_db) for x in self._win) / len(self._win)
        raw_short = 10.0 * math.log10(max(mean_p, 1e-12))
        self._short_crest = sum(x.crest_db for x in self._win) / len(self._win)

        d = self._duck
        if d.active and d.settle_until is not None and self.now >= d.settle_until:
            d.settle_until = None
            d.frozen = None
            assert d.pre_level is not None
            if raw_short <= cfg.silence_gate_db + cfg.buried_headroom_db:
                d.buried = True
            else:
                d.offset_db = max(0.0, min(cfg.max_duck_offset_db, d.pre_level - raw_short))
        self._short_db = raw_short + d.offset_db
        block_db = f.loudness_db + d.offset_db

        self._track_quiet(block_db, ts)
        self._track_baseline()
        return self.vote()

    # -- internals --------------------------------------------------------
    def _is_quiet(self, block_db: float) -> bool:
        if block_db <= self.cfg.silence_gate_db:
            return True
        return self.baseline_db is not None and block_db < self.baseline_db - self.cfg.quiet_below_baseline_db

    def _track_quiet(self, block_db: float, ts: float) -> None:
        if self._duck.active and self._duck.settle_until is not None:
            self._quiet_since = None
            return  # the duck itself is a level drop, not a separator
        if self._is_quiet(block_db):
            if self._quiet_since is None:
                self._quiet_since = ts
                self._separator(ts)
        else:
            self._quiet_since = None

    def _separator(self, ts: float) -> None:
        if self._separators and ts - self._separators[-1] < self.cfg.separator_merge_s:
            return
        self._separators.append(ts)
        seps = list(self._separators)
        fits = 0
        for a, b in zip(reversed(seps[:-1]), reversed(seps)):
            if unit_fit(b - a, self.cfg.grid_tolerance_s) is None:
                break
            fits += 1
        self._grid_fits = fits

    def _track_baseline(self) -> None:
        cfg = self.cfg
        if self._duck.active or self.now < self._skip_baseline_until:
            return
        if self._short_db <= cfg.silence_gate_db or len(self._win) < cfg.window_blocks:
            return
        if self.baseline_db is None:
            self.baseline_db = self._short_db
            self.baseline_crest = self._short_crest
            return
        if self._short_db - self.baseline_db > cfg.delta_db / 2:
            self._elevated_s += BLOCK_S
            if self._elevated_s < cfg.max_elevated_s:
                return
        else:
            self._elevated_s = 0.0
        alpha = min(1.0, BLOCK_S / cfg.baseline_tau_s)
        self.baseline_db += alpha * (self._short_db - self.baseline_db)
        assert self.baseline_crest is not None
        self.baseline_crest += alpha * (self._short_crest - self.baseline_crest)

    @property
    def warm(self) -> bool:
        return self.baseline_db is not None and self._observed_s >= self.cfg.warmup_s

    @property
    def duck_offset_db(self) -> float:
        return self._duck.offset_db

    @property
    def loudness_inert(self) -> bool:
        return self._duck.buried

    def vote(self) -> Vote:
        cfg = self.cfg
        ts = self.now
        if self._duck.frozen is not None:
            return Vote(ts, self._duck.frozen, reason="duck_settling")
        if not self.warm:
            return Vote(ts, 0.0, reason="warming")
        assert self.baseline_db is not None and self.baseline_crest is not None
        loud = crest = 0.0
        if not self._duck.buried and self._short_db > cfg.silence_gate_db:
            delta = self._short_db - self.baseline_db
            loud = max(0.0, min(1.0, delta / (cfg.delta_db * cfg.full_conf_factor)))
            drop = self.baseline_crest - self._short_crest
            crest = cfg.crest_share * max(0.0, min(1.0, drop / (cfg.crest_drop_db * cfg.full_conf_factor)))
        silence = 0.0
        if self._separators:
            age = ts - self._separators[-1]
            if age < cfg.silence_decay_s:
                silence = cfg.silence_boost * (1.0 - age / cfg.silence_decay_s)
        grid = 0.0
        if self._grid_fits and self._separators and ts - self._separators[-1] <= cfg.grid_hold_s:
            grid = cfg.grid_weight * min(1.0, 0.5 * self._grid_fits)
        clock = 0.0
        if cfg.clock_period_s and self._pod_starts:
            expected = self._pod_starts[-1] + cfg.clock_period_s
            if abs(ts - expected) <= cfg.clock_window_s:
                clock = cfg.clock_bias
        evidence = loud + crest
        # Boundary cues only add to live evidence; they never duck alone.
        support = (silence + grid + clock) if evidence > 0.15 else 0.0
        conf = max(0.0, min(1.0, evidence + support))
        if self._duck.buried:
            # Loudness is blind while the ducked set is buried in room noise:
            # hold only while the ad-unit grid is live, otherwise give the
            # volume back (a late restore is worse than a missed duck).
            conf = 0.7 if grid > 0 else 0.0
        return Vote(
            ts,
            conf,
            loudness=loud,
            crest=crest,
            silence=silence,
            grid=grid,
            clock=clock,
            reason=f"delta={self._short_db - self.baseline_db:.1f}dB crest_drop={self.baseline_crest - self._short_crest:.1f}dB",
        )
