import json
import logging
import threading
import urllib.error
import urllib.request

import pytest

from tv_voice import cli
from tv_voice.controller import Controller
from tv_voice.jev_tier import JevTier
from tv_voice.remote import DryRunRemote
from tv_voice.server import make_server


@pytest.fixture
def no_jev(monkeypatch, tmp_path):
    monkeypatch.setenv("TV_VOICE_JEVKIT_DIR", str(tmp_path / "none"))


def test_cli_dry_run_say(capsys, no_jev):
    assert cli.main(["--dry-run", "say", "turn", "it", "up", "5"]) == 0
    out = json.loads(capsys.readouterr().out)
    assert out["ok"] and out["dry_run"] and out["tier"] == "rules"
    assert out["detail"] == "would send KEY_VOLUP x5"


def test_cli_dry_run_unknown_fails_closed(capsys, no_jev):
    assert cli.main(["--dry-run", "say", "what's", "the", "weather"]) == 1
    out = json.loads(capsys.readouterr().out)
    assert not out["ok"] and out["action"] == "none"


def test_cli_parse(capsys):
    assert cli.main(["parse", "open", "jellyfin"]) == 0
    assert json.loads(capsys.readouterr().out)["action"] == "open.jellyfin"


def test_unverified_app_refused_by_default(monkeypatch):
    monkeypatch.delenv("TV_VOICE_ALLOW_UNVERIFIED_APPS", raising=False)
    dry = DryRunRemote()
    result = Controller(dry, JevTier(None, autoload=False), dry_run=True).handle("open netflix")
    assert not result.ok and "unverified" in result.detail and dry.sent == []
    monkeypatch.setenv("TV_VOICE_ALLOW_UNVERIFIED_APPS", "1")
    result = Controller(dry, JevTier(None, autoload=False), dry_run=True).handle("open netflix")
    assert result.ok and dry.sent == [("app", "3201907018807")]


def test_logs_carry_no_transcript(caplog):
    controller = Controller(DryRunRemote(), JevTier(None, autoload=False), dry_run=True)
    logger = logging.getLogger("tv_voice")
    logger.propagate = True
    with caplog.at_level(logging.INFO, logger="tv_voice"):
        controller.handle("search for my secret show")
    logger.propagate = False
    line = json.loads(caplog.records[-1].getMessage())
    assert set(line) == {"tier", "action", "ok", "latency_ms", "dry_run"}
    assert "secret" not in caplog.text
    assert controller.recent[-1]["text"] == "search for my secret show"  # memory only


def test_recent_ring_is_bounded():
    controller = Controller(DryRunRemote(), JevTier(None, autoload=False), dry_run=True)
    for _ in range(50):
        controller.handle("mute")
    assert len(controller.recent) == 20


def post(port, body, headers=None):
    data = body if isinstance(body, bytes) else json.dumps(body).encode()
    request = urllib.request.Request(f"http://127.0.0.1:{port}/command", data=data,
                                     headers={"Content-Type": "application/json", **(headers or {})})
    try:
        with urllib.request.urlopen(request, timeout=3) as response:
            return response.status, json.loads(response.read())
    except urllib.error.HTTPError as error:
        return error.code, json.loads(error.read())


@pytest.fixture
def server():
    dry = DryRunRemote()
    controller = Controller(dry, JevTier(None, autoload=False), dry_run=True)
    httpd = make_server(controller, "127.0.0.1", 0, token="")
    threading.Thread(target=httpd.serve_forever, daemon=True).start()
    yield httpd.server_address[1], dry
    httpd.shutdown()


def test_http_command(server):
    port, dry = server
    status, body = post(port, {"text": "go back"})
    assert status == 200 and body["ok"] and body["action"] == "back"
    assert dry.sent == [("key", "KEY_RETURN")]


@pytest.mark.parametrize("body", [{"nope": 1}, {"text": ""}, {"text": "x" * 500}, b"not json"])
def test_http_bad_bodies(server, body):
    port, dry = server
    status, _ = post(port, body)
    assert status == 400 and dry.sent == []


def test_http_token_required_when_configured():
    dry = DryRunRemote()
    httpd = make_server(Controller(dry, JevTier(None, autoload=False), dry_run=True), "127.0.0.1", 0, token="s3")
    threading.Thread(target=httpd.serve_forever, daemon=True).start()
    try:
        port = httpd.server_address[1]
        assert post(port, {"text": "mute"})[0] == 401
        assert post(port, {"text": "mute"}, {"X-TV-Voice-Token": "s3"})[0] == 200
        assert dry.sent == [("key", "KEY_MUTE")]
    finally:
        httpd.shutdown()
