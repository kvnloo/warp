#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
if [ -f "$ROOT/.env" ]; then
  set -a
  # shellcheck disable=SC1091
  source "$ROOT/.env"
  set +a
fi
SDK="${TIZEN_SDK:-$HOME/tizen-studio}"
export PATH="$SDK/tools/ide/bin:$SDK/tools:$PATH"
command -v tizen >/dev/null
cd "$ROOT"
npm run build
cd "$ROOT/apps/tizen"
tizen package -t wgt -s "${TIZEN_PROFILE:-JellyfinTV}" -o "$ROOT" -- dist
ls -lh "$ROOT"/Warp.wgt
