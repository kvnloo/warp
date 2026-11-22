import types

import pytest

from tv_voice import jev_tier
from tv_voice.actions import JEV_CANDIDATES
from tv_voice.controller import Controller
from tv_voice.jev_tier import JevTier, load_jevkit
from tv_voice.remote import DryRunRemote


def fake_jevkit(selected="mute", confidence=0.9, sensitive=False, raises=None, min_conf=0.65):
    calls = []

    def choose(request, timeout=3.0):
        calls.append(request)
        if raises:
            raise raises
        return {"schema": "jev.action_choice_v1", "selected_id": selected, "confidence": confidence,
                "reason": "chosen"}

    privacy = types.SimpleNamespace(is_sensitive=lambda text: sensitive,
                                    redact=lambda text: text.replace("555-867-5309", "[phone]"),
                                    normalize=lambda text: text)
    kit = types.SimpleNamespace(choose=types.SimpleNamespace(choose=choose, MIN_CONFIDENCE=min_conf),
                                privacy=privacy)
    return kit, calls


def test_request_uses_the_fixed_table_only():
    kit, calls = fake_jevkit()
    result = JevTier(kit).choose("make the telly hush")
    assert result.action.id == "mute" and result.reason == "chosen"
    request = calls[0]
    assert request["schema"] == "jev.action_choice_request_v1"
    assert request["candidates"] == JEV_CANDIDATES
    ids = {c["id"] for c in request["candidates"]}
    assert {"reobserve", "abstain"} <= ids and "power.off" not in ids
    assert 2 <= len(request["candidates"]) <= 32
    assert set(request) == {"schema", "goal", "candidates"}


def test_below_floor_fails_closed():
    kit, _ = fake_jevkit(confidence=0.64)
    result = JevTier(kit).choose("do the thing with the sound")
    assert result.action is None and result.reason == "below confidence floor"


def test_floor_never_below_local_floor_even_if_jevkit_lower():
    kit, _ = fake_jevkit(confidence=0.62, min_conf=0.5)
    assert JevTier(kit).floor() == pytest.approx(0.65)
    assert JevTier(kit).choose("sound thing").action is None


def test_floor_follows_a_stricter_jevkit():
    kit, _ = fake_jevkit(confidence=0.8, min_conf=0.9)
    assert JevTier(kit).choose("sound thing").action is None


@pytest.mark.parametrize("selected", ["reobserve", "abstain", "KEY_POWER", "rm -rf", None])
def test_declines_and_unknown_ids_fail_closed(selected):
    kit, _ = fake_jevkit(selected=selected, confidence=0.99)
    assert JevTier(kit).choose("hmm").action is None


def test_sensitive_text_is_never_sent():
    kit, calls = fake_jevkit(sensitive=True)
    result = JevTier(kit).choose("my password is hunter2")
    assert result.action is None and "private" in result.reason
    assert calls == []


def test_redactable_text_is_never_sent():
    kit, calls = fake_jevkit()
    result = JevTier(kit).choose("call 555-867-5309 on the tv")
    assert result.action is None and calls == []


def test_overlong_text_is_never_sent():
    kit, calls = fake_jevkit()
    assert JevTier(kit).choose("blah " * 100).action is None and calls == []


def test_jev_exception_fails_closed():
    kit, _ = fake_jevkit(raises=TimeoutError("slow"))
    result = JevTier(kit).choose("something odd")
    assert result.action is None and "TimeoutError" in result.reason


def test_missing_jevkit_fails_closed(tmp_path):
    assert load_jevkit(str(tmp_path)) is None
    tier = JevTier(None, autoload=False)
    result = tier.choose("make it sing")
    assert result.action is None and result.reason == "jevkit not available"


def test_missing_jevkit_via_env(monkeypatch, tmp_path):
    monkeypatch.setenv("TV_VOICE_JEVKIT_DIR", str(tmp_path / "nope"))
    assert load_jevkit() is None


def test_broken_jevkit_fails_closed(tmp_path):
    pkg = tmp_path / "jevkit"
    pkg.mkdir()
    (pkg / "__init__.py").write_text("raise ImportError('broken')\n")
    (pkg / "choose.py").write_text("")
    assert load_jevkit(str(tmp_path)) is None


def test_controller_uses_rules_first_and_never_calls_jev_for_a_rule_hit():
    kit, calls = fake_jevkit()
    remote = DryRunRemote()
    result = Controller(remote, JevTier(kit), dry_run=True).handle("turn it up 5")
    assert result.tier == "rules" and result.ok and calls == []
    assert remote.sent == [("key", "KEY_VOLUP")] * 5


def test_controller_escalates_to_jev_and_sends_its_pick():
    kit, calls = fake_jevkit(selected="open.jellyfin", confidence=0.91)
    remote = DryRunRemote()
    result = Controller(remote, JevTier(kit), dry_run=True).handle("I wanna watch my own movies")
    assert result.tier == "jev" and result.ok and len(calls) == 1
    assert remote.sent == [("app", "AprZAARz4r.Jellyfin")]


def test_controller_fails_closed_when_jev_unsure():
    kit, _ = fake_jevkit(confidence=0.3)
    remote = DryRunRemote()
    result = Controller(remote, JevTier(kit), dry_run=True).handle("do the thing")
    assert not result.ok and result.tier == "none" and remote.sent == []


def test_controller_without_jevkit_fails_closed():
    remote = DryRunRemote()
    result = Controller(remote, JevTier(None, autoload=False), dry_run=True).handle("do the thing")
    assert not result.ok and remote.sent == [] and result.detail == "jevkit not available"


def test_weak_fuzzy_rule_goes_to_jev(monkeypatch):
    kit, calls = fake_jevkit(selected="pause", confidence=0.9)
    remote = DryRunRemote()
    result = Controller(remote, JevTier(kit), dry_run=True).handle("pawz")
    assert len(calls) == 1 and result.tier == "jev"


def test_local_floor_constant():
    assert jev_tier.FLOOR == 0.65
