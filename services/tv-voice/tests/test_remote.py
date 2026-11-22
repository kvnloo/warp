"""The Samsung client against a local fake TV WebSocket. Never touches the real TV."""
import base64
import json
import stat
import threading
import time
from urllib.parse import parse_qs, urlparse

import pytest
from websockets.sync.server import serve

from tv_voice import remote as R
from tv_voice.actions import APPS, launch
from tv_voice.intent import parse


class FakeTV:
    def __init__(self, token="tok-12345", close_after=None):
        self.token, self.close_after = token, close_after
        self.messages, self.paths, self.connections = [], [], 0
        self.server = serve(self._handler, "127.0.0.1", 0)
        self.port = self.server.socket.getsockname()[1]
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()

    def _handler(self, ws):
        self.connections += 1
        self.paths.append(ws.request.path)
        ws.send(json.dumps({"event": "ms.channel.connect", "data": {"id": "x", "token": self.token}}))
        count = 0
        for message in ws:
            self.messages.append(json.loads(message))
            count += 1
            if self.close_after and count >= self.close_after and self.connections == 1:
                ws.close()
                return

    def stop(self):
        self.server.shutdown()


@pytest.fixture
def home(tmp_path, monkeypatch):
    monkeypatch.setenv("XDG_CONFIG_HOME", str(tmp_path / "cfg"))
    monkeypatch.delenv("TV_VOICE_TOKEN_FILE", raising=False)
    return tmp_path


def wait_for(predicate, timeout=3.0):
    end = time.time() + timeout
    while time.time() < end:
        if predicate():
            return True
        time.sleep(0.02)
    return False


def test_key_json_is_exact_and_token_saved_private(home):
    tv = FakeTV()
    try:
        remote = R.TvRemote("127.0.0.1", tv.port)
        remote.execute(parse("volume up 2").action)
        assert wait_for(lambda: len(tv.messages) == 2)
        assert tv.messages[0] == {"method": "ms.remote.control",
                                  "params": {"Cmd": "Click", "DataOfCmd": "KEY_VOLUP", "Option": "false",
                                             "TypeOfRemote": "SendRemoteKey"}}
        assert tv.messages == [tv.messages[0]] * 2
        path = urlparse(tv.paths[0])
        assert path.path == "/api/v2/channels/samsung.remote.control"
        assert base64.b64decode(parse_qs(path.query)["name"][0]).decode() == "tv-voice"
        token_file = home / "cfg" / "tv-voice" / "token"
        assert token_file.read_text() == "tok-12345"
        assert stat.S_IMODE(token_file.stat().st_mode) == 0o600
        assert stat.S_IMODE(token_file.parent.stat().st_mode) == 0o700
        remote.close()
    finally:
        tv.stop()


def test_one_connection_is_reused(home):
    tv = FakeTV()
    try:
        remote = R.TvRemote("127.0.0.1", tv.port)
        remote.execute(parse("mute").action)
        remote.execute(parse("pause").action)
        assert wait_for(lambda: len(tv.messages) == 2)
        assert tv.connections == 1
        assert [m["params"]["DataOfCmd"] for m in tv.messages] == ["KEY_MUTE", "KEY_PAUSE"]
        remote.close()
    finally:
        tv.stop()


def test_reconnects_after_the_tv_drops_the_socket(home):
    tv = FakeTV(close_after=1)
    try:
        remote = R.TvRemote("127.0.0.1", tv.port)
        remote.execute(parse("mute").action)
        assert wait_for(lambda: len(tv.messages) == 1)
        time.sleep(0.2)  # the TV has closed the socket by now
        remote.execute(parse("play").action)
        assert wait_for(lambda: len(tv.messages) == 2)
        assert tv.connections == 2
        assert tv.messages[1]["params"]["DataOfCmd"] == "KEY_PLAY"
        remote.close()
    finally:
        tv.stop()


def test_reconnects_after_a_local_socket_error(home):
    tv = FakeTV()
    try:
        remote = R.TvRemote("127.0.0.1", tv.port)
        remote.execute(parse("mute").action)
        remote._tv.connection.sock.close()  # simulate a dead socket under the client
        remote.execute(parse("home").action)
        assert wait_for(lambda: len(tv.messages) == 2)
        assert tv.messages[1]["params"]["DataOfCmd"] == "KEY_HOME" and tv.connections == 2
        remote.close()
    finally:
        tv.stop()


def test_unreachable_tv_raises_remote_error(home):
    tv = FakeTV()
    port = tv.port
    tv.stop()
    time.sleep(0.1)
    with pytest.raises(R.RemoteError):
        R.TvRemote("127.0.0.1", port, timeout=1).execute(parse("mute").action)


def test_app_launch_goes_over_the_websocket(home):
    tv = FakeTV()
    try:
        remote = R.TvRemote("127.0.0.1", tv.port)
        jellyfin = next(a for a in APPS if a.name == "Jellyfin")
        remote.execute(launch(jellyfin))
        assert wait_for(lambda: len(tv.messages) == 1)
        assert tv.messages[0] == {"method": "ms.channel.emit", "params": {
            "event": "ed.apps.launch", "to": "host",
            "data": {"action_type": "DEEP_LINK", "appId": "AprZAARz4r.Jellyfin", "metaTag": ""}}}
        remote.close()
    finally:
        tv.stop()


def test_saved_token_rides_in_the_wss_url(home):
    token_file = home / "cfg" / "tv-voice" / "token"
    R.write_private(token_file, "saved-token")
    tv = R._make_tv("192.168.1.28", 8002, token_file, "tv-voice", 1)
    url = tv._format_websocket_url("samsung.remote.control")
    assert url.startswith("wss://192.168.1.28:8002/api/v2/channels/samsung.remote.control?")
    assert parse_qs(urlparse(url).query)["token"] == ["saved-token"]


def test_dry_run_remote_records_and_sends_nothing():
    dry = R.DryRunRemote()
    dry.execute(parse("channel 7").action)
    dry.execute(parse("search for dune").action)
    assert dry.sent == [("key", "KEY_7"), ("key", "KEY_ENTER"), ("text", "dune")]
