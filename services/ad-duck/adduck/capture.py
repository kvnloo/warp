"""Microphone -> Features. Raw samples live only inside this module's RingBuffer.

``mic_features`` opens the default input device through the optional
``sounddevice`` package, pushes each callback chunk into a RingBuffer and
yields only ``Features``. Samples are never returned, stored, logged, or sent
anywhere; on exit the ring buffer is zeroed.

``sounddevice`` is not a dependency of the core or the tests; on the Lenovo
tablet the capture layer is the Android port (see README).
"""

from __future__ import annotations

import queue
from typing import Iterator

from .features import Features
from .ringbuffer import RingBuffer

SAMPLE_RATE = 16000


def mic_features(sample_rate: int = SAMPLE_RATE, device: object = None) -> Iterator[Features]:
    import sounddevice as sd  # optional; imported only when a real mic is used

    ring = RingBuffer(sample_rate)
    out: "queue.Queue[Features]" = queue.Queue(maxsize=64)

    def _callback(indata, frames, time_info, status) -> None:  # runs on the audio thread
        for feat in ring.push(indata[:, 0]):
            try:
                out.put_nowait(feat)
            except queue.Full:
                pass  # drop features, never buffer audio

    stream = sd.InputStream(
        samplerate=sample_rate, channels=1, dtype="float32", device=device, callback=_callback
    )
    try:
        with stream:
            while True:
                yield out.get()
    finally:
        ring.wipe()
