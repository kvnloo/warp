#!/usr/bin/env python3
"""Re-sign a .tpk with the Samsung TV author cert using an ISOLATED Tizen CLI copy (never the global
~/tizen-studio-data profiles). Answers the password prompts in a pty; passwords come from files, never argv/git.

usage: sign_tpk_isolated.py <tpk> <isolated-sdk-dir> [profile]
  <isolated-sdk-dir> holds tizen-sdk/ (sdk.info -> its own tizen-data/) and tizen-data/profile/profiles.xml
env: TIZEN_AUTHOR_PASS_FILE (default ~/src/jellyfin-tizen-sideload/cert/author.pass)
"""
import os, pty, select, sys, time

tpk = os.path.abspath(sys.argv[1])
sdk = os.path.abspath(sys.argv[2])
profile = sys.argv[3] if len(sys.argv) > 3 else "JellyfinTV"
author = open(os.path.expanduser(os.environ.get("TIZEN_AUTHOR_PASS_FILE", "~/src/jellyfin-tizen-sideload/cert/author.pass"))).read().strip()
distributor = "tizenpkcs12passfordsigner"  # public SDK distributor cert password (documented by Samsung)
bin_dir = os.path.join(sdk, "tizen-sdk", "tools", "ide", "bin")
env = dict(os.environ, DBUS_SESSION_BUS_ADDRESS="unix:path=/nonexistent", PATH=bin_dir + ":" + os.environ["PATH"])
pid, fd = pty.fork()
if pid == 0:
    os.chdir(os.path.dirname(tpk))
    os.execvpe(os.path.join(bin_dir, "tizen"), ["tizen", "package", "-t", "tpk", "-s", profile, "--", tpk], env)
buf, log, t0 = "", [], time.time()
while time.time() - t0 < 180:
    r, _, _ = select.select([fd], [], [], 1)
    if not r:
        continue
    try:
        chunk = os.read(fd, 4096).decode(errors="replace")
    except OSError:
        break
    if not chunk:
        break
    buf += chunk
    log.append(chunk)
    low = buf.lower().rstrip()
    if not low.endswith((":", "?")):
        continue
    if "(y), no: (n)" in low:
        os.write(fd, b"N\n")
        buf = ""
    elif "author password" in low:
        os.write(fd, (author + "\n").encode())
        buf = ""
    elif "distributor" in low and "password" in low:
        os.write(fd, (distributor + "\n").encode())
        buf = ""
_, status = os.waitpid(pid, 0)
print("".join(log).replace(author, "***")[-1500:])
sys.exit(os.waitstatus_to_exitcode(status))
