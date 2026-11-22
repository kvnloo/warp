# tv-voice

Say a sentence, the living-room TV does it. TV-only: this never talks to Hermes, z0live or any agent session. It turns one command into Samsung remote keys or an app launch and nothing else.

```
text ──► tier 1: local rules (µs) ──hit──► action ──► Samsung network remote (wss :8002)
              │ unsure
              ▼
         tier 2: Jev picks one id from a fixed TV-action table (~0.5 s)
              │ below 0.65, reobserve/abstain, error, no key, private text
              ▼
         no action (fail closed)
```

## Run

```bash
cd services/tv-voice
python3 -m venv .venv && .venv/bin/pip install -e '.[test]'   # samsungtvws, websocket-client, requests
.venv/bin/python -m pytest -q

.venv/bin/tvctl --dry-run say "turn it up 5"   # shows what would be sent, touches nothing
.venv/bin/tvctl parse "open jellyfin"          # rules tier only
.venv/bin/tvctl info                           # read-only GET http://TV:8001/api/v2/
.venv/bin/tvctl say "turn it up 5"             # really sends
TV_VOICE_HTTP_TOKEN=... .venv/bin/tvctl serve --bind 0.0.0.0   # POST :8765/command {"text": "..."}
curl -s -H 'X-TV-Voice-Token: ...' -d '{"text":"pause"}' http://groot:8765/command
```

The first real `say` makes the TV show an **Allow** prompt for `tv-voice`. Accept it with the physical remote. The token is saved 0600 at `~/.config/tv-voice/token` (dir 0700). The client name must stay `tv-voice`, because the token is bound to it.

| env | default | |
|---|---|---|
| `TV_VOICE_HOST` | `192.168.1.28` | DHCP-reserved QN85B |
| `TV_VOICE_PORT` | `8002` | wss |
| `TV_VOICE_DRY_RUN` | unset | `1` = `--dry-run` |
| `TV_VOICE_HTTP_TOKEN` | unset | require `X-TV-Voice-Token` on `/command` |
| `TV_VOICE_JEVKIT_DIR` | `~/.hermes/plugins/hermes-jev` | where `jevkit/` lives |
| `TV_VOICE_ALLOW_UNVERIFIED_APPS` | unset | `1` = allow launching app ids not checked on this TV |
| `JEV_MIN_CONFIDENCE` | 0.65 | jevkit's floor. tv-voice never acts below 0.65 whatever it says |

## What it understands (tier 1)

Volume up/down with N, number words, "a little", "a lot" (capped at 20 steps). Mute/unmute (one toggle key). Play, pause, skip/next, rewind. Back, home, OK, arrows with N (capped at 10). Open/launch/watch <app> with fuzzy names. Channel up/down/N. HDMI 1–4 and the source picker. Power off. Settings, info, guide. "search X" types X into the focused TV text field. Filler words ("hey jev", "please", "can you", "the tv") are stripped.

Deliberately not handled: "set volume to 20" (the current level is unknown, so it would be a guess) and power on (an off TV has no network remote; that needs Wake-on-LAN with the TV MAC).

## Jev tier

Jev gets only `jev.action_choice_request_v1` with the spoken command as the goal and the 21 parameterless actions in `tv_voice/actions.py` (`JEV_CANDIDATES`) plus `reobserve`/`abstain`. Power off is not in the table. Before anything is sent, the command must pass `jevkit.privacy.is_sensitive` and `redact` must leave it unchanged (so no email, phone or card number). Otherwise it stays local and nothing happens.

## Privacy and logs

The stderr log line is `{tier, action, ok, latency_ms, dry_run}`. No words, no audio. The last 20 commands live in an in-memory ring (`Controller.recent`). `--debug` adds the text and detail to the log line.

## Apps

| app | id | status |
|---|---|---|
| Warp | `WarpTV2022.Warp` | verified (our widget) |
| Jellyfin | `AprZAARz4r.Jellyfin` | verified |
| Netflix, YouTube, Prime Video, Disney+, Spotify, Hulu | see `actions.py` | **unverified** on this TV; refused unless `TV_VOICE_ALLOW_UNVERIFIED_APPS=1` |

Apps launch over the same WebSocket (`ed.apps.launch`, samsungtvws `run_app`), not the REST `POST /api/v2/applications/<id>`.

## Plugging in speech

Speech is push-to-talk and stays outside this package. Anything that turns audio into text can drive it:

1. **HTTP:** the ASR process POSTs `{"text": transcript}` to `/command`.
2. **In-process:** `Controller(TvRemote()).handle(transcript)`.

A local ASR (for example whisper.cpp `tiny.en`/`base.en`, or Vosk with a small grammar built from the phrases in `tests/test_intent.py`) records only while the button is held, transcribes in memory, posts the text and discards the audio. No model is bundled or downloaded here. On groot (8 GB RAM, no GPU) a tiny/base CPU model is the right size.

The physical remote's mic button is owned by Bixby on Tizen. A third-party app normally cannot register it. Push-to-talk will probably come from a phone shortcut or a button on groot rather than the TV remote. This is not verified on the QN85B yet.

## License

GPL-3.0-only, like the rest of Warp. `samsungtvws` is LGPL-3.0 (its package metadata, not MIT). It is used unmodified as a pip dependency and is not vendored.
