"""Gate (b): can words be recovered from the feature stream? (must be: no)

    python tools/gate_words.py CLIP.wav --model DIR --reference "the text"

1. Positive control: transcribe the original clip with a local CPU speech
   recogniser (faster-whisper). It must recover most reference words, which
   proves the recogniser and clip work.
2. Attack: compute the Features stream exactly as the service does
   (RingBuffer -> extract_features, 2 records/s), then synthesise audio from
   the features alone - each of the 8 band energies, interpolated at 2 Hz,
   drives band-limited noise - and transcribe that with the same recogniser.
   PASS requires that no reference word comes back.

Audio stays in RAM: the reconstruction is handed to the recogniser as an
array and never written. The caller deletes the clip afterwards.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import List, Sequence

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from adduck.features import BAND_EDGES_HZ, BLOCK_S, Features  # noqa: E402
from adduck.ringbuffer import RingBuffer  # noqa: E402  (tool, not runtime: reads the test clip)

SR = 16000


def features_of(samples: np.ndarray, sr: int = SR) -> List[Features]:
    ring = RingBuffer(sr)
    return list(ring.push(samples))


def reconstruct(feats: Sequence[Features], sr: int = SR, seed: int = 0) -> np.ndarray:
    """Best-effort resynthesis from features only (a 'band vocoder' at 2 Hz)."""
    bands = np.array([f.bands_db for f in feats], dtype=np.float64)
    return vocode(bands, BLOCK_S, sr, seed)


def fine_bands_db(samples: np.ndarray, hop_s: float, sr: int = SR) -> np.ndarray:
    """Same 8 bands, but at a fine frame rate. Used ONLY as a positive control
    showing the vocoder attack works when envelopes are fast enough."""
    hop = int(hop_s * sr)
    frames = len(samples) // hop
    out = np.zeros((frames, len(BAND_EDGES_HZ) - 1))
    freqs = np.fft.rfftfreq(hop, 1.0 / sr)
    for i in range(frames):
        p = np.abs(np.fft.rfft(samples[i * hop : (i + 1) * hop] * np.hanning(hop))) ** 2
        for b, (lo, hi) in enumerate(zip(BAND_EDGES_HZ[:-1], BAND_EDGES_HZ[1:])):
            out[i, b] = 10 * np.log10(np.sum(p[(freqs >= lo) & (freqs < hi)]) + 1e-12)
    return out


def vocode(bands_db: np.ndarray, hop_s: float, sr: int = SR, seed: int = 0) -> np.ndarray:
    """Band envelopes (frames x 8, dB) at ``hop_s`` -> noise-excited audio."""
    rng = np.random.default_rng(seed)
    hop = int(hop_s * sr)
    n = hop * len(bands_db)
    t_samples = np.arange(n) / sr
    t_feats = (np.arange(len(bands_db)) + 0.5) * hop_s
    noise_spec = np.fft.rfft(rng.standard_normal(n))
    freqs = np.fft.rfftfreq(n, 1.0 / sr)
    out = np.zeros(n)
    for b, (lo, hi) in enumerate(zip(BAND_EDGES_HZ[:-1], BAND_EDGES_HZ[1:])):
        carrier = np.fft.irfft(np.where((freqs >= lo) & (freqs < hi), noise_spec, 0), n)
        carrier /= np.sqrt(np.mean(carrier**2)) + 1e-12
        amp = np.sqrt(10 ** (bands_db[:, b] / 10.0))
        out += carrier * np.interp(t_samples, t_feats, amp)
    peak = np.max(np.abs(out)) + 1e-12
    return (0.5 * out / peak).astype(np.float32)


def read_wav(path: str) -> np.ndarray:
    """Read the 16 kHz mono 16-bit test clip (this tool only; never the service)."""
    import wave

    with wave.open(path, "rb") as w:
        assert w.getframerate() == SR and w.getnchannels() == 1 and w.getsampwidth() == 2
        data = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2")
    return data.astype(np.float32) / 32768.0


def words(text: str) -> List[str]:
    return re.findall(r"[a-z']+", text.lower())


def main(argv: Sequence[str]) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("clip")
    ap.add_argument("--model", required=True, help="local faster-whisper model directory")
    ap.add_argument("--reference", required=True)
    ap.add_argument("--control-min", type=float, default=0.6)
    args = ap.parse_args(argv)

    from faster_whisper import WhisperModel

    model = WhisperModel(args.model, device="cpu", compute_type="int8")
    clip = read_wav(args.clip)

    def transcribe(x: np.ndarray) -> str:
        segs, _ = model.transcribe(
            x, language="en", beam_size=5, vad_filter=False, condition_on_previous_text=False
        )
        return " ".join(s.text.strip() for s in segs).strip()

    feats = features_of(clip)
    recon = reconstruct(feats)
    control = transcribe(clip)
    attack = transcribe(recon)
    # Second control: the same vocoder at 50 frames/s (25x our rate).
    fast = transcribe(vocode(fine_bands_db(clip, 0.02), 0.02))
    ref = set(words(args.reference))
    control_hits = sorted(ref & set(words(control)))
    attack_hits = sorted(ref & set(words(attack)))
    report = {
        "model": args.model,
        "clip_seconds": round(len(clip) / SR, 2),
        "feature_records": len(feats),
        "feature_rate_hz": 1.0 / BLOCK_S,
        "reference": args.reference,
        "control_transcript": control,
        "control_reference_words_recovered": f"{len(control_hits)}/{len(ref)}",
        "reconstruction_transcript": attack,
        "reconstruction_reference_words_recovered": f"{len(attack_hits)}/{len(ref)}",
        "reconstruction_word_list": attack_hits,
        "reconstruction_any_words": bool(words(attack)),
        "vocoder_50hz_control_transcript": fast,
        "vocoder_50hz_reference_words_recovered": f"{len(ref & set(words(fast)))}/{len(ref)}",
    }
    control_ok = len(control_hits) >= args.control_min * len(ref)
    attack_ok = not attack_hits
    report["control_pass"] = control_ok
    report["attack_pass"] = attack_ok
    report["gate"] = "PASS" if control_ok and attack_ok else ("INVALID (control failed)" if not control_ok else "FAIL")
    print(json.dumps(report, indent=2))
    return 0 if report["gate"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
