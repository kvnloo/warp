from __future__ import annotations

import numpy as np
import pytest

from adduck.detect import AdDetector, unit_fit
from adduck.engine import Engine
from adduck.features import BLOCK_S, N_BANDS, Features, extract_features
from adduck.nn import LogisticModel
from adduck.ringbuffer import RingBuffer
from adduck.state import Action, AdState, AdStateMachine, StateConfig
from adduck.tv import DryRunTV, Ducker, DuckerConfig, FakeTV

SR = 16000
rng = np.random.default_rng(0)


def speechy(n: int, level_db: float = -28.0) -> np.ndarray:
    t = np.arange(n) / SR
    env = np.clip(np.sin(2 * np.pi * 4 * t) + np.sin(2 * np.pi * 5.3 * t), 0, None) ** 2
    x = rng.standard_normal(n) * (0.05 + env)
    return x / np.sqrt(np.mean(x**2)) * 10 ** (level_db / 20)


def compressed(n: int, level_db: float) -> np.ndarray:
    x = np.tanh(6 * speechy(n, -10))
    return x / np.sqrt(np.mean(x**2)) * 10 ** (level_db / 20)


def feat(loud: float, crest: float, silent: bool = False) -> Features:
    return Features(loud, crest, silent, tuple([loud - 10] * N_BANDS))


# -- features -----------------------------------------------------------------


