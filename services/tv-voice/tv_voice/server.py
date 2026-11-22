"""Tiny LAN HTTP front end: POST /command {"text": "..."} -> JSON result.

Push-to-talk clients (a phone shortcut, a future local-ASR listener) post the
recognised text here. Set TV_VOICE_HTTP_TOKEN to require an X-TV-Voice-Token
header; leave it unset only on a trusted LAN.
"""
from __future__ import annotations

import hmac
import json
import os
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Optional

from .controller import Controller

MAX_BODY = 2048


def make_server(controller: Controller, host: str = "127.0.0.1", port: int = 8765,
                token: Optional[str] = None) -> ThreadingHTTPServer:
    secret = token if token is not None else (os.environ.get("TV_VOICE_HTTP_TOKEN") or None)

    class Handler(BaseHTTPRequestHandler):
        server_version = "tv-voice/0.1"

        def log_message(self, *args) -> None:  # the controller logs; no request lines with paths
            pass

        def _reply(self, code: int, body: dict) -> None:
            data = json.dumps(body).encode()
            self.send_response(code)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def do_GET(self) -> None:
            if self.path == "/healthz":
                self._reply(200, {"ok": True, "dry_run": controller.dry_run})
            else:
                self._reply(404, {"ok": False, "error": "not found"})

        def do_POST(self) -> None:
            if self.path != "/command":
                return self._reply(404, {"ok": False, "error": "not found"})
            if secret and not hmac.compare_digest(self.headers.get("X-TV-Voice-Token", ""), secret):
                return self._reply(401, {"ok": False, "error": "unauthorized"})
            try:
                length = int(self.headers.get("Content-Length", "0"))
            except ValueError:
                length = -1
            if not 0 < length <= MAX_BODY:
                return self._reply(400, {"ok": False, "error": "body required, max 2 KB"})
            try:
                payload = json.loads(self.rfile.read(length))
                text = payload["text"]
                assert isinstance(text, str) and 0 < len(text.strip()) <= 200
            except Exception:
                return self._reply(400, {"ok": False, "error": 'expected {"text": "<1-200 chars>"}'})
            self._reply(200, controller.handle(text).to_dict())

    return ThreadingHTTPServer((host, port), Handler)
