"""Optional tiny model: logistic regression in numpy over feature records.

It never sees audio - its inputs are the detector's cue votes plus the
Features vector relative to the programme baseline. Weights are plain lists
(``to_dict``/``from_dict``); this module does no file I/O.
"""

from __future__ import annotations

from typing import Dict, List, Optional, Sequence

import numpy as np

from .detect import AdDetector, Vote
from .features import Features


def context_vector(vote: Vote, f: Features, baseline_db: Optional[float]) -> np.ndarray:
    base = baseline_db if baseline_db is not None else f.loudness_db
    bands = np.array(f.bands_db, dtype=np.float64)
    shape = bands - float(np.max(bands))  # spectral shape, level-free
    return np.concatenate(
        [
            [vote.loudness, vote.crest, vote.silence, vote.grid, vote.clock],
            [(f.loudness_db - base) / 10.0, f.crest_db / 10.0, 1.0 if f.silent else 0.0],
            shape / 30.0,
        ]
    )


class LogisticModel:
    def __init__(self, n_inputs: int) -> None:
        self.w = np.zeros(n_inputs)
        self.b = 0.0

    def predict(self, x: np.ndarray) -> np.ndarray:
        z = np.clip(np.asarray(x) @ self.w + self.b, -30, 30)
        return 1.0 / (1.0 + np.exp(-z))

    def fit(self, x: np.ndarray, y: np.ndarray, epochs: int = 300, lr: float = 0.5, l2: float = 1e-3) -> "LogisticModel":
        x = np.asarray(x, dtype=np.float64)
        y = np.asarray(y, dtype=np.float64)
        for _ in range(epochs):
            p = self.predict(x)
            g = p - y
            self.w -= lr * (x.T @ g / len(y) + l2 * self.w)
            self.b -= lr * float(np.mean(g))
        return self

    def to_dict(self) -> Dict[str, List[float]]:
        return {"w": [float(v) for v in self.w], "b": [self.b]}

    @classmethod
    def from_dict(cls, d: Dict[str, Sequence[float]]) -> "LogisticModel":
        m = cls(len(d["w"]))
        m.w = np.array(d["w"], dtype=np.float64)
        m.b = float(d["b"][0])
        return m


def blended_scorer(model: LogisticModel, detector: AdDetector, weight: float = 0.5):
    """Engine scorer: blend rule confidence with the model's probability."""

    def score(vote: Vote, f: Features) -> float:
        if vote.reason in ("warming", "duck_settling"):
            return vote.confidence
        p = float(model.predict(context_vector(vote, f, detector.baseline_db)))
        return (1.0 - weight) * vote.confidence + weight * p

    return score
