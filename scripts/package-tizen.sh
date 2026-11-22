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
rm -f "$ROOT/Warp.wgt"
if [ -n "${TIZEN_AUTHOR_PASS_FILE:-}" ]; then
  # Headless/ssh: the desktop keyring that holds profile passwords is locked. Point TIZEN_SDK at an
  # isolated CLI copy whose profiles.xml has empty passwords, and answer the prompts from files.
  python3 "$ROOT/scripts/tizen-pty-sign.py" "${TIZEN_PROFILE:-JellyfinTV}" "$ROOT" dist
else
  tizen package -t wgt -s "${TIZEN_PROFILE:-JellyfinTV}" -o "$ROOT" -- dist
fi
ls -lh "$ROOT"/Warp.wgt
