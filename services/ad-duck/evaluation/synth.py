"""Synthetic TV + room simulator for evaluation. Generates audio in RAM only.

No recordings are used or produced. A timeline of programme and ad segments
is rendered block by block at the *current* TV volume (so ducking changes what
the simulated microphone hears, as in a real room), mixed with room noise,
and pushed through the same RingBuffer -> extract_features path the live
service uses.

Programme: speech-like modulated noise (high crest), music beds, and loud
action scenes (false-positive traps). Ads: compressed (low crest) speech +
music, a scenario-dependent number of dB above the programme, in 15/30 s
spots separated by short silences.
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field
from typing import Iterator, List, Optional, Tuple

import numpy as np

SR = 16000
BLOCK = 8000


@dataclass
class Segment:
    start: float
    end: float
    kind: str  # speech | music | action | ad | silence
    level_db: float  # target RMS dBFS at reference volume
    drive: float = 1.0  # ad limiter drive (higher = more compressed)


@dataclass
class Scenario:
    name: str
    ad_delta_db: Tuple[float, float]  # ad level above programme, range
    programme_db: float = -28.0
    pods: int = 4
    spots: Tuple[int, int] = (3, 6)
    gap_min_s: Tuple[float, float] = (240.0, 420.0)
    room_noise_db: float = -62.0
    action_scenes: int = 3
    music_beds: int = 3
    ad_drive: Tuple[float, float] = (0.3, 1.0)


SCENARIOS = [
    Scenario("loud_ads", (5.0, 8.0)),
    Scenario("moderate_ads", (2.5, 4.5)),
    Scenario("drc_flat_ads", (0.0, 1.5)),  # TV night mode: only crest/gaps differ
    Scenario("noisy_room", (3.0, 6.0), room_noise_db=-48.0),
]


@dataclass
class Timeline:
    segments: List[Segment]
    pods: List[Tuple[float, float]]  # ground-truth ad pods (start, end)
    duration: float


def build_timeline(sc: Scenario, rng: np.random.Generator) -> Timeline:
    segs: List[Segment] = []
    pods: List[Tuple[float, float]] = []
    t = 0.0

    def programme(dur: float) -> None:
        nonlocal t
        end = t + dur
        # Scene-to-scene level drift plus traps placed inside the programme.
        cur = t
        traps = sorted(rng.uniform(t + 20, end - 40, size=sc.action_scenes + sc.music_beds))
        kinds = ["action"] * sc.action_scenes + ["music"] * sc.music_beds
        rng.shuffle(kinds)
        for when, kind in zip(traps, kinds):
            if when <= cur + 5:
                continue
            segs.append(Segment(cur, when, "speech", sc.programme_db + rng.uniform(-2, 2)))
            if rng.random() < 0.6:  # scene cut with a short silence: a trap for the gap cue
                cut = rng.uniform(0.4, 0.9)
                segs.append(Segment(when, when + cut, "silence", -120))
                when += cut
            length = rng.uniform(6, 20)
            lvl = sc.programme_db + (rng.uniform(3, 5) if kind == "action" else rng.uniform(-3, 1))
            segs.append(Segment(when, when + length, kind, lvl))
            cur = when + length
        segs.append(Segment(cur, end, "speech", sc.programme_db + rng.uniform(-2, 2)))
        t = end

    programme(rng.uniform(90, 150))
    for _ in range(sc.pods):
        gap = rng.uniform(0.4, 0.9)
        segs.append(Segment(t, t + gap, "silence", -120))
        t += gap
        pod_start = t
        n = int(rng.integers(sc.spots[0], sc.spots[1] + 1))
        for i in range(n):
            dur = float(rng.choice([15.0, 15.0, 30.0, 30.0, 30.0]))
            lvl = sc.programme_db + rng.uniform(*sc.ad_delta_db)
            segs.append(Segment(t, t + dur - 0.5, "ad", lvl, drive=float(rng.uniform(*sc.ad_drive))))
            segs.append(Segment(t + dur - 0.5, t + dur, "silence", -120))
            t += dur
        pods.append((pod_start, t))
        programme(rng.uniform(*sc.gap_min_s))
    return Timeline(segs, pods, t)


def _pinkish(rng: np.random.Generator, n: int) -> np.ndarray:
    spec = np.fft.rfft(rng.standard_normal(n))
    f = np.fft.rfftfreq(n, 1.0 / SR)
    spec /= np.sqrt(np.maximum(f, 50.0))
    spec[f > 7000] *= 0.2
    x = np.fft.irfft(spec, n)
    return x / (np.std(x) + 1e-12)


def _render(kind: str, t0: float, n: int, rng: np.random.Generator, drive: float = 1.0) -> np.ndarray:
    t = t0 + np.arange(n) / SR
    if kind == "silence":
        return np.zeros(n)
    if kind in ("speech", "action"):
        # Syllable-rate envelope with pauses: bursty, high crest.
        env = np.zeros(n)
        for hz in (3.1, 4.3, 5.7):
            env += np.sin(2 * math.pi * hz * t + rng.uniform(0, 2 * math.pi))
        env = np.clip(env, 0, None) ** 2
        env *= (np.sin(2 * math.pi * 0.23 * t + t0) > -0.6)  # phrase pauses
        x = _pinkish(rng, n) * (0.1 + env)
        if kind == "action":
            spikes = rng.random(n) < 0.0004
            x += np.convolve(spikes * rng.standard_normal(n) * 25, np.exp(-np.arange(200) / 30.0), "same")
        return x
    if kind == "music":
        base = 110.0 * (1 + int(t0 // 4) % 3)
        x = sum(np.sin(2 * math.pi * base * k * t) / k for k in (1, 1.5, 2, 3))
        return x + 0.3 * _pinkish(rng, n) * (0.5 + 0.5 * np.sin(2 * math.pi * 2 * t) ** 2)
    if kind == "ad":
        # Voice-over on a music bed, then a hard broadcast-style limiter.
        speech = _render("speech", t0, n, rng)
        bed = _render("music", t0 + 7, n, rng)
        x = speech / (np.std(speech) + 1e-9) + 0.6 * bed / (np.std(bed) + 1e-9)
        k = 1.0 + 6.0 * drive
        return np.tanh(k * x / (np.max(np.abs(x)) + 1e-9)) / np.tanh(k)
    raise ValueError(kind)


def volume_gain_db(volume: int, reference: int) -> float:
    """Rough Samsung-style curve: 20 -> 3 is about -25 dB."""
    return 30.0 * math.log10(max(volume, 1) / max(reference, 1))


@dataclass
class Room:
    timeline: Timeline
    scenario: Scenario
    reference_volume: int
    rng: np.random.Generator
    _starts: np.ndarray = field(init=False)

    def __post_init__(self) -> None:
        self._starts = np.array([s.start for s in self.timeline.segments])

    def block(self, t0: float, volume: int) -> np.ndarray:
        """0.5 s of what the microphone hears at TV ``volume``."""
        times = t0 + np.arange(BLOCK) / SR
        idx = np.clip(np.searchsorted(self._starts, times, side="right") - 1, 0, None)
        out = np.zeros(BLOCK)
        for i in np.unique(idx):
            seg = self.timeline.segments[int(i)]
            mask = idx == i
            if seg.kind == "silence" or times[mask][0] >= self.timeline.duration:
                continue
            x = _render(seg.kind, t0, BLOCK, self.rng, seg.drive)
            x = x / (np.sqrt(np.mean(x**2)) + 1e-12) * 10 ** (seg.level_db / 20.0)
            out[mask] = x[mask]
        out *= 10 ** (volume_gain_db(volume, self.reference_volume) / 20.0)
        out += self.rng.standard_normal(BLOCK) * 10 ** (self.scenario.room_noise_db / 20.0)
        return np.clip(out, -1.0, 1.0).astype(np.float32)
