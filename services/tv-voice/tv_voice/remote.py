"""Samsung network remote: samsungtvws (LGPL-3.0, used unmodified as a dependency).

`TvRemote` keeps one WebSocket open to the TV, saves the pairing token 0600
under ~/.config/tv-voice/, and reconnects once if the TV dropped the socket
(it does after standby or a few idle minutes). `DryRunRemote` records what
would have been sent and never touches the network.
"""
from __future__ import annotations

import os
import select
import threading
import time
from pathlib import Path
from typing import List, Optional, Tuple

from .actions import Action

DEFAULT_HOST = "192.168.1.28"
DEFAULT_PORT = 8002
CLIENT_NAME = "tv-voice"
KEY_DELAY = 0.12  # seconds between repeated clicks; samsungtvws defaults to a full second


def config_dir() -> Path:
    base = os.environ.get("XDG_CONFIG_HOME") or str(Path.home() / ".config")
    return Path(base) / "tv-voice"


def token_path() -> Path:
    return Path(os.environ.get("TV_VOICE_TOKEN_FILE") or config_dir() / "token")


def write_private(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    os.chmod(path.parent, 0o700)
    temp = path.with_name(path.name + ".tmp")
    fd = os.open(temp, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(fd, "w") as handle:
        handle.write(text)
    os.chmod(temp, 0o600)
    os.replace(temp, path)


class RemoteError(RuntimeError):
    pass


def _make_tv(host: str, port: int, token_file: Path, name: str, timeout: float):
    from samsungtvws import SamsungTVWS

    class _PrivateTokenTV(SamsungTVWS):
        def _set_token(self, token: str) -> None:  # library writes with the default umask
            write_private(Path(self.token_file), token)

    return _PrivateTokenTV(host=host, port=port, token_file=str(token_file), name=name,
                           timeout=timeout, key_press_delay=KEY_DELAY)


class TvRemote:
    def __init__(self, host: str = DEFAULT_HOST, port: int = DEFAULT_PORT, *, name: str = CLIENT_NAME,
                 token_file: Optional[Path] = None, timeout: float = 5.0):
        self.host, self.port, self.name, self.timeout = host, port, name, timeout
        self.token_file = Path(token_file) if token_file else token_path()
        self.token_file.parent.mkdir(parents=True, exist_ok=True)
        os.chmod(self.token_file.parent, 0o700)
        self._tv = None
        self._lock = threading.Lock()

    # -- connection -------------------------------------------------------------
    def _connected(self) -> bool:
        """True only if the socket is open and the TV has not closed it under us."""
        conn = getattr(self._tv, "connection", None) if self._tv else None
        if conn is None or not conn.connected:
            return False
        try:
            while select.select([conn.sock], [], [], 0)[0]:
                data = conn.recv()  # drains TV events; a close frame or EOF ends the loop
                if not data or not conn.connected:
                    return False
        except Exception:
            return False
        return True

    def _drop(self) -> None:
        if self._tv is not None:
            try:
                self._tv.close()
            except Exception:
                pass
        self._tv = None

    def _ensure(self):
        if not self._connected():
            self._drop()
            self._tv = _make_tv(self.host, self.port, self.token_file, self.name, self.timeout)
            self._tv.open()
        return self._tv

    def _with_retry(self, fn) -> None:
        with self._lock:
            last: Optional[BaseException] = None
            for _attempt in range(2):
                try:
                    fn(self._ensure())
                    return
                except Exception as error:  # broken pipe, closed socket, refused: reconnect once
                    last = error
                    self._drop()
            raise RemoteError(f"TV remote unavailable: {type(last).__name__}: {last}")

    def close(self) -> None:
        with self._lock:
            self._drop()

    # -- actions ------------------------------------------------------------------
    def send_keys(self, keys: Tuple[str, ...]) -> None:
        remaining = list(keys)

        def run(tv) -> None:
            while remaining:
                tv.send_key(remaining[0], key_press_delay=KEY_DELAY if len(remaining) > 1 else 0)
                remaining.pop(0)  # a reconnect resumes where it stopped, never re-sends

        self._with_retry(run)

    def launch(self, app_id: str) -> None:
        # ed.apps.launch over the same WebSocket (samsungtvws.run_app), not the REST POST.
        self._with_retry(lambda tv: tv.run_app(app_id))

    def type_text(self, text: str) -> None:
        # Only lands if a text field on the TV is focused (the IME is up).
        self._with_retry(lambda tv: tv.send_text(text))

    def execute(self, action: Action) -> None:
        if action.kind == "key":
            self.send_keys(action.keys)
        elif action.kind == "app" and action.app:
            self.launch(action.app.app_id)
        elif action.kind == "text":
            self.type_text(action.text)


class DryRunRemote:
    """Records the actions instead of sending them. Used by --dry-run and the tests."""

    def __init__(self) -> None:
        self.sent: List[Tuple[str, str]] = []

    def execute(self, action: Action) -> None:
        if action.kind == "key":
            self.sent.extend(("key", k) for k in action.keys)
        elif action.kind == "app" and action.app:
            self.sent.append(("app", action.app.app_id))
        elif action.kind == "text":
            self.sent.append(("text", action.text))

    def close(self) -> None:
        pass


def device_info(host: str = DEFAULT_HOST, timeout: float = 3.0) -> dict:
    """Read-only GET of the TV's REST device info (port 8001). Never POSTs."""
    import requests

    started = time.perf_counter()
    response = requests.get(f"http://{host}:8001/api/v2/", timeout=timeout)
    response.raise_for_status()
    body = response.json()
    body["_latency_ms"] = round((time.perf_counter() - started) * 1000, 1)
    return body
