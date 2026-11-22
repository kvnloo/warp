"""Tier 1: instant local rules. No network, no model, microseconds.

`parse(text)` returns a `Match` (an action plus a confidence) or None when the
rules do not recognise the command. A rule hit is confidence 1.0; a fuzzy hit
(a misheard word, "mutte", "jelyfin") carries the similarity ratio so the
controller can hand weak matches to Jev instead of guessing.
"""
from __future__ import annotations

import difflib
import re
from dataclasses import dataclass
from typing import Optional

from . import actions as A
from .actions import Action

_NUMBER_WORDS = {
    "zero": 0, "one": 1, "two": 2, "to": None, "too": None, "three": 3, "four": 4, "for": None,
    "five": 5, "six": 6, "seven": 7, "eight": 8, "nine": 9, "ten": 10, "eleven": 11,
    "twelve": 12, "thirteen": 13, "fourteen": 14, "fifteen": 15, "sixteen": 16,
    "seventeen": 17, "eighteen": 18, "nineteen": 19, "twenty": 20, "thirty": 30,
}
_FILLER_PREFIX = re.compile(
    r"^(?:(?:hey|hi|ok|okay|yo|so|um+|uh+|er+|jev|tv|please|can you|could you|would you|will you|"
    r"i want to|i wanna|i'd like to|id like to|let's|lets|go ahead and|just|now)\s+)+")
_FILLER_ANY = re.compile(
    r"\b(?:please|for me|thanks|thank you|right now|real quick|quickly|um+|uh+|kinda|like|"
    r"the tv|on the tv|on tv|the television|television|the telly)\b")

_AMOUNT_WORDS = (
    (re.compile(r"\b(?:a (?:little|bit|tad|touch|smidge)|a little bit|slightly|a notch|one notch)\b"), 2),
    (re.compile(r"\b(?:a couple(?: of)?(?: notches| steps| times)?)\b"), 2),
    (re.compile(r"\b(?:a few(?: notches| steps| times)?)\b"), 3),
    (re.compile(r"\b(?:a lot|way|loads|much|a bunch)\b"), 8),
)
_VOL_UP = r"(?:up|louder|higher|raise|increase|boost|crank|pump)"
_VOL_DOWN = r"(?:down|quieter|softer|lower|decrease|reduce|less loud)"
_ARROW = {"up": "KEY_UP", "down": "KEY_DOWN", "left": "KEY_LEFT", "right": "KEY_RIGHT"}
_KEYWORDS = {  # single-word commands used for typo-tolerant fuzzy matching
    "mute": lambda: A.keys("mute", "mute (toggle)", "KEY_MUTE"),
    "unmute": lambda: A.keys("mute", "unmute (toggle)", "KEY_MUTE"),
    "pause": lambda: A.keys("pause", "pause", "KEY_PAUSE"),
    "play": lambda: A.keys("play", "play", "KEY_PLAY"),
    "resume": lambda: A.keys("play", "play", "KEY_PLAY"),
    "back": lambda: A.keys("back", "back", "KEY_RETURN"),
    "home": lambda: A.keys("home", "home", "KEY_HOME"),
    "louder": lambda: A.repeat("volume.up", "volume up", "KEY_VOLUP", 2, A.MAX_VOLUME_STEPS),
    "quieter": lambda: A.repeat("volume.down", "volume down", "KEY_VOLDOWN", 2, A.MAX_VOLUME_STEPS),
    "select": lambda: A.keys("ok", "ok", "KEY_ENTER"),
    "rewind": lambda: A.keys("rewind", "rewind", "KEY_REWIND"),
}


@dataclass(frozen=True)
class Match:
    action: Action
    confidence: float
    rule: str


def normalize(text: str) -> str:
    t = text.lower().strip()
    t = t.replace("’", "'")
    t = re.sub(r"[^a-z0-9+'\- ]+", " ", t)
    t = t.replace("-", " ")
    t = re.sub(r"\s+", " ", t).strip()
    whole = t
    t = _FILLER_PREFIX.sub("", t)
    t = _FILLER_ANY.sub(" ", t)
    t = re.sub(r"\s+", " ", t).strip()
    if not t and whole in {"ok", "okay"}:
        return "ok"
    return t


def _to_int(token: str) -> Optional[int]:
    if token.isdigit():
        return int(token)
    return _NUMBER_WORDS.get(token)


def _number(t: str) -> Optional[int]:
    m = re.search(r"\b(twenty|thirty)[ ]?(one|two|three|four|five|six|seven|eight|nine)\b", t)
    if m:
        return _NUMBER_WORDS[m.group(1)] + _NUMBER_WORDS[m.group(2)]
    for token in t.split():
        value = _to_int(token)
        if value is not None:
            return value
    return None


def _amount(t: str, default: int) -> int:
    m = re.search(r"\bby (\w+)\b", t)
    if m and _to_int(m.group(1)) is not None:
        return _to_int(m.group(1))  # type: ignore[return-value]
    n = _number(t)
    if n is not None:
        return n
    for pattern, value in _AMOUNT_WORDS:
        if pattern.search(t):
            return value
    return default


