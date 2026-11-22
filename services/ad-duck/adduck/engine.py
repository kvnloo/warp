"""Wire features -> detector -> state machine -> ducker.

The engine only ever sees ``Features`` records; it has no access to samples.
Any exception in detection or control ends in a restore attempt.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Callable, Iterable, List, Optional, Tuple

from .detect import AdDetector, Vote
from .features import BLOCK_S, Features
from .state import Action, AdStateMachine
from .tv import Ducker

Scorer = Callable[[Vote, Features], float]


@dataclass
class Step:
    ts: float
    confidence: float
    state: str
    action: Optional[str]


class Engine:
    def __init__(
        self,
        detector: AdDetector,
        machine: AdStateMachine,
        ducker: Ducker,
        scorer: Optional[Scorer] = None,
    ) -> None:
        self.detector = detector
        self.machine = machine
        self.ducker = ducker
        self.scorer = scorer  # optional NN blend over the same features
        self.actions: List[Tuple[float, str]] = []

    def step(self, f: Features, ts: float) -> Step:
        now = ts + BLOCK_S
        action: Optional[str] = None
        try:
            vote = self.detector.observe(f, ts)
            conf = vote.confidence if self.scorer is None else self.scorer(vote, f)
            intervened = self.ducker.tick(now)
            if intervened in ("manual", "ceiling", "error") and self.machine.ducked:
                self.machine.leave(now)
                self.detector.on_duck(now, False)
                action = f"restore:{intervened}" if intervened != "manual" else "cancel:manual"
            decision = self.machine.update(conf, now)
            if decision is Action.DUCK:
                if self.ducker.duck(now):
                    self.detector.on_duck(now, True)
                    action = "duck"
                else:
                    self.machine.leave(now)  # could not / should not duck
            elif decision is Action.RESTORE:
                self.ducker.restore(now)
                self.detector.on_duck(now, False)
                action = "restore"
        except Exception:
            self.fail_safe(now)
            action = "restore:error"
            conf = 0.0
        if action:
            self.actions.append((now, action))
        return Step(now, conf, self.machine.state.value, action)

    def fail_safe(self, now: float) -> None:
        try:
            self.ducker.restore(now)
        finally:
            self.machine.reset()

    def run(self, features: Iterable[Features], start_ts: float = 0.0) -> List[Step]:
        steps = []
        ts = start_ts
        try:
            for f in features:
                steps.append(self.step(f, ts))
                ts += BLOCK_S
        finally:
            self.fail_safe(ts)  # stopping always gives the volume back
        return steps
