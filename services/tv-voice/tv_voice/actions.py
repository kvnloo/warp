"""The closed set of things this controller can do to the TV.

Every voice command ends up as one `Action`. Nothing else ever reaches the TV:
no free-form key names from Jev, no URLs, no shell. Jev only ever sees the
ids and descriptions in `JEV_CANDIDATES` and can only pick one of them.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple

MAX_VOLUME_STEPS = 20  # a misheard "volume up 80" must not blast the room
MAX_ARROW_STEPS = 10


@dataclass(frozen=True)
class App:
    name: str
    app_id: str
    verified: bool
    aliases: Tuple[str, ...] = ()


# verified = the id was confirmed on this QN85B (Warp installed by us, Jellyfin seen
# in the TV's app list). The rest are commonly published Tizen store ids and have NOT
# been checked on this TV; launching them needs TV_VOICE_ALLOW_UNVERIFIED_APPS=1.
APPS: Tuple[App, ...] = (
    App("Warp", "WarpTV2022.Warp", True, ("warp", "plex", "warp tv")),
    App("Jellyfin", "AprZAARz4r.Jellyfin", True, ("jellyfin", "jelly fin", "jelly")),
    App("Netflix", "3201907018807", False, ("netflix",)),
    App("YouTube", "111299001912", False, ("youtube", "you tube")),
    App("Prime Video", "3201910019365", False, ("prime video", "prime", "amazon", "amazon prime")),
    App("Disney+", "3201901017640", False, ("disney", "disney plus")),
    App("Spotify", "3201606009684", False, ("spotify",)),
    App("Hulu", "3201601007625", False, ("hulu",)),
)


def app_by_name(name: str) -> Optional[App]:
    for app in APPS:
        if app.name.lower() == name.lower():
            return app
    return None


@dataclass(frozen=True)
class Action:
    """kind: key | app | text | none. `keys` is the exact key sequence to click."""
    id: str
    kind: str
    description: str
    keys: Tuple[str, ...] = ()
    app: Optional[App] = None
    text: str = field(default="", repr=False)  # search query; never logged

    def summary(self) -> str:
        if self.kind == "key":
            if len(set(self.keys)) == 1 and len(self.keys) > 1:
                return f"{self.keys[0]} x{len(self.keys)}"
            return " ".join(self.keys)
        if self.kind == "app":
            return f"launch {self.app.name}" if self.app else "launch ?"
        if self.kind == "text":
            return f"search ({len(self.text)} chars)"
        return "no action"


def keys(action_id: str, description: str, *seq: str) -> Action:
    return Action(action_id, "key", description, tuple(seq))


def repeat(action_id: str, description: str, key: str, n: int, cap: int) -> Action:
    n = max(1, min(int(n), cap))
    return Action(action_id, "key", description, tuple([key] * n))


def launch(app: App) -> Action:
    return Action(f"open.{app.name.lower().replace(' ', '_').replace('+', 'plus')}", "app",
                  f"open the {app.name} app", app=app)


NONE = Action("none", "none", "do nothing")

# Parameterless actions Jev may choose between when the rule tier is unsure. Power is
# deliberately absent: turning the TV off needs an unambiguous rule match.
_JEV_TABLE: List[Action] = [
    repeat("volume.up", "turn the volume up a little (2 steps)", "KEY_VOLUP", 2, MAX_VOLUME_STEPS),
    repeat("volume.down", "turn the volume down a little (2 steps)", "KEY_VOLDOWN", 2, MAX_VOLUME_STEPS),
    keys("mute", "mute or unmute the sound (toggle)", "KEY_MUTE"),
    keys("play", "play or resume what is on screen", "KEY_PLAY"),
    keys("pause", "pause what is playing", "KEY_PAUSE"),
    keys("ff", "skip forward / fast forward / next", "KEY_FF"),
    keys("rewind", "rewind / skip back", "KEY_REWIND"),
    keys("back", "go back one screen", "KEY_RETURN"),
    keys("home", "go to the TV home screen", "KEY_HOME"),
    keys("ok", "press OK / select the highlighted item", "KEY_ENTER"),
    keys("up", "move the highlight up", "KEY_UP"),
    keys("down", "move the highlight down", "KEY_DOWN"),
    keys("left", "move the highlight left", "KEY_LEFT"),
    keys("right", "move the highlight right", "KEY_RIGHT"),
    keys("channel.up", "next channel", "KEY_CHUP"),
    keys("channel.down", "previous channel", "KEY_CHDOWN"),
    keys("source", "open the input / source picker", "KEY_SOURCE"),
    keys("menu", "open the TV settings menu", "KEY_MENU"),
    keys("info", "show info about what is playing", "KEY_INFO"),
] + [launch(app) for app in APPS if app.verified]

JEV_ACTIONS: Dict[str, Action] = {a.id: a for a in _JEV_TABLE}
JEV_CANDIDATES: List[Dict[str, str]] = (
    [{"id": a.id, "description": a.description} for a in _JEV_TABLE]
    + [{"id": "reobserve", "description": "the request is unclear; ask again"},
       {"id": "abstain", "description": "the request is not a TV control command; do nothing"}]
)
