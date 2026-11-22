"""Hysteresis state machine: PROGRAM -> SUSPECT -> AD -> RECOVERY -> PROGRAM.

Asymmetric on purpose (after AdHush ``state.py``, MIT): ducking needs the
vote to stay high for ``duck_dwell_s``; restoring needs it low for only
``restore_dwell_s``; and ``max_duck_s`` is a hard ceiling, because a stuck
duck is a defect while a missed one is only an annoyance.
"""

from __future__ import annotations

from dataclasses import dataclass
from enum import Enum
from typing import Optional


class AdState(Enum):
    PROGRAM = "program"
    SUSPECT = "suspect"
    AD = "ad"
    RECOVERY = "recovery"


class Action(Enum):
    DUCK = "duck"
    RESTORE = "restore"


@dataclass
class StateConfig:
    duck_confidence: float = 0.6
    restore_confidence: float = 0.4
    duck_dwell_s: float = 2.0
    restore_dwell_s: float = 1.5
    max_duck_s: float = 240.0
    recovery_s: float = 3.0


class AdStateMachine:
    def __init__(self, config: Optional[StateConfig] = None) -> None:
        self.cfg = config or StateConfig()
        self.state = AdState.PROGRAM
        self._since: Optional[float] = None  # start of the current dwell
        self._entered_ad: Optional[float] = None
        self._recovery_until = 0.0

    def update(self, confidence: float, now: float) -> Optional[Action]:
        cfg = self.cfg
        if self.state is AdState.RECOVERY:
            if now >= self._recovery_until:
                self.state = AdState.PROGRAM
            return None
        if self.state is AdState.PROGRAM:
            if confidence >= cfg.duck_confidence:
                self.state = AdState.SUSPECT
                self._since = now
            return None
        if self.state is AdState.SUSPECT:
            if confidence < cfg.duck_confidence:
                self.state = AdState.PROGRAM
                self._since = None
                return None
            assert self._since is not None
            if now - self._since >= cfg.duck_dwell_s:
                self.state = AdState.AD
                self._entered_ad = now
                self._since = None
                return Action.DUCK
            return None
        # AD
        assert self._entered_ad is not None
        if now - self._entered_ad >= cfg.max_duck_s:
            return self.leave(now)
        if confidence < cfg.restore_confidence:
            if self._since is None:
                self._since = now
            if now - self._since >= cfg.restore_dwell_s:
                return self.leave(now)
        else:
            self._since = None
        return None

    def leave(self, now: float) -> Action:
        """Leave AD now (ceiling, user override, or error)."""
        self.state = AdState.RECOVERY
        self._recovery_until = now + self.cfg.recovery_s
        self._entered_ad = None
        self._since = None
        return Action.RESTORE

    def reset(self) -> None:
        self.state = AdState.PROGRAM
        self._since = None
        self._entered_ad = None

    @property
    def ducked(self) -> bool:
        return self.state is AdState.AD
