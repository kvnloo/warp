"""TV control interface and the safe volume ducker.

Backends implement three calls::

    get_volume() -> int
    duck(level)            # remember the current volume, then set ``level``
    restore()              # set the remembered volume back

``FakeTV`` (tests / simulation) and ``DryRunTV`` (prints nothing, records
intended calls in memory) live here. The real Samsung backend is written
separately (branch feat/tv-voice) and only has to satisfy ``TVBackend``.

``Ducker`` owns the safety rules:

* duck to ``duck_level`` (default 3), never a full mute, and never *raise*
  the volume (if the set is already at or below the duck level, do nothing);
* remember the prior volume and restore exactly that;
* if the volume changes while ducked, the user did it: cancel, keep their
  volume, and back off for ``backoff_s``;
* hard ceiling ``max_duck_s`` independent of the detector;
* any backend error while ducking -> try to restore; restore failures are
  retried on every tick until they succeed.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import List, Optional, Tuple

from typing import Protocol


class TVBackend(Protocol):
    def get_volume(self) -> int: ...

    def duck(self, level: int) -> None: ...

    def restore(self) -> None: ...


class TVError(RuntimeError):
    pass


@dataclass
class FakeTV:
    volume: int = 20
    fail_get: bool = False
    fail_duck: bool = False
    fail_restore: int = 0  # number of restore calls that will fail
    calls: List[Tuple[str, int]] = field(default_factory=list)
    saved: Optional[int] = None

    def get_volume(self) -> int:
        if self.fail_get:
            raise TVError("get_volume failed")
        return self.volume

    def duck(self, level: int) -> None:
        self.calls.append(("duck", level))
        if self.fail_duck:
            raise TVError("duck failed")
        if self.saved is None:
            self.saved = self.volume
        self.volume = level

    def restore(self) -> None:
        self.calls.append(("restore", -1 if self.saved is None else self.saved))
        if self.fail_restore > 0:
            self.fail_restore -= 1
            raise TVError("restore failed")
        if self.saved is not None:
            self.volume = self.saved
            self.saved = None

    def user_sets(self, volume: int) -> None:
        """Simulate someone pressing the remote."""
        self.volume = volume


@dataclass
class DryRunTV:
    """Pretends to be a TV at ``volume``; records what it would have sent."""

    volume: int = 20
    intents: List[Tuple[str, int]] = field(default_factory=list)
    saved: Optional[int] = None

    def get_volume(self) -> int:
        return self.volume

    def duck(self, level: int) -> None:
        self.intents.append(("duck", level))
        if self.saved is None:
            self.saved = self.volume
        self.volume = level

    def restore(self) -> None:
        self.intents.append(("restore", -1 if self.saved is None else self.saved))
        if self.saved is not None:
            self.volume = self.saved
            self.saved = None


@dataclass
class DuckerConfig:
    duck_level: int = 3
    max_duck_s: float = 240.0
    backoff_s: float = 600.0


class Ducker:
    def __init__(self, tv: TVBackend, config: Optional[DuckerConfig] = None) -> None:
        self.tv = tv
        self.cfg = config or DuckerConfig()
        self.prior: Optional[int] = None  # volume to give back; None = not ducked
        self.level: int = self.cfg.duck_level  # volume we set while ducked
        self.ducked_at: Optional[float] = None
        self.backoff_until = 0.0
        self.pending_restore = False
        self.events: List[Tuple[float, str]] = []

    @property
    def ducked(self) -> bool:
        return self.prior is not None

    def duck(self, now: float) -> bool:
        """Duck the TV. Returns True if the TV is now ducked by us."""
        if self.ducked or self.pending_restore or now < self.backoff_until:
            return False
        try:
            current = int(self.tv.get_volume())
        except Exception:
            self.events.append((now, "error:get_volume"))
            return False
        if current <= self.cfg.duck_level:
            self.events.append((now, "skip:already_quiet"))
            return False
        self.prior = current
        self.ducked_at = now
        self.level = self.cfg.duck_level
        try:
            self.tv.duck(self.cfg.duck_level)
        except Exception:
            self.events.append((now, "error:duck"))
            self.restore(now)
            return False
        self.events.append((now, f"duck:{current}->{self.cfg.duck_level}"))
        return True

    def ease(self, now: float, steps: int) -> bool:
        """Duck less deeply (raise the ducked level by ``steps``), staying below
        the prior volume. Used when the ducked set is buried in room noise and
        the detector can no longer hear when the programme is back."""
        if self.prior is None or self.pending_restore:
            return False
        new_level = min(self.level + steps, self.prior - 1)
        if new_level <= self.level:
            return False
        try:
            self.tv.duck(new_level)
        except Exception:
            self.events.append((now, "error:duck"))
            self.restore(now)
            return False
        self.events.append((now, f"ease:{self.level}->{new_level}"))
        self.level = new_level
        return True

    def restore(self, now: float) -> bool:
        """Give the prior volume back. Returns True once it succeeded."""
        if self.prior is None:
            return True
        try:
            self.tv.restore()
        except Exception:
            self.pending_restore = True
            self.events.append((now, "error:restore"))
            return False
        self.events.append((now, f"restore:{self.prior}"))
        self.prior = None
        self.ducked_at = None
        self.pending_restore = False
        return True

    def tick(self, now: float) -> Optional[str]:
        """Safety checks; call every block. Returns a reason if it intervened."""
        if self.prior is None:
            return None
        if self.pending_restore:
            self.restore(now)
            return "retry_restore"
        assert self.ducked_at is not None
        if now - self.ducked_at >= self.cfg.max_duck_s:
            self.restore(now)
            return "ceiling"
        try:
            current = int(self.tv.get_volume())
        except Exception:
            self.events.append((now, "error:get_volume"))
            self.restore(now)
            return "error"
        if current != self.level:
            # Someone used the remote: their volume wins, and we stay away.
            self.events.append((now, f"manual:{current}"))
            self.prior = None
            self.ducked_at = None
            self.backoff_until = now + self.cfg.backoff_s
            return "manual"
        return None
