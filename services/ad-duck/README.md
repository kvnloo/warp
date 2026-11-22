# ad-duck

Turns the TV down when an ad break starts and back up when the show returns,
by listening with a microphone in the room. It is built so that it **cannot
understand speech and never keeps audio**.

## Privacy, in plain language

* **Nothing is recorded.** Sound from the mic goes into a small piece of
  memory that holds half a second and is overwritten by the next half second.
  It is never written to disk, never sent over the network, never logged.
* **Only one function ever looks at the sound** (`adduck/features.py:
  extract_features`). Every half second it boils the sound down to about a
  dozen rounded numbers: how loud it was, how "squashed" it was (ads are
  compressed), whether it was silent, and how much energy fell into 8 broad
  pitch ranges. Everything else in the program only ever sees those numbers.
* **Those numbers can't hold words.** Speech has 4-5 syllables per second;
  we keep 2 snapshots per second, and 8 broad pitch ranges are far too coarse
  to tell one vowel from another. We tested this with a real speech
  recogniser (below): it reads the original sentence perfectly, and gets
  *nothing at all* from sound rebuilt out of our numbers.
* **No speech recognition, no transcripts, no audio fingerprints, no cloud.**
  The detector is a handful of rules (plus an optional tiny model that is
  trained only on the numbers, never on audio).

Do not shrink the 0.5 s block: the gate below shows the same 8 bands at 50
snapshots/s *are* intelligible. The privacy comes from the slow rate.

## How it decides

Per 0.5 s record (`adduck/detect.py`, structure after AdHush, see NOTICE.md):

| cue | why |
|---|---|
| loudness jump vs a slow programme baseline (baseline freezes during a jump) | ads are mixed hotter |
| crest-factor drop | ads are heavily compressed; a loud action scene is not |
| short silences | spots are separated by gaps |
| 15/30/45/60 s grid between gaps | spots are sold in fixed units; holds a duck mid-pod |
| optional break-clock prior | breaks recur at a channel's interval |

Neither loudness nor crest alone reaches the duck threshold. A hysteresis
state machine (`adduck/state.py`) ducks after 2 s of agreement and restores
after 1 s - restoring is deliberately faster, because the show staying quiet
is worse than an ad being loud.

While ducked, the mic hears the TV quieter. The detector measures that drop
and adds it back, and checks the last second (not just the last 3 s) so the
volume returns quickly. If the ducked TV is buried in room noise, the ducker
raises the ducked level a few steps at a time until the detector can hear it.

## TV safety rules (`adduck/tv.py`)

* Duck to a low volume (default 3), never full mute, never *raise* volume.
* Restore exactly the volume that was there before.
* If anyone changes the volume while ducked, that wins: cancel, keep their
  volume, and do not duck again for 10 minutes.
* Hard ceiling: never stay ducked longer than 4 minutes.
* Any error (detector or TV) -> restore. Failed restores are retried every
  half second. Stopping the program restores.

TV control is an interface: `get_volume()`, `duck(level)`, `restore()`.
`FakeTV` and `DryRunTV` are included; the Samsung backend is being written on
branch `feat/tv-voice` and only has to implement those three calls.

## Run

```sh
pip install numpy pytest            # core needs only numpy
python -m pytest                    # unit tests + static privacy gate
python tools/gate_static.py         # gate (a)
python -m evaluation.run_eval       # synthetic precision/recall (held-out seeds)
pip install sounddevice && python -m adduck   # dry run on the default mic
```

## Proof gates

**(a) Static** - `tools/gate_static.py` parses every module and fails if the
audio-path modules (`features`, `ringbuffer`, `capture`) import anything off an
allowlist or call any writer (open/print/np.save/tofile/write/send/logging/...),
if any audio-path public function could return something other than
`Features`, if `extract_features`/`RingBuffer` are referenced anywhere else, or
if any module imports network/serialisation libraries. Tests also feed it
deliberately leaky code to prove it catches each case.

**(b) Words** - `tools/gate_words.py CLIP.wav --model DIR --reference TEXT`.
A sentence is spoken by macOS `say`, the service's own feature path turns it
into 2 Hz records, audio is rebuilt from the records alone (each band envelope
drives band-limited noise), and the rebuilt audio and original are both fed to
a local CPU recogniser (faster-whisper, int8). The clip is deleted afterwards;
the rebuilt audio is never written.

