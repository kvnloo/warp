# Notices

Warp is GPL-3.0-only. See LICENSE.

## Not imported

`PatrickSt1991/flixor-tizen` is licensed under a custom grant: AGPL-3.0 plus a non-commercial restriction and a public-source addendum. Those extra terms are not OSI-approved, and the grant says they control over AGPL-3.0 where they conflict. That is incompatible with this GPL-3.0 repository and with any later commercial use. No Flixor source, assets, or build scripts were copied. The port was used only as a feature checklist: PIN login, D-pad navigation, Chrome-generation constraints, and `.wgt` packaging.

`jellyfin/jellyfin-web` is GPL-2.0-only. It was not copied.

`jellyfin/swiftfin` is MPL-2.0. It was used as a UX hierarchy reference only. No SwiftUI, strings, or artwork were copied.

## Concepts, not code

`jellyfin/jellyfin-tizen` is MPL-2.0. Warp reimplements the public Tizen ideas from Samsung's TV documentation rather than copying Jellyfin files: widget `config.xml`, remote key registration, and AVPlay versus HTML5. Samsung's 2022 web-engine table lists Tizen 6.5 / Chromium M85 for this QN85B. The playback profile follows Samsung's 2022 TV codec set (H.264, HEVC, VP9, AV1, AAC, AC-3, E-AC-3, and the containers AVPlay accepts). If AVPlay rejects a direct-play URL, Warp retries once as a transcode. It does not transcode first when the profile matches.

## Third parties

React and Vite are MIT. Their notices travel with `node_modules`. The packaged widget contains compiled React. Do not commit Tizen certificates, `profiles.xml`, or `.env`.
