"""Tier 2: Jev picks one action from the fixed TV table when the rules are unsure.

Jev never sees anything but the spoken command and the action table, and it can
only answer with one of the table's ids. Every failure path - jevkit missing,
no key, timeout, low confidence, `reobserve`/`abstain`, unknown id, a command
that looks private - returns no action. It never guesses.
"""
from __future__ import annotations

import importlib
import os
import sys
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Optional

from .actions import JEV_ACTIONS, JEV_CANDIDATES, Action

DEFAULT_JEV_PLUGIN = Path.home() / ".hermes" / "plugins" / "hermes-jev"
FLOOR = 0.65  # never act below this, even if jevkit's own floor is configured lower
MAX_COMMAND_CHARS = 160


@dataclass(frozen=True)
class JevResult:
    action: Optional[Action]
    confidence: float
    reason: str
    latency_ms: float


def load_jevkit(path: Optional[str] = None) -> Optional[Any]:
    """Import jevkit from the hermes-jev plugin dir. None if it is not installed."""
    root = Path(path or os.environ.get("TV_VOICE_JEVKIT_DIR") or DEFAULT_JEV_PLUGIN).expanduser()
    if not (root / "jevkit" / "choose.py").is_file():
        return None
    if str(root) not in sys.path:
        sys.path.insert(0, str(root))
    try:
        jevkit = importlib.import_module("jevkit")
        importlib.import_module("jevkit.choose")
        importlib.import_module("jevkit.privacy")
        return jevkit
    except Exception:  # a broken plugin is the same as no plugin: fail closed
        return None


class JevTier:
    def __init__(self, jevkit: Any = None, *, timeout: float = 2.0, autoload: bool = True):
        self.jevkit = jevkit if jevkit is not None else (load_jevkit() if autoload else None)
        self.timeout = timeout

    def floor(self) -> float:
        kit_floor = getattr(getattr(self.jevkit, "choose", None), "MIN_CONFIDENCE", FLOOR)
        try:
            return max(FLOOR, float(kit_floor))
        except (TypeError, ValueError):
            return FLOOR

    def _private(self, text: str) -> bool:
        privacy = getattr(self.jevkit, "privacy", None)
        if privacy is None:
            return True  # cannot check, so do not send
        try:
            if privacy.is_sensitive(text):
                return True
            # redact() masks emails, phone and card numbers; if it changes anything the
            # command carried something personal and stays on this machine.
            return privacy.redact(text) != privacy.normalize(text)
        except Exception:
            return True

    def request(self, text: str) -> dict:
        return {
            "schema": "jev.action_choice_request_v1",
            "goal": f"A person said this to their TV's voice remote: \"{text}\". Pick the TV action they want.",
            "candidates": JEV_CANDIDATES,
        }

    def choose(self, text: str) -> JevResult:
        start = time.perf_counter()

        def done(action: Optional[Action], confidence: float, reason: str) -> JevResult:
            return JevResult(action, confidence, reason, round((time.perf_counter() - start) * 1000, 1))

        if self.jevkit is None or getattr(self.jevkit, "choose", None) is None:
            return done(None, 0.0, "jevkit not available")
        if not isinstance(text, str) or not text.strip() or len(text) > MAX_COMMAND_CHARS:
            return done(None, 0.0, "command empty or too long for Jev")
        if self._private(text):
            return done(None, 0.0, "command looks private; not sent to Jev")
        try:
            reply = self.jevkit.choose.choose(self.request(text), timeout=self.timeout)
        except Exception as error:
            return done(None, 0.0, f"Jev error ({type(error).__name__})")
        selected = reply.get("selected_id") if isinstance(reply, dict) else None
        try:
            confidence = float(reply.get("confidence", 0.0))
        except (AttributeError, TypeError, ValueError):
            confidence = 0.0
        if selected in (None, "reobserve", "abstain"):
            return done(None, confidence, f"Jev declined ({selected or 'no answer'}: {reply.get('reason', '') if isinstance(reply, dict) else ''})")
        if confidence < self.floor():
            return done(None, confidence, "below confidence floor")
        action = JEV_ACTIONS.get(selected)
        if action is None:
            return done(None, confidence, "Jev answered an id outside the table")
        return done(action, confidence, "chosen")
