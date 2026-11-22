import pytest

from tv_voice import intent

# (phrase, action id, what gets sent)
CASES = [
    ("turn it up 5", "volume.up", "KEY_VOLUP x5"),
    ("Turn it up five.", "volume.up", "KEY_VOLUP x5"),
    ("volume up", "volume.up", "KEY_VOLUP x2"),
    ("volume up 10", "volume.up", "KEY_VOLUP x10"),
    ("hey jev, turn the volume up a little", "volume.up", "KEY_VOLUP x2"),
    ("can you turn it up a bit please", "volume.up", "KEY_VOLUP x2"),
    ("louder", "volume.up", "KEY_VOLUP x2"),
    ("a little louder", "volume.up", "KEY_VOLUP x2"),
    ("make it louder", "volume.up", "KEY_VOLUP x2"),
    ("turn up the volume by three", "volume.up", "KEY_VOLUP x3"),
    ("crank it up a lot", "volume.up", "KEY_VOLUP x8"),
    ("I can't hear anything", "volume.up", "KEY_VOLUP x2"),
    ("raise the volume a few notches", "volume.up", "KEY_VOLUP x3"),
    ("volume up twenty five", "volume.up", "KEY_VOLUP x20"),  # clamped
    ("turn it down", "volume.down", "KEY_VOLDOWN x2"),
    ("turn it down 4", "volume.down", "KEY_VOLDOWN x4"),
    ("volume down a little", "volume.down", "KEY_VOLDOWN x2"),
    ("quieter please", "volume.down", "KEY_VOLDOWN x2"),
    ("um, lower the volume", "volume.down", "KEY_VOLDOWN x2"),
    ("it's too loud", "volume.down", "KEY_VOLDOWN x2"),
    ("turn the sound down by 6", "volume.down", "KEY_VOLDOWN x6"),
    ("mute", "mute", "KEY_MUTE"),
    ("mute the tv", "mute", "KEY_MUTE"),
    ("please mute it", "mute", "KEY_MUTE"),
    ("turn the sound off", "mute", "KEY_MUTE"),
    ("unmute", "mute", "KEY_MUTE"),
    ("un-mute", "mute", "KEY_MUTE"),
    ("turn the sound back on", "mute", "KEY_MUTE"),
    ("pause", "pause", "KEY_PAUSE"),
    ("pause the movie", "pause", "KEY_PAUSE"),
    ("hold on", "pause", "KEY_PAUSE"),
    ("play", "play", "KEY_PLAY"),
    ("resume", "play", "KEY_PLAY"),
    ("unpause", "play", "KEY_PLAY"),
    ("keep going", "play", "KEY_PLAY"),
    ("go back", "back", "KEY_RETURN"),
    ("back", "back", "KEY_RETURN"),
    ("go home", "home", "KEY_HOME"),
    ("home screen", "home", "KEY_HOME"),
    ("open jellyfin", "open.jellyfin", "launch Jellyfin"),
    ("launch the jellyfin app", "open.jellyfin", "launch Jellyfin"),
    ("open warp", "open.warp", "launch Warp"),
    ("put on plex", "open.warp", "launch Warp"),
    ("can you open netflix", "open.netflix", "launch Netflix"),
    ("watch youtube", "open.youtube", "launch YouTube"),
    ("channel up", "channel.up", "KEY_CHUP"),
    ("next channel", "channel.up", "KEY_CHUP"),
    ("channel down", "channel.down", "KEY_CHDOWN"),
    ("go to channel 7", "channel.7", "KEY_7 KEY_ENTER"),
    ("channel 12", "channel.12", "KEY_1 KEY_2 KEY_ENTER"),
    ("hdmi 2", "hdmi.2", "KEY_HDMI2"),
    ("switch to HDMI 1", "hdmi.1", "KEY_HDMI1"),
    ("switch to hdmi two", "hdmi.2", "KEY_HDMI2"),
    ("change the input", "source", "KEY_SOURCE"),
    ("source", "source", "KEY_SOURCE"),
    ("turn off the tv", "power.off", "KEY_POWER"),
    ("turn it off", "power.off", "KEY_POWER"),
    ("power off", "power.off", "KEY_POWER"),
    ("shut it down", "power.off", "KEY_POWER"),
    ("goodnight", "power.off", "KEY_POWER"),
    ("next", "ff", "KEY_FF"),
    ("skip", "ff", "KEY_FF"),
    ("next episode", "ff", "KEY_FF"),
    ("fast forward", "ff", "KEY_FF"),
    ("rewind", "rewind", "KEY_REWIND"),
    ("up", "up", "KEY_UP"),
    ("down 3", "down", "KEY_DOWN x3"),
    ("go left", "left", "KEY_LEFT"),
    ("move right two", "right", "KEY_RIGHT x2"),
    ("ok", "ok", "KEY_ENTER"),
    ("okay", "ok", "KEY_ENTER"),
    ("select", "ok", "KEY_ENTER"),
    ("press ok", "ok", "KEY_ENTER"),
    ("open settings", "menu", "KEY_MENU"),
    ("guide", "guide", "KEY_GUIDE"),
]


@pytest.mark.parametrize("phrase,action_id,sends", CASES)
def test_rule_tier(phrase, action_id, sends):
    match = intent.parse(phrase)
    assert match is not None, phrase
    assert match.action.id == action_id
    assert match.action.summary() == sends
    assert match.confidence >= 0.85


def test_at_least_forty_phrasings():
    assert len(CASES) >= 40


@pytest.mark.parametrize("phrase,query", [
    ("search for the office", "the office"),
    ("search severance", "severance"),
    ("look up planet earth", "planet earth"),
])
def test_search_keeps_query(phrase, query):
    match = intent.parse(phrase)
    assert match.action.kind == "text" and match.action.text == query
    assert query not in match.action.summary()  # the query never reaches a log line


@pytest.mark.parametrize("phrase", [
    "what's the weather tomorrow",
    "set the volume to 20",        # absolute volume: unknown current level, never guess
    "turn it on",                  # off TV has no network remote
    "hdmi 9",
    "turn it up no down",
    "",
    "   ",
    "x" * 300,
])
def test_unrecognised_or_unsafe_returns_none(phrase):
    assert intent.parse(phrase) is None


@pytest.mark.parametrize("phrase,action_id", [("mutte", "mute"), ("pase", "pause"), ("hom", "home")])
def test_fuzzy_single_word(phrase, action_id):
    match = intent.parse(phrase)
    assert match.action.id == action_id and match.rule == "fuzzy"
    assert match.confidence < 1.0


def test_fuzzy_app_name():
    match = intent.parse("open jelyfin")
    assert match.action.id == "open.jellyfin" and 0.85 <= match.confidence < 1.0


def test_arrow_steps_are_capped():
    assert intent.parse("down 30").action.summary() == "KEY_DOWN x10"


def test_turn_off_sound_is_mute_not_power():
    assert intent.parse("turn off the sound").action.id == "mute"