def test_feature_record_is_tiny_and_quantised():
    f = extract_features(speechy(SR // 2), SR)
    assert len(f.bands_db) == N_BANDS == 8
    for v in (f.loudness_db, f.crest_db):
        assert (v * 2) == int(v * 2)  # 0.5 dB steps
    assert all(b == int(b) for b in f.bands_db)  # 1 dB steps


def test_louder_is_louder_and_compression_lowers_crest():
    a = extract_features(speechy(SR // 2, -30), SR)
    b = extract_features(speechy(SR // 2, -24), SR)
    c = extract_features(compressed(SR // 2, -24), SR)
    assert b.loudness_db - a.loudness_db == pytest.approx(6, abs=1.0)
    assert c.crest_db < b.crest_db - 3


def test_silence_flag():
    assert extract_features(np.zeros(SR // 2), SR).silent
    assert not extract_features(speechy(SR // 2), SR).silent


def test_extract_does_not_mutate_input():
    x = speechy(SR // 2)
    before = x.copy()
    extract_features(x, SR)
    assert np.array_equal(x, before)


def test_ringbuffer_rate_size_and_wipe():
    ring = RingBuffer(SR)
    out = []
    for chunk in np.array_split(speechy(SR * 3), 37):  # odd chunk sizes
        out += list(ring.push(chunk))
    assert len(out) == 6  # exactly 2 records per second
    assert ring._buf.size == int(SR * BLOCK_S)  # never grows
    ring.wipe()
    assert not ring._buf.any()
    assert "[" not in repr(ring)  # repr never prints samples


# -- state machine ------------------------------------------------------------


def run_machine(m: AdStateMachine, confs, t0=0.0):
    acts = []
    t = t0
    for c in confs:
        a = m.update(c, t)
        if a:
            acts.append((t, a))
        t += 0.5
    return acts, t


def test_duck_needs_dwell_and_restore_is_faster():
    m = AdStateMachine(StateConfig(duck_dwell_s=2.0, restore_dwell_s=1.0))
    acts, _ = run_machine(m, [0.9] * 3 + [0.1] + [0.9] * 5)
    assert acts == [(4.0, Action.DUCK)]  # the blip at 1.5 s restarted the 2 s dwell at 2.0
    acts, _ = run_machine(m, [0.1] * 3, 10.0)
    assert acts == [(11.0, Action.RESTORE)]


def test_max_duck_ceiling():
    m = AdStateMachine(StateConfig(max_duck_s=10.0))
    acts, _ = run_machine(m, [1.0] * 40)
    assert [a for _, a in acts][:2] == [Action.DUCK, Action.RESTORE]
    assert acts[1][0] - acts[0][0] == pytest.approx(10.0)


def test_recovery_ignores_votes():
    m = AdStateMachine(StateConfig(recovery_s=3.0))
    m.state = AdState.AD
    m._entered_ad = 0.0
    m.leave(1.0)
    assert m.update(1.0, 2.0) is None and m.state is AdState.RECOVERY


# -- ducker -------------------------------------------------------------------


def test_duck_and_restore_prior_volume():
    tv = FakeTV(volume=27)
    d = Ducker(tv, DuckerConfig(duck_level=3))
    assert d.duck(0.0) and tv.volume == 3
    assert d.restore(5.0) and tv.volume == 27


def test_never_raises_volume():
    tv = FakeTV(volume=2)
    d = Ducker(tv, DuckerConfig(duck_level=3))
    assert not d.duck(0.0) and tv.volume == 2 and tv.calls == []


def test_manual_change_cancels_and_backs_off():
    tv = FakeTV(volume=20)
    d = Ducker(tv, DuckerConfig(backoff_s=600))
    d.duck(0.0)
    tv.user_sets(12)
    assert d.tick(1.0) == "manual"
    assert tv.volume == 12 and not d.ducked  # the user's choice is kept
    assert not d.duck(100.0)  # backing off
    assert d.duck(700.0)


def test_ducker_ceiling():
    tv = FakeTV(volume=20)
    d = Ducker(tv, DuckerConfig(max_duck_s=60))
    d.duck(0.0)
    assert d.tick(30.0) is None
    assert d.tick(60.0) == "ceiling" and tv.volume == 20


def test_duck_error_restores():
    tv = FakeTV(volume=20, fail_duck=True)
    d = Ducker(tv)
    assert not d.duck(0.0)
    assert ("restore", 20) in tv.calls or tv.volume == 20
    assert not d.ducked


def test_restore_failure_is_retried():
    tv = FakeTV(volume=20, fail_restore=2)
    d = Ducker(tv)
    d.duck(0.0)
    assert not d.restore(1.0)
    assert d.tick(1.5) == "retry_restore" and tv.volume == 3
    d.tick(2.0)
    assert tv.volume == 20 and not d.ducked


def test_get_volume_error_while_ducked_restores():
    tv = FakeTV(volume=20)
    d = Ducker(tv)
    d.duck(0.0)
    tv.fail_get = True
    assert d.tick(1.0) == "error" and tv.volume == 20


def test_ease_stays_below_prior():
    tv = FakeTV(volume=8)
    d = Ducker(tv, DuckerConfig(duck_level=3))
    d.duck(0.0)
    assert d.ease(1.0, 3) and tv.volume == 6
    assert d.ease(2.0, 3) and tv.volume == 7
    assert not d.ease(3.0, 3)
    assert d.tick(4.0) is None  # eased level is ours, not a manual change
    d.restore(5.0)
    assert tv.volume == 8


def test_dry_run_records_intents_only():
    tv = DryRunTV(volume=15)
    d = Ducker(tv)
    d.duck(0.0)
    d.restore(1.0)
    assert tv.intents == [("duck", 3), ("restore", 15)]


# -- detector -----------------------------------------------------------------


def feed(det: AdDetector, feats, t0=0.0):
    t = t0
    votes = []
    for f in feats:
        votes.append(det.observe(f, t))
        t += BLOCK_S
    return votes, t


def test_loud_compressed_segment_votes_ad_but_loud_uncompressed_does_not():
    det = AdDetector()
    _, t = feed(det, [feat(-28, 18)] * 120)
    loud_action, t = feed(det, [feat(-24, 19)] * 10, t)
    assert loud_action[-1].confidence < 0.6
    _, t = feed(det, [feat(-28, 18)] * 60, t)
    ad, t = feed(det, [feat(-23, 8)] * 10, t)
    assert ad[-1].confidence >= 0.9


def test_baseline_freezes_during_elevation():
    det = AdDetector()
    _, t = feed(det, [feat(-28, 18)] * 120)
    base = det.baseline_db
    feed(det, [feat(-22, 8)] * 240, t)  # two minutes of hot ads
    assert det.baseline_db == pytest.approx(base, abs=0.2)


def test_duck_compensation_measures_drop():
    det = AdDetector()
    _, t = feed(det, [feat(-28, 18)] * 120)
    _, t = feed(det, [feat(-22, 8)] * 10, t)
    det.on_duck(t, True)
    votes, t = feed(det, [feat(-40, 8)] * 12, t)  # mic hears the ducked ad
    assert det.duck_offset_db == pytest.approx(18, abs=1.0)
    assert votes[-1].confidence >= 0.9  # still judged an ad on the original scale
    votes, t = feed(det, [feat(-46, 18)] * 4, t)  # programme back (ducked scale)
    assert votes[-1].confidence < 0.4


def test_unit_grid():
    assert unit_fit(30.4, 1.25) == 30.0
    assert unit_fit(22.0, 1.25) is None


# -- engine -------------------------------------------------------------------


class Boom(AdDetector):
    def observe(self, f, ts):
        raise RuntimeError("boom")


def test_engine_error_restores_volume():
    tv = FakeTV(volume=20)
    eng = Engine(Boom(), AdStateMachine(), Ducker(tv))
    eng.ducker.duck(0.0)
    step = eng.step(feat(-28, 18), 0.5)
    assert step.action == "restore:error" and tv.volume == 20


def test_engine_run_always_restores_on_exit():
    tv = FakeTV(volume=20)
    eng = Engine(AdDetector(), AdStateMachine(), Ducker(tv))

    def stream():
        yield from [feat(-28, 18)] * 120
        yield from [feat(-22, 8)] * 20
        raise KeyboardInterrupt

    with pytest.raises(KeyboardInterrupt):
        eng.run(stream())
    assert ("duck", 3) in tv.calls and tv.volume == 20


# -- nn -----------------------------------------------------------------------


def test_logistic_model_fits_and_roundtrips():
    x = np.vstack([rng.normal(-1, 0.3, (100, 3)), rng.normal(1, 0.3, (100, 3))])
    y = np.r_[np.zeros(100), np.ones(100)]
    m = LogisticModel(3).fit(x, y)
    assert ((m.predict(x) > 0.5) == y).mean() > 0.97
    m2 = LogisticModel.from_dict(m.to_dict())
    assert np.allclose(m2.predict(x), m.predict(x))
