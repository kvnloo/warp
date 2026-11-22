#!/usr/bin/env python3
"""Run `tizen package` with the keyring disabled and answer its password prompts.

Over ssh the Tizen CLI cannot unlock the desktop keyring that holds the profile
passwords and fails with "Invaild password". With DBus pointed nowhere it prompts
instead; this answers from files that never enter git or argv.

usage: tizen-pty-sign.py <profile> <out-dir> <src-dir>
env:   TIZEN_AUTHOR_PASS_FILE  path to the author certificate password (required)
       TIZEN_DISTRIBUTOR_PASS  defaults to the public SDK distributor password
"""
import os
import pty
import select
import sys
import time

profile = sys.argv[1]
out_dir, src_dir = (os.path.abspath(path) for path in sys.argv[2:4])
author = open(os.path.expanduser(os.environ["TIZEN_AUTHOR_PASS_FILE"])).read().strip()
distributor = os.environ.get("TIZEN_DISTRIBUTOR_PASS", "tizenpkcs12passfordsigner")
env = dict(os.environ, DBUS_SESSION_BUS_ADDRESS="unix:path=/nonexistent")
pid, fd = pty.fork()
if pid == 0:
    os.execvpe("tizen", ["tizen", "package", "-t", "wgt", "-s", profile, "-o", out_dir, "--", src_dir], env)
buf, log, start = "", [], time.time()
while time.time() - start < 180:
    ready, _, _ = select.select([fd], [], [], 1)
    if not ready:
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
        continue  # wait for the whole prompt; chunks can split "Save distributor1 password" from "(Y)/(N)?"
    if "(y), no: (n)" in low:
        os.write(fd, b"N\n")  # "Save ... password?" -> never store it
        buf = ""
    elif "author password" in low:
        os.write(fd, (author + "\n").encode())
        buf = ""
    elif "distributor" in low and "password" in low:
        os.write(fd, (distributor + "\n").encode())
        buf = ""
_, status = os.waitpid(pid, 0)
print("".join(log).replace(author, "***").replace(distributor, "***")[-1200:])
sys.exit(os.waitstatus_to_exitcode(status))