def _app_from(name: str) -> Optional[tuple]:
    name = re.sub(r"\b(?:the|app|application|channel|please)\b", " ", name)
    name = re.sub(r"\s+", " ", name).strip()
    if not name:
        return None
    best, best_score = None, 0.0
    for app in A.APPS:
        for alias in app.aliases:
            score = 1.0 if name == alias else difflib.SequenceMatcher(None, name, alias).ratio()
            if score > best_score:
                best, best_score = app, score
    if best is None or best_score < 0.6:
        return None
    return best, best_score


def _vol(direction: str, t: str) -> Match:
    n = _amount(t, 2)
    if direction == "up":
        return Match(A.repeat("volume.up", f"volume up {n}", "KEY_VOLUP", n, A.MAX_VOLUME_STEPS), 1.0, "volume")
    return Match(A.repeat("volume.down", f"volume down {n}", "KEY_VOLDOWN", n, A.MAX_VOLUME_STEPS), 1.0, "volume")


def parse(text: str) -> Optional[Match]:
    if not isinstance(text, str):
        return None
    t = normalize(text)
    if not t or len(t) > 200:
        return None

    # --- sound -----------------------------------------------------------------
    if re.fullmatch(r"(?:un ?mute|sound on|turn (?:the )?(?:sound|audio|volume) (?:back )?on|"
                    r"(?:bring|turn) (?:the )?(?:sound|audio) back(?: on)?)(?: it)?", t):
        return Match(A.keys("mute", "unmute (toggle)", "KEY_MUTE"), 1.0, "unmute")
    if re.fullmatch(r"(?:mute|silence|shut up|quiet|hush|sound off|kill the (?:sound|audio)|"
                    r"turn (?:off )?(?:the )?(?:sound|audio|volume) off|turn off (?:the )?(?:sound|audio|volume))"
                    r"(?: it| that| the (?:sound|audio|volume)| sound| audio)?", t):
        return Match(A.keys("mute", "mute (toggle)", "KEY_MUTE"), 1.0, "mute")
    if re.search(r"\bset (?:the )?volume to\b|\bvolume (?:to|at) \d", t):
        return None  # absolute volume needs the current level; not supported, never guess
    if "channel" not in t:
        if re.search(r"\b(?:too loud|hurts|lower it)\b", t):
            return _vol("down", t)
        if re.search(r"\b(?:can't hear|cant hear|can not hear|cannot hear|too quiet)\b", t):
            return _vol("up", t)
        up = bool(re.search(rf"\b(?:volume|sound|audio)\b.*\b{_VOL_UP}\b|\b{_VOL_UP}\b.*\b(?:volume|sound|audio)\b", t)
                  or re.search(r"\b(?:turn|crank|bump|pump|kick|jack) (?:it |this |that )?(?:\w+ )?up\b", t)
                  or re.search(r"\blouder\b", t))
        down = bool(re.search(rf"\b(?:volume|sound|audio)\b.*\b{_VOL_DOWN}\b|\b{_VOL_DOWN}\b.*\b(?:volume|sound|audio)\b", t)
                    or re.search(r"\b(?:turn|bring|knock|dial) (?:it |this |that )?(?:\w+ )?down\b", t)
                    or re.search(r"\b(?:quieter|softer)\b", t))
        if (up and re.search(rf"\b{_VOL_DOWN}\b", t)) or (down and re.search(rf"\b{_VOL_UP}\b", t)):
            up = down = True
        if up and not down:
            return _vol("up", t)
        if down and not up:
            return _vol("down", t)
        if up and down:
            return None  # "turn it up, no down" - contradictory, let a person repeat it

    # --- power -----------------------------------------------------------------
    if re.fullmatch(r"(?:turn|switch|shut|power) (?:it |this |the tv )?(?:off|down)|(?:turn|switch|shut|power) off"
                    r"(?: it| this)?|power off|off|tv off|good ?night|shut it down|go to sleep", t):
        return Match(A.keys("power.off", "turn the TV off", "KEY_POWER"), 1.0, "power")
    if re.fullmatch(r"(?:turn|switch|power) (?:it )?on|power on|turn on|wake up", t):
        return None  # an off TV has no network remote; waking needs Wake-on-LAN (not built)

    # --- inputs ----------------------------------------------------------------
    m = re.fullmatch(r"(?:(?:switch|change|go|set|put it) (?:to |on )?)?(?:input |source )?h ?d ?m ?i ?(\w+)?(?: input| port)?", t)
    if m:
        n = _to_int(m.group(1)) if m.group(1) else None
        if n is None:
            return Match(A.keys("source", "open the input picker", "KEY_SOURCE"), 1.0, "input")
        if 1 <= n <= 4:
            return Match(A.keys(f"hdmi.{n}", f"switch to HDMI {n}", f"KEY_HDMI{n}"), 1.0, "hdmi")
        return None
    if re.fullmatch(r"(?:change |switch |show |open )?(?:the )?(?:input|inputs|source|sources)(?: menu| list)?", t):
        return Match(A.keys("source", "open the input picker", "KEY_SOURCE"), 1.0, "input")

    # --- channel ---------------------------------------------------------------
    if re.search(r"\bchannel up\b|\bnext channel\b|\bup a channel\b|\bchannel plus\b", t):
        return Match(A.keys("channel.up", "next channel", "KEY_CHUP"), 1.0, "channel")
    if re.search(r"\bchannel down\b|\bprevious channel\b|\blast channel\b|\bdown a channel\b|\bchannel minus\b", t):
        return Match(A.keys("channel.down", "previous channel", "KEY_CHDOWN"), 1.0, "channel")
    m = re.fullmatch(r"(?:(?:go|switch|change|turn|put it) (?:to |on )?)?channel (\d{1,4})(?: (?:dash|point|dot) ?(\d{1,2})| (\d{1,2}))?", t)
    if m:
        digits = m.group(1) + ((m.group(2) or m.group(3)) and "-" + (m.group(2) or m.group(3)) or "")
        seq = tuple("KEY_PLUS100" if d == "-" else f"KEY_{d}" for d in digits) + ("KEY_ENTER",)
        return Match(A.Action(f"channel.{digits}", "key", f"tune to channel {digits}", seq), 1.0, "channel")

    # --- apps --------------------------------------------------------------------
    m = re.fullmatch(r"(?:open|launch|start|run|go to|switch to|put on|bring up|load|fire up|watch|"
                     r"go on|pull up|throw on) (?:up )?(?:the )?(.+?)(?: app)?", t)
    if m:
        hit = _app_from(m.group(1))
        if hit:
            app, score = hit
            return Match(A.launch(app), round(score, 3), "app")

    # --- search ----------------------------------------------------------------
    m = re.fullmatch(r"(?:search|look up|find|search for|look for)(?: for)? (.+)", t)
    if m:
        query = m.group(1).strip()
        return Match(A.Action("search", "text", "type a search query", text=query), 1.0, "search")

    # --- playback --------------------------------------------------------------
    if re.fullmatch(r"(?:un ?pause|resume|continue|keep (?:going|playing)|play(?: it)?(?: again)?|start (?:it|playing)|go)", t):
        return Match(A.keys("play", "play", "KEY_PLAY"), 1.0, "play")
    if re.fullmatch(r"(?:pause|pause it|pause (?:this|that|the (?:show|movie|video))|hold on|wait|stop|stop it|freeze|hang on)", t):
        return Match(A.keys("pause", "pause", "KEY_PAUSE"), 1.0, "pause")
    if re.fullmatch(r"(?:next|skip|skip (?:it|this|ahead|forward)|next (?:one|episode|track|song)|fast ?forward|go forward)", t):
        return Match(A.keys("ff", "skip forward", "KEY_FF"), 1.0, "next")
    if re.fullmatch(r"(?:rewind|skip back|go back a bit|back up a bit|previous (?:episode|track|song))", t):
        return Match(A.keys("rewind", "rewind", "KEY_REWIND"), 1.0, "rewind")

    # --- navigation ------------------------------------------------------------
    if re.fullmatch(r"(?:go )?back|return|previous screen|go back(?: a screen| one)?|exit|close (?:this|that|it)", t):
        return Match(A.keys("back", "back", "KEY_RETURN"), 1.0, "back")
    if re.fullmatch(r"(?:go )?home|home screen|go to (?:the )?home(?: screen)?|main menu|smart hub", t):
        return Match(A.keys("home", "home", "KEY_HOME"), 1.0, "home")
    if re.fullmatch(r"ok|okay|enter|select(?: (?:it|this|that))?|confirm|click(?: it)?|press ok|choose (?:it|this|that)|yes", t):
        return Match(A.keys("ok", "ok", "KEY_ENTER"), 1.0, "ok")
    m = re.fullmatch(r"(?:(?:go|move|press|scroll|arrow|cursor|navigate) )?(up|down|left|right)(?: (\w+))?(?: times)?", t)
    if m:
        n = _to_int(m.group(2)) if m.group(2) else 1
        if n is not None:
            d = m.group(1)
            return Match(A.repeat(d, f"move {d} {n}", _ARROW[d], n, A.MAX_ARROW_STEPS), 1.0, "arrow")
    if re.fullmatch(r"(?:open |show |go to )?(?:the )?(?:settings|menu|settings menu)", t):
        return Match(A.keys("menu", "settings menu", "KEY_MENU"), 1.0, "menu")
    if re.fullmatch(r"(?:show )?(?:info|information|what's on|whats on|details)", t):
        return Match(A.keys("info", "info", "KEY_INFO"), 1.0, "info")
    if re.fullmatch(r"(?:open |show )?(?:the )?guide", t):
        return Match(A.keys("guide", "guide", "KEY_GUIDE"), 1.0, "guide")

    # --- fuzzy single word -----------------------------------------------------
    if " " not in t:
        close = difflib.get_close_matches(t, list(_KEYWORDS), n=1, cutoff=0.7)
        if close:
            score = difflib.SequenceMatcher(None, t, close[0]).ratio()
            return Match(_KEYWORDS[close[0]](), round(score, 3), "fuzzy")
    return None
