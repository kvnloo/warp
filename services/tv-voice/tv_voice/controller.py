"""Text command -> TV action. The only entry point the CLI, HTTP server and a
future ASR front end use. It never talks to Hermes or any agent session.

Privacy: the log line carries tier, latency and the action id only. The words
themselves stay in an in-memory ring (last 20) unless debug is on.
"""
from __future__ import annotations

import json
import logging
import os
import sys
import time
from collections import deque
from dataclasses import asdict, dataclass
from typing import Any, Deque, Optional

from . import intent
from .actions import Action
from .jev_tier import JevTier

TIER1_ACCEPT = 0.85  # a fuzzy rule hit below this goes to Jev instead
log = logging.getLogger("tv_voice")


@dataclass
class Result:
    ok: bool
    tier: str          # rules | jev | none
    action: str        # action id, or "none"
    detail: str        # human-readable: what was sent, or why nothing was
    confidence: float
    latency_ms: float
    dry_run: bool

    def to_dict(self) -> dict:
        return asdict(self)


def _allow_unverified() -> bool:
    return os.environ.get("TV_VOICE_ALLOW_UNVERIFIED_APPS") == "1"


class Controller:
    def __init__(self, remote: Any, jev: Optional[JevTier] = None, *, dry_run: bool = False,
                 debug: bool = False, use_jev: bool = True):
        self.remote = remote
        self.jev = jev if jev is not None else (JevTier() if use_jev else None)
        self.dry_run = dry_run
        self.debug = debug
        self.recent: Deque[dict] = deque(maxlen=20)

    def _resolve(self, text: str):
        match = intent.parse(text)
        if match and match.confidence >= TIER1_ACCEPT:
            return "rules", match.action, match.confidence, ""
        if self.jev is None:
            return "none", None, match.confidence if match else 0.0, "rules unsure and Jev disabled"
        picked = self.jev.choose(text)
        if picked.action is None:
            return "none", None, picked.confidence, picked.reason
        return "jev", picked.action, picked.confidence, ""

    def handle(self, text: str) -> Result:
        started = time.perf_counter()
        tier, action, confidence, why = self._resolve(text)
        ok, detail = False, why
        if action is not None:
            ok, detail = self._execute(action)
        result = Result(ok, tier, action.id if action else "none", detail, round(confidence, 3),
                        round((time.perf_counter() - started) * 1000, 1), self.dry_run)
        self.recent.append({"text": text, **result.to_dict()})
        line = {"tier": result.tier, "action": result.action, "ok": result.ok,
                "latency_ms": result.latency_ms, "dry_run": result.dry_run}
        if self.debug:
            line["text"] = text
            line["detail"] = result.detail
        log.info(json.dumps(line))
        return result

    def _execute(self, action: Action):
        if action.kind == "none":
            return False, "nothing to do"
        if action.kind == "app" and action.app and not action.app.verified and not _allow_unverified():
            return False, (f"{action.app.name} app id {action.app.app_id} is unverified on this TV; "
                           "set TV_VOICE_ALLOW_UNVERIFIED_APPS=1 to try it")
        try:
            self.remote.execute(action)
        except Exception as error:
            return False, f"send failed: {error}"
        return True, ("would send " if self.dry_run else "sent ") + action.summary()


def setup_logging(debug: bool = False) -> None:
    handler = logging.StreamHandler(sys.stderr)
    handler.setFormatter(logging.Formatter("%(asctime)s tv-voice %(message)s"))
    log.handlers[:] = [handler]
    log.setLevel(logging.INFO)
    log.propagate = False
