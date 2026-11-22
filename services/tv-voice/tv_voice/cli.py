"""tvctl - control the living-room TV with a sentence.

  tvctl say "turn it up 5"            # parse, then send to the TV
  tvctl --dry-run say "open jellyfin" # parse and show what would be sent
  tvctl parse "mute"                  # rules tier only, no Jev, no TV
  tvctl serve --host 0.0.0.0          # POST /command {"text": ...} on the LAN
  tvctl info                          # read-only GET of the TV's device info
"""
from __future__ import annotations

import argparse
import json
import os
import sys

from . import intent
from .controller import Controller, setup_logging
from .remote import DEFAULT_HOST, DEFAULT_PORT, DryRunRemote, TvRemote, device_info


def _controller(args) -> Controller:
    remote = DryRunRemote() if args.dry_run else TvRemote(args.host, args.port)
    return Controller(remote, dry_run=args.dry_run, debug=args.debug, use_jev=not args.no_jev)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(prog="tvctl", description="TV-only voice/text controller")
    parser.add_argument("--host", default=os.environ.get("TV_VOICE_HOST", DEFAULT_HOST))
    parser.add_argument("--port", type=int, default=int(os.environ.get("TV_VOICE_PORT", DEFAULT_PORT)))
    parser.add_argument("--dry-run", action="store_true", default=os.environ.get("TV_VOICE_DRY_RUN") == "1")
    parser.add_argument("--no-jev", action="store_true", help="rules tier only")
    parser.add_argument("--debug", action="store_true", help="include the command text in logs")
    sub = parser.add_subparsers(dest="cmd", required=True)
    say = sub.add_parser("say")
    say.add_argument("text", nargs="+")
    parse = sub.add_parser("parse")
    parse.add_argument("text", nargs="+")
    serve = sub.add_parser("serve")
    serve.add_argument("--bind", default=os.environ.get("TV_VOICE_BIND", "127.0.0.1"))
    serve.add_argument("--http-port", type=int, default=int(os.environ.get("TV_VOICE_HTTP_PORT", 8765)))
    sub.add_parser("info")
    args = parser.parse_args(argv)
    setup_logging(args.debug)

    if args.cmd == "parse":
        match = intent.parse(" ".join(args.text))
        print(json.dumps({"action": match.action.id, "sends": match.action.summary(),
                          "confidence": match.confidence, "rule": match.rule} if match else {"action": "none"}))
        return 0 if match else 1
    if args.cmd == "info":
        info = device_info(args.host)
        device = info.get("device", {})
        print(json.dumps({k: device.get(k) for k in ("name", "modelName", "OS", "PowerState", "TokenAuthSupport")}
                         | {"latency_ms": info["_latency_ms"]}))
        return 0
    controller = _controller(args)
    if args.cmd == "say":
        result = controller.handle(" ".join(args.text))
        print(json.dumps(result.to_dict()))
        controller.remote.close()
        return 0 if result.ok else 1
    from .server import make_server

    server = make_server(controller, args.bind, args.http_port)
    print(f"tv-voice listening on http://{args.bind}:{args.http_port}/command "
          f"({'dry run' if args.dry_run else 'TV ' + args.host})", file=sys.stderr)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        controller.remote.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
