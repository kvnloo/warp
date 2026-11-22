#!/usr/bin/env python3
"""tvlog_server.py: log endpoint for the native Warp app on the TV (the retail TV has no dlog).

  POST /log      one JSON object per request -> appended to LOG (with recv time + client ip)
  GET  /config   {"mockStreamUrl": ...} when a test clip exists in MEDIA (demo playback without Plex),
                 plus {"selftest": "..."} when DIR/selftest.txt exists (opt-in in-app key walk)
  GET  /media/X  static file with HTTP Range support (the TV's player seeks with Range requests)
  GET  /tail?n=  last n log lines (for the test driver)
  UDP  8797      "warp-log?" -> "warp-log http://HOST:PORT" (LAN discovery; nothing is hardcoded in the app)

Usage: tvlog_server.py --host 192.168.1.25 --port 8796 --dir /mnt/zer0models/work/warp-native

Relay mode (when the log host's firewall drops LAN traffic, e.g. groot's ufw): run the server on the log host,
forward a LAN port to it from another machine (ssh -N -L 0.0.0.0:8796:<loghost>:8796 <loghost>) and run
  tvlog_server.py --udp-only --port 8796
there. Discovery answers with the address of the interface that received the broadcast, so no IP is baked in.
"""
import argparse, json, os, re, socket, threading, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

ap = argparse.ArgumentParser()
ap.add_argument("--host", default="")
ap.add_argument("--udp-only", action="store_true")
ap.add_argument("--port", type=int, default=8796)
ap.add_argument("--udp", type=int, default=8797)
ap.add_argument("--dir", default="")
args = ap.parse_args()
LOG = os.path.join(args.dir, "tvlog.jsonl")
MEDIA = os.path.join(args.dir, "media")
BASE = f"http://{args.host}:{args.port}"
lock = threading.Lock()


class H(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, format, *a):  # noqa: A002 (quiet)
        pass

    def send(self, code, body=b"", ctype="application/json", extra=None):
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        for k, v in (extra or {}).items():
            self.send_header(k, v)
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(body)

    def do_POST(self):
        if self.path.split("?")[0] != "/log":
            return self.send(404)
        n = int(self.headers.get("Content-Length") or 0)
        raw = self.rfile.read(min(n, 65536)).decode("utf-8", "replace")
        try:
            obj = json.loads(raw)
        except Exception:
            obj = {"ev": "bad_json", "raw": raw[:500]}
        if not isinstance(obj, dict):
            obj = {"ev": "non_object", "raw": raw[:500]}
        obj["recv_ms"] = int(time.time() * 1000)
        obj["ip"] = str(self.client_address[0])
        with lock, open(LOG, "a") as f:
            f.write(json.dumps(obj, ensure_ascii=False) + "\n")
        self.send(204)

    def do_HEAD(self):
        self.do_GET()

    def do_GET(self):
        path = self.path.split("?")[0]
        if path == "/config":
            clips = sorted(c for c in os.listdir(MEDIA) if c.endswith((".mkv", ".mp4"))) if os.path.isdir(MEDIA) else []
            base = f"http://{self.headers.get('Host')}" if self.headers.get("Host") else BASE  # works through a relay
            cfg = {"mockStreamUrl": f"{base}/media/{clips[0]}" if clips else ""}
            st = os.path.join(args.dir, "selftest.txt")  # opt-in key script; delete the file to stop handing it out
            if os.path.isfile(st):
                cfg["selftest"] = open(st).read().strip()
            return self.send(200, json.dumps(cfg).encode())
        if path == "/tail":
            m = re.search(r"n=(\d+)", self.path)
            n = int(m.group(1)) if m else 50
            try:
                lines = open(LOG).read().splitlines()[-n:]
            except FileNotFoundError:
                lines = []
            return self.send(200, ("\n".join(lines) + "\n").encode(), "text/plain")
        if path.startswith("/media/"):
            name = os.path.basename(path[7:])
            fp = os.path.join(MEDIA, name)
            if not name or not os.path.isfile(fp):
                return self.send(404)
            size = os.path.getsize(fp)
            start, end = 0, size - 1
            rng = self.headers.get("Range")
            m = re.match(r"bytes=(\d*)-(\d*)", rng or "")
            if m:
                if m.group(1):
                    start = int(m.group(1))
                    end = int(m.group(2)) if m.group(2) else size - 1
                else:
                    start = size - int(m.group(2))
                end = min(end, size - 1)
            length = end - start + 1
            ctype = "video/x-matroska" if name.endswith(".mkv") else "video/mp4"
            self.send_response(206 if m else 200)
            self.send_header("Content-Type", ctype)
            self.send_header("Accept-Ranges", "bytes")
            self.send_header("Content-Length", str(length))
            if m:
                self.send_header("Content-Range", f"bytes {start}-{end}/{size}")
            self.end_headers()
            if self.command == "HEAD":
                return
            with open(fp, "rb") as f:
                f.seek(start)
                left = length
                try:
                    while left > 0:
                        chunk = f.read(min(1 << 20, left))
                        if not chunk:
                            break
                        self.wfile.write(chunk)
                        left -= len(chunk)
                except (BrokenPipeError, ConnectionResetError):
                    pass
            note({"ev": "media_get", "name": name, "range": rng, "ip": self.client_address[0], "recv_ms": int(time.time() * 1000)})
            return
        if path == "/":
            return self.send(200, b"warp tvlog ok\n", "text/plain")
        self.send(404)


def local_ip_for(peer):
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        s.connect((peer, 9))
        return s.getsockname()[0]
    finally:
        s.close()


def note(obj):
    if not args.dir:
        print(json.dumps(obj), flush=True)
        return
    with lock, open(LOG, "a") as f:
        f.write(json.dumps(obj) + "\n")


def udp():
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    s.bind(("", args.udp))
    while True:
        data, addr = s.recvfrom(256)
        if data.strip() == b"warp-log?":
            url = BASE if args.host else f"http://{local_ip_for(addr[0])}:{args.port}"
            s.sendto(f"warp-log {url}".encode(), addr)
            note({"ev": "discovery_answered", "ip": addr[0], "url": url, "recv_ms": int(time.time() * 1000)})


if args.udp_only:
    print(f"warp tvlog discovery on udp {args.udp} -> this host:{args.port}", flush=True)
    udp()
    raise SystemExit
if not args.host or not args.dir:
    ap.error("--host and --dir are required unless --udp-only")
os.makedirs(args.dir, exist_ok=True)
threading.Thread(target=udp, daemon=True).start()
print(f"warp tvlog on {BASE} (udp {args.udp}), log {LOG}", flush=True)
ThreadingHTTPServer((args.host, args.port), H).serve_forever()
