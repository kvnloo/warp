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
if [ -z "${TV_IP:-}" ]; then
  echo "Set TV_IP in .env. Do not commit it." >&2
  exit 1
fi
action="${1:-connect}"
case "$action" in
  connect)
    sdb connect "$TV_IP"
    sdb devices
    ;;
  install)
    sdb connect "$TV_IP" || true
    serial="$(sdb devices | awk '$2=="device" {print $1; exit}')"
    test -n "$serial"
    tizen install -n "$ROOT/Warp.wgt" -s "$serial"
    ;;
  run)
    sdb connect "$TV_IP" || true
    serial="$(sdb devices | awk '$2=="device" {print $1; exit}')"
    test -n "$serial"
    tizen run -p WarpTV2022.Warp -s "$serial"
    ;;
  logs)
    sdb connect "$TV_IP" || true
    sdb dlog -v time
    ;;
  *)
    echo "usage: tv.sh connect|install|run|logs" >&2
    exit 2
    ;;
esac
