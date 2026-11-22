from evaluation.run_eval import _match, score, simulate
from evaluation.synth import SCENARIOS


def test_match_is_one_to_one_and_windowed():
    hits, offsets = _match([10.0, 11.0, 50.0], [9.0, 30.0], 0.0, 5.0)
    assert hits == 1 and offsets == [1.0]


def test_closed_loop_loud_ads_smoke():
    r = simulate(SCENARIOS[0], 100)
    m = score([r])
    assert m["duck_recall"] == 1.0
    assert m["restore_recall"] >= 0.75
    assert len(r.ducks) == len(r.restores)  # every duck was given back
