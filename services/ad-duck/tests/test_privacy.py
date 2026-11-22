"""Privacy gates that run in CI without a speech recogniser."""

from __future__ import annotations

import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))

import gate_static  # noqa: E402
from adduck.features import BLOCK_S  # noqa: E402
from adduck.ringbuffer import RingBuffer  # noqa: E402

SR = 16000


def test_static_gate_passes_on_package():
    assert gate_static.check_package(ROOT / "adduck") == []


def test_static_gate_catches_file_write():
    src = "def f(x) -> None:\n    open('a.wav', 'wb').write(x)\n"
    problems = gate_static.check_source("features.py", src)
    assert any("open()" in p for p in problems) and any(".write()" in p for p in problems)


def test_static_gate_catches_network_and_serialisers():
    for mod in ("socket", "urllib.request", "requests", "pickle", "wave", "soundfile", "http.client"):
        assert gate_static.check_source("detect.py", f"import {mod}\n"), mod


def test_static_gate_catches_np_save_and_logging():
    src = "import numpy as np\ndef f(x) -> None:\n    np.save('x.npy', x)\n    log.info(x)\n"
    problems = gate_static.check_source("ringbuffer.py", src)
    assert any(".save()" in p for p in problems) and any(".info()" in p for p in problems)


def test_static_gate_catches_raw_array_return():
    src = "import numpy as np\ndef leak(x) -> np.ndarray:\n    return x\n"
    assert any("returns" in p for p in gate_static.check_source("capture.py", src))


def test_static_gate_catches_samples_outside_audio_path():
    src = "from .features import extract_features\n"
    assert gate_static.check_source("engine.py", src)
    assert gate_static.check_source("engine.py", "from .ringbuffer import RingBuffer\n")
    assert gate_static.check_source("detect.py", "import numpy as np\ny = np.fft.rfft([1])\n")


def test_feature_rate_cannot_follow_syllables():
    """A 4 Hz syllable-rate amplitude modulation is invisible at 2 records/s.

    Sampled at 2 Hz, a 4 Hz envelope aliases to DC: every block holds exactly
    two whole cycles, so per-block energy is flat whatever the phase. The
    feature stream carries no trace of the modulation.
    """
    rng = np.random.default_rng(1)
    t = np.arange(SR * 10) / SR
    flat = rng.standard_normal(t.size) * 0.05
    modulated = flat * (1 + 0.95 * np.sin(2 * np.pi * 4 * t + 0.7))
    feats = list(RingBuffer(SR).push(modulated))
    loud = np.array([f.loudness_db for f in feats])
    assert len(feats) == int(10 / BLOCK_S)
    assert loud.std() < 0.5  # dB; the modulation itself is ~ +-25 dB deep
