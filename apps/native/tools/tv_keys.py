#!/usr/bin/env python3
"""Send remote-control keys to the Samsung TV over its remote socket (wss://TV:8002), for latency walks.

usage: tv_keys.py <tv-ip> <gap-seconds> KEY_RIGHT KEY_ENTER ... (or "sleep:2.5")
The pairing token is read from stdin (first line) so it never lands in argv, logs or git. A token is bound to
the client name it was issued for; set TV_CLIENT_NAME to that name (default "z0live", the household's paired client).
Prints one JSON line per key with the local send time (ms since epoch).
"""
import asyncio, base64, json, os, ssl, sys, time
import websockets

ip, gap, keys = sys.argv[1], float(sys.argv[2]), sys.argv[3:]
token = sys.stdin.readline().strip()


async def main():
    name = base64.b64encode(os.environ.get("TV_CLIENT_NAME", "z0live").encode()).decode()
    url = f"wss://{ip}:8002/api/v2/channels/samsung.remote.control?name={name}&token={token}"
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    async with websockets.connect(url, ssl=ctx, open_timeout=8, max_size=None) as ws:
        hello = json.loads(await asyncio.wait_for(ws.recv(), 8))
        print(json.dumps({"connected": hello.get("event")}), flush=True)
        for k in keys:
            if k.startswith("sleep:"):
                await asyncio.sleep(float(k[6:]))
                continue
            await ws.send(json.dumps({"method": "ms.remote.control", "params": {"Cmd": "Click", "DataOfCmd": k, "Option": "false", "TypeOfRemote": "SendRemoteKey"}}))
            print(json.dumps({"key": k, "sent_ms": int(time.time() * 1000)}), flush=True)
            await asyncio.sleep(gap)


asyncio.run(main())
