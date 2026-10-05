# Warp

Fast Plex client for a Samsung QN85B (Tizen 6.5, Chromium M85). GPL-3.0-only. See `NOTICE.md` before importing other TV clients.

```bash
cp .env.example .env   # TV_IP stays out of git
npm ci
npm test
npm run dev            # browser UI on 127.0.0.1:5173
npm run build          # apps/tizen/dist, Chrome 85 target
npm run package:tizen  # signed Warp.wgt if Tizen Studio and a profile exist
npm run tv             # build, package, install, launch
```

`npm run tv:connect`, `tv:install`, `tv:run`, and `tv:logs` read `TV_IP` from `.env`.

Developer Mode on the TV: Apps, App Settings, `12345`, Developer mode On, host IP of this machine, reboot. The host IP is whatever is on the same LAN as the TV. Do not commit it.

Red button or `d` opens the performance receipt. Those numbers are TV measurements only when the bundle is running on the QN85B.