Results (2026-11-22, groot CPU):

| recogniser | clip | original (control) | rebuilt from 2 Hz features | same bands at 50 Hz (attack sanity) |
|---|---|---|---|---|
| faster-whisper base | "The quick brown fox ... before breakfast" (Samantha) | 14/15 words | `""` 0/15 | 9/15 |
| faster-whisper small.en | same | 15/15 | `""` 0/15 | 15/15 |
| faster-whisper small.en | "Please remember to water the tomato plants ..." (Daniel) | 15/15 | `""` 0/15 | 15/15 |

The 50 Hz column matters: the rebuild method is a strong attack (it recovers
every word when envelopes are fast), so the empty result at 2 Hz is due to the
feature rate, not a weak attack.

## Synthetic evaluation

`evaluation/` renders a programme (speech-like audio, music beds, loud action
scenes, scene-cut silences) with ad pods of 3-6 compressed spots on a 15/30 s
grid, mixes in room noise, and runs the full closed loop - the simulated mic
hears the TV at whatever volume the ducker set. Synthetic audio is generated
in RAM; no recordings. Tuned on seeds 0-2, reported on held-out seeds
100-104 (20 pods per scenario):

| scenario | duck P | duck R | restore P | restore R | restore offset med/max |
|---|---|---|---|---|---|
| loud ads (+5..8 dB) | 0.87 | 1.00 | 0.87 | 1.00 | +1.5 / +3.5 s |
| moderate ads (+2.5..4.5 dB) | 0.87 | 1.00 | 0.87 | 1.00 | +1.5 / +3.1 s |
| TV night mode, ads not louder (0..1.5 dB) | 0.70 | 0.35 | 0.70 | 0.35 | +1.8 / +3.0 s |
| noisy room (-48 dBFS noise) | 0.80 | 1.00 | 0.72 | 0.90 | +2.3 / +3.6 s |
| **all** | **0.83** | **0.84** | **0.80** | **0.81** | +1.7 / +3.6 s |

Duck hit = within 12 s after the pod starts; restore hit = within 4 s
before/after it ends. This is synthetic data; real-room numbers will differ.
Weakest case: when the TV's own night/DRC mode removes the loudness jump,
only compression and gaps remain and most pods are missed.

## Android port (Lenovo tablet) - network rule

The tablet build must talk to **exactly one host: the TV at 192.168.1.28**
(Samsung remote-control ports, 8001/8002). No other LAN host, no internet, no
analytics, no crash reporting. Concretely: no `INTERNET`-using libraries other
than the TV client; capture with `AudioRecord` into a fixed array; port only
`features`/`detect`/`state`/`tv`.

How to verify:

1. Router: give the tablet a fixed lease, then watch its traffic (e.g. on an
   OpenWrt/Linux router: `tcpdump -n -i br-lan host <tablet-ip> and not host 192.168.1.28`)
   while ads play for an hour - it should print nothing (DHCP/ARP aside).
2. On the tablet: Settings -> Apps -> ad-duck -> Mobile data & Wi-Fi shows
   data usage only toward the TV; or run it with a local VPN firewall (e.g.
   NetGuard) allowing only 192.168.1.28 and check the blocked-connection log
   stays empty.
3. `adb shell dumpsys netstats detail` / `adb shell cat /proc/net/tcp6` to
   list open sockets: only 192.168.1.28 should appear.
4. Storage: `adb shell run-as <pkg> ls -R` should show no audio files.

## Layout

```
adduck/features.py    the only code that reads samples
adduck/ringbuffer.py  fixed in-RAM block buffer -> Features
adduck/capture.py     optional sounddevice mic -> Features
adduck/detect.py      rule cues + fusion over Features
adduck/state.py       hysteresis state machine
adduck/tv.py          TV interface, FakeTV, DryRunTV, safe Ducker
adduck/engine.py      wiring; any error restores
adduck/nn.py          optional numpy logistic model over Features
tools/gate_static.py  gate (a)
tools/gate_words.py   gate (b)
evaluation/           synthetic closed-loop eval
```
