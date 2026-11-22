"""The ONE place raw audio is touched.

``extract_features`` takes one 0.5 s block of microphone samples and returns a
``Features`` record of a dozen coarse numbers. Nothing else in this package is
allowed to read samples (``tools/gate_static.py`` enforces that), and nothing
in this module writes anywhere: no files, no sockets, no logs.

What survives a block:

* ``loudness_db`` - K-weighted short-term level of the block (LUFS-like).
* ``crest_db``    - peak-to-RMS ratio. Commercials are heavily compressed,
                    so this drops during ads even when loudness does not.
* ``silent``      - block below the silence gate (a pod-boundary cue).
* ``bands_db``    - energy in 8 log-spaced bands, 100 Hz to 8 kHz.

Every value is quantised (1 dB / 0.5 dB steps). Two such records per second
is far below the ~4-5 syllables/s of speech, and 8 octave-ish bands are too
coarse to tell vowels apart, so the record cannot carry words. The
``gate_words`` tool checks that claim against a real speech recogniser.

The K-weighting approximation is adapted from AdHush (MIT, (c) 2026 Paul D.
Pruitt), ``src/adhush/detect/loudness.py``; see NOTICE.md.
"""

from __future__ import annotations

import math
from dataclasses import dataclass
from typing import Tuple

import numpy as np

BLOCK_S = 0.5
N_BANDS = 8
BAND_EDGES_HZ = tuple(float(x) for x in np.geomspace(100.0, 8000.0, N_BANDS + 1))
SILENCE_GATE_DB = -50.0
FLOOR_DB = -90.0
LOUDNESS_STEP_DB = 0.5
BAND_STEP_DB = 1.0
CREST_STEP_DB = 0.5

_HP_HZ = 38.0  # RLB high-pass corner (adapted from AdHush)
_SHELF_HZ = 1500.0
_SHELF_GAIN = 1.505  # +4 dB high shelf, power terms


@dataclass(frozen=True)
class Features:
    """Everything the detector is ever allowed to know about 0.5 s of sound."""

    loudness_db: float
    crest_db: float
    silent: bool
    bands_db: Tuple[float, ...]


def _quantise(value: float, step: float) -> float:
    return float(round(value / step) * step)


def _db(power: float) -> float:
    return FLOOR_DB if power <= 1e-12 else max(FLOOR_DB, 10.0 * math.log10(power))


def extract_features(samples: np.ndarray, sample_rate: int) -> Features:
    """Reduce one block of raw samples to a quantised ``Features`` record.

    ``samples`` is mono float in [-1, 1]. The array is only read; the caller
    owns (and overwrites) the ring buffer it came from.
    """
    x = np.asarray(samples, dtype=np.float64).ravel()
    n = x.size
    if n == 0:
        return Features(FLOOR_DB, 0.0, True, tuple([FLOOR_DB] * N_BANDS))
    x = x - float(np.mean(x))  # drop DC from the mic
    power_spec = np.abs(np.fft.rfft(x)) ** 2
    freqs = np.fft.rfftfreq(n, d=1.0 / sample_rate)
    scale = np.full(power_spec.shape, 2.0)
    scale[0] = 1.0
    if n % 2 == 0:
        scale[-1] = 1.0
    per_bin = power_spec * scale / (n * n)  # Parseval: sums to mean(x^2)

    f2 = np.square(freqs)
    k_weight = (np.square(f2) / (np.square(f2) + _HP_HZ**4)) * (1.0 + _SHELF_GAIN * f2 / (f2 + _SHELF_HZ**2))
    loudness = -0.691 + _db(float(np.sum(per_bin * k_weight)))

    rms = math.sqrt(float(np.mean(np.square(x))))
    crest = 0.0 if rms <= 1e-9 else 20.0 * math.log10(float(np.max(np.abs(x))) / rms)

    bands = []
    for lo, hi in zip(BAND_EDGES_HZ[:-1], BAND_EDGES_HZ[1:]):
        mask = (freqs >= lo) & (freqs < hi)
        bands.append(_quantise(_db(float(np.sum(per_bin[mask]))), BAND_STEP_DB))

    loud_q = _quantise(loudness, LOUDNESS_STEP_DB)
    return Features(
        loudness_db=loud_q,
        crest_db=_quantise(crest, CREST_STEP_DB),
        silent=loud_q <= SILENCE_GATE_DB,
        bands_db=tuple(bands),
    )
