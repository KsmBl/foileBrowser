#!/usr/bin/env bash
#
# foileBrowser installer (Linux / macOS).
#
# Publishes the app and installs it under a prefix (default: ~/.local, no root needed),
# adding a `foilebrowser` launcher plus, on Linux, an icon and a .desktop entry that can be
# set as the default file manager. Run uninstall.sh to reverse it.
#
# Usage:
#   ./install.sh [--prefix DIR] [--no-aot] [--framework-dependent]
#
# NativeAOT is the default: it is the smallest thing to ship and the lightest to run
# (~75 MB RSS, one binary, no .NET runtime to install), and it is the build the memory
# figures in the README are measured on. It needs 'clang' and zlib to compile, and the
# publish takes a few minutes.
#
#   --no-aot              trimmed self-contained build instead — still runtime-free, still
#                         needs no .NET installed, but a larger footprint. Use this when
#                         clang is unavailable or the AOT publish is too slow.
#   --framework-dependent smallest download, needs the .NET runtime installed to run.
#   --aot, --self-contained  accepted and ignored; both are implied by the default.
#
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PREFIX="${PREFIX:-$HOME/.local}"
SELF_CONTAINED=1
AOT=1

while [ $# -gt 0 ]; do
  case "$1" in
    --prefix) PREFIX="$2"; shift 2 ;;
    --prefix=*) PREFIX="${1#*=}"; shift ;;
    --no-aot) AOT=0; SELF_CONTAINED=1; shift ;;
    --framework-dependent|--no-self-contained) AOT=0; SELF_CONTAINED=0; shift ;;
    # Both were opt-in flags before AOT became the default. Keeping them is what stops an
    # existing script from failing on an option that now describes what it already gets.
    --aot|--self-contained) shift ;;
    -h|--help) grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

APP_DIR="$PREFIX/lib/foilebrowser"
BIN_DIR="$PREFIX/bin"
LAUNCHER="$BIN_DIR/foilebrowser"

command -v dotnet >/dev/null 2>&1 || { echo "error: the .NET SDK ('dotnet') is required to build foileBrowser." >&2; exit 1; }

if [ "$AOT" -eq 1 ]; then
  echo "==> Publishing (Release, NativeAOT) to $APP_DIR — this takes a few minutes"
elif [ "$SELF_CONTAINED" -eq 1 ]; then
  echo "==> Publishing (Release, trimmed self-contained) to $APP_DIR"
else
  echo "==> Publishing (Release, framework-dependent) to $APP_DIR"
fi
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
    command -v clang >/dev/null 2>&1 || {
      echo "error: the default NativeAOT build needs 'clang' (and zlib) to compile natively." >&2
      echo "       Install clang, or run with --no-aot for a trimmed self-contained build." >&2
      exit 1
    }
    PUBLISH_ARGS+=(--self-contained true -p:FoileAot=true)  # NativeAOT (smallest footprint)
  else
    PUBLISH_ARGS+=(--self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=true)
  fi
else
  PUBLISH_ARGS+=(--self-contained false)
fi
rm -rf "$APP_DIR"
dotnet publish "$REPO_DIR/src/FoileBrowser.csproj" "${PUBLISH_ARGS[@]}"

# The AOT publish drops a separate debug companion twice the size of the binary itself. Nobody
# installing a file browser wants 40 MB of symbols in their prefix; the release packaging drops
# them for the same reason.
rm -f "$APP_DIR"/*.dbg "$APP_DIR"/*.pdb

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
