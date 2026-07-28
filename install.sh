#!/usr/bin/env bash
#
# foileBrowser installer (Linux / macOS).
#
# Publishes the app and installs it under a prefix (default: ~/.local, no root needed),
# adding a `foilebrowser` launcher plus, on Linux, an icon and a .desktop entry that can be
# set as the default file manager. Run uninstall.sh to reverse it.
#
# Usage:
#   ./install.sh [--prefix DIR] [--self-contained] [--aot]
#
#   --self-contained  trimmed, runtime-free build (smaller footprint; no .NET needed to run)
#   --aot             NativeAOT build (smallest memory, ~75 MB RSS; requires 'clang' to build,
#                     and implies --self-contained). Publish is slower.
#
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PREFIX="${PREFIX:-$HOME/.local}"
SELF_CONTAINED=0
AOT=0

while [ $# -gt 0 ]; do
  case "$1" in
    --prefix) PREFIX="$2"; shift 2 ;;
    --prefix=*) PREFIX="${1#*=}"; shift ;;
    --self-contained) SELF_CONTAINED=1; shift ;;
    --aot) AOT=1; SELF_CONTAINED=1; shift ;;
    -h|--help) grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

APP_DIR="$PREFIX/lib/foilebrowser"
BIN_DIR="$PREFIX/bin"
LAUNCHER="$BIN_DIR/foilebrowser"

command -v dotnet >/dev/null 2>&1 || { echo "error: the .NET SDK ('dotnet') is required to build foileBrowser." >&2; exit 1; }

echo "==> Publishing (Release) to $APP_DIR"
PUBLISH_ARGS=(-c Release -o "$APP_DIR" --nologo)
if [ "$SELF_CONTAINED" -eq 1 ]; then
  # Self-contained/AOT builds are trimmed for a much smaller memory/disk footprint (see docs/PRD §6.12).
  # Use a *portable* RID (linux-x64, osx-arm64, …): distro-specific RIDs like "arch-x64" (what
  # `dotnet --info` reports on Arch) have no runtime/ILCompiler packages on nuget.org.
  case "$(uname -s)" in Darwin) rid_os=osx ;; *) rid_os=linux ;; esac
  case "$(uname -m)" in
    x86_64|amd64) rid_arch=x64 ;;
    aarch64|arm64) rid_arch=arm64 ;;
    armv7l|armv7|armhf) rid_arch=arm ;;
    *) rid_arch=x64 ;;
  esac
  RID="${rid_os}-${rid_arch}"
  PUBLISH_ARGS+=(-r "$RID")
  if [ "$AOT" -eq 1 ]; then
    command -v clang >/dev/null 2>&1 || { echo "error: --aot needs 'clang' (and zlib) to compile natively." >&2; exit 1; }
    PUBLISH_ARGS+=(--self-contained true -p:FoileAot=true)  # NativeAOT (smallest footprint)
  else
    PUBLISH_ARGS+=(--self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=true)
  fi
else
  PUBLISH_ARGS+=(--self-contained false)
fi
rm -rf "$APP_DIR"
dotnet publish "$REPO_DIR/src/FoileBrowser.csproj" "${PUBLISH_ARGS[@]}"

echo "==> Installing launcher: $LAUNCHER"
mkdir -p "$BIN_DIR"
# DOTNET_GCConserveMemory keeps the managed heap tight. There is no renderer to select: the UI is
# platform widgets plus direct painting, so no GPU stack is ever mapped in.
if [ "$SELF_CONTAINED" -eq 1 ]; then
  cat > "$LAUNCHER" <<EOF
#!/usr/bin/env bash
export DOTNET_GCConserveMemory=\${DOTNET_GCConserveMemory:-9}
exec "$APP_DIR/FoileBrowser" "\$@"
EOF
else
  cat > "$LAUNCHER" <<EOF
#!/usr/bin/env bash
export DOTNET_GCConserveMemory=\${DOTNET_GCConserveMemory:-9}
exec dotnet "$APP_DIR/FoileBrowser.dll" "\$@"
EOF
fi
chmod +x "$LAUNCHER"

if [ "$(uname -s)" = "Linux" ]; then
  ICON_DIR="$PREFIX/share/icons/hicolor"
  APPS_DIR="$PREFIX/share/applications"
  echo "==> Installing icon and desktop entry"
  mkdir -p "$ICON_DIR/scalable/apps" "$ICON_DIR/256x256/apps" "$APPS_DIR"
  install -m644 "$REPO_DIR/assets/foilebrowser.svg" "$ICON_DIR/scalable/apps/foilebrowser.svg"
  [ -f "$REPO_DIR/src/Assets/foilebrowser.png" ] && install -m644 "$REPO_DIR/src/Assets/foilebrowser.png" "$ICON_DIR/256x256/apps/foilebrowser.png"

  cat > "$APPS_DIR/foilebrowser.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=foileBrowser
GenericName=File Manager
Comment=Fast, keyboard-first file browser
Exec=$LAUNCHER %U
Icon=foilebrowser
Terminal=false
Categories=System;FileManager;Utility;
MimeType=inode/directory;
Keywords=files;file manager;explorer;browser;
StartupNotify=true
EOF

  command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$APPS_DIR" 2>/dev/null || true
  command -v gtk-update-icon-cache   >/dev/null 2>&1 && gtk-update-icon-cache -qtf "$ICON_DIR" 2>/dev/null || true
  echo "    To make it your default file manager:  xdg-mime default foilebrowser.desktop inode/directory"
fi

echo
echo "Installed. Launch with 'foilebrowser'."
case ":$PATH:" in
  *":$BIN_DIR:"*) ;;
  *) echo "Note: $BIN_DIR is not on your PATH — add it to use the 'foilebrowser' command." ;;
esac
