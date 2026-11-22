"""Fixed-size in-RAM ring buffer for raw samples.

The buffer is one preallocated numpy array that is overwritten in place. It
never grows, and its only output is ``Features`` - each full 0.5 s block is
handed straight to ``extract_features`` and the samples stay behind to be
overwritten by the next block. ``wipe()`` zeroes it. There is no save, dump,
pickle or repr of its contents.
"""

from __future__ import annotations

from typing import Iterator

import numpy as np

from .features import BLOCK_S, Features, extract_features


class RingBuffer:
    def __init__(self, sample_rate: int, block_s: float = BLOCK_S) -> None:
        self.sample_rate = int(sample_rate)
        self.block_len = int(round(block_s * sample_rate))
        self._buf = np.zeros(self.block_len, dtype=np.float32)
        self._fill = 0

    def __repr__(self) -> str:  # never show samples
        return f"RingBuffer(rate={self.sample_rate}, capacity={self._buf.size})"

    def push(self, chunk: np.ndarray) -> Iterator[Features]:
        """Copy ``chunk`` into the buffer; yield one Features per full block."""
        data = np.asarray(chunk, dtype=np.float32).ravel()
        pos = 0
        while pos < data.size:
            take = min(self.block_len - self._fill, data.size - pos)
            self._buf[self._fill : self._fill + take] = data[pos : pos + take]
            self._fill += take
            pos += take
            if self._fill == self.block_len:
                self._fill = 0  # next chunk overwrites this block in place
                yield extract_features(self._buf, self.sample_rate)

    def wipe(self) -> None:
        self._buf.fill(0.0)
        self._fill = 0
