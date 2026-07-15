#!/usr/bin/env bash
#
# foileBrowser uninstaller (Linux / macOS). Removes what install.sh added.
#
# Usage:
#   ./uninstall.sh [--prefix DIR]
#
set -euo pipefail

PREFIX="${PREFIX:-$HOME/.local}"

while [ $# -gt 0 ]; do
  case "$1" in
    --prefix) PREFIX="$2"; shift 2 ;;
    --prefix=*) PREFIX="${1#*=}"; shift ;;
    -h|--help) grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

echo "==> Removing foileBrowser from $PREFIX"
rm -rf "$PREFIX/lib/foilebrowser"
rm -f  "$PREFIX/bin/foilebrowser"
rm -f  "$PREFIX/share/applications/foilebrowser.desktop"
rm -f  "$PREFIX/share/icons/hicolor/scalable/apps/foilebrowser.svg"
rm -f  "$PREFIX/share/icons/hicolor/256x256/apps/foilebrowser.png"

if [ "$(uname -s)" = "Linux" ]; then
  command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$PREFIX/share/applications" 2>/dev/null || true
  command -v gtk-update-icon-cache   >/dev/null 2>&1 && gtk-update-icon-cache -qtf "$PREFIX/share/icons/hicolor" 2>/dev/null || true
fi

echo "Done. (Per-user settings in ~/.config/foileBrowser were left untouched — delete them manually if desired.)"
