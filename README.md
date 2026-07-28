# foileBrowser

A fast, keyboard-first, cross-platform (Windows / Linux / macOS) file browser, inspired by [OneCommander](https://onecommander.com/) and [File Pilot](https://filepilot.tech/).

**Stack:** C# 14 · .NET 10 · [NativeForms](../NativeForms) (Win32/GTK via P/Invoke) · MVVM (CommunityToolkit.Mvvm) · NUnit

## Screenshots

A single pane on first run — a resizable sidebar of favorites, drives and removable devices, the
file list, and the inspector. Split it, tab it and arrange it from there:

![The main window](docs/screenshots/main-window.png)

The app photographs itself, so this can be regenerated anywhere:

```sh
dotnet run --project src/FoileBrowser.csproj -- --screenshot docs/screenshots/main-window.png
```

It composites the window through the toolkit's own draw pipeline rather than asking the desktop for
a grab, which is what makes it work on a headless or Wayland session where a screenshot tool gets
nothing.

## Layout

- `src/` — application code
- `tests/` — NUnit tests
- `docs/` — documentation, including the [PRD](docs/PRD.md) and `screenshots/`

## Build & run

```sh
dotnet run --project src/FoileBrowser.csproj   # launch the app
dotnet test                                    # run the NUnit suite
dotnet build foileBrowser.slnx                 # build the whole solution
```

## Install

Installs a `foilebrowser` launcher (plus, on Linux, an icon and a menu entry that can be set
as the default file manager). No root required — it installs under `~/.local` by default.

```sh
# Linux / macOS
./install.sh                 # or: ./install.sh --prefix /usr/local --self-contained
./uninstall.sh
```

```powershell
# Windows (PowerShell)
./install.ps1                # or: ./install.ps1 -SelfContained
./uninstall.ps1
```

Without `--self-contained` the launcher runs on the installed .NET 10 runtime; with it, a
standalone **trimmed** build is published that needs no runtime and has the smallest footprint.

**Optional:** install `ffprobe` (from FFmpeg) to get the audio/video metadata columns — resolution,
fps, duration, channels, bitrate, codec. Without it those columns stay blank; image metadata columns
(dimensions, megapixels, channels, depth, colour count) need no extra tools.

**Memory:** there is no renderer to choose — the window and the text-bearing controls are real
platform widgets and everything else is painted straight onto them, so no GPU stack, no rendering
engine and no bundled font are mapped in. Icons are drawn in code rather than typed as emoji, which
keeps the colour-emoji and CJK fallback fonts (20 MB between them) out of the process entirely.
SkiaSharp is still linked as an image decoder and loads only when a metadata column or a preview
meets a format the toolkit does not read itself.

Measured idle on one Linux/GTK (Wayland) desktop, published Release:

| Build | RSS | PSS |
|---|---:|---:|
| Avalonia (the previous UI) | 149 MB | 107 MB |
| NativeForms | 96 MB | 42 MB |
| NativeForms + NativeAOT (`./install.sh --aot`) | **73 MB** | 41–48 MB |

Against the neighbours, measured identically on the same folder and session:

| File manager | RSS | PSS |
|---|---:|---:|
| Thunar (GTK, C) | 59 MB | 29 MB |
| **foileBrowser (AOT)** | **73 MB** | 41–48 MB |
| Dolphin (Qt/KDE) | 153 MB | 51 MB |

So: under half of Dolphin's RSS and about level on PSS; Thunar is leaner than both. The ~14 MB
between us and Thunar is the .NET runtime itself — `ConserveMemory`, gen0 tuning and size-optimised
codegen were each measured and none moved it — so closing that gap means not having a managed
runtime. Windows Explorer isn't in the table because it can't be measured here; Wine ships its own
reimplementation, which would tell you nothing about Microsoft's.

PSS is the honest figure — most of what remains is GTK the desktop already has resident. What is
left in the AOT build is mostly the window's own surface buffer (11 MB) and the binary itself
(9 MB). AOT needs `clang` to build; archive support survives it via a compile-time source generator,
so no runtime reflection is used. See [docs/PRD.md](docs/PRD.md) §6.12 for the breakdown.

## Status

- **M0 — Scaffold** ✅ — repo layout, PRD, README
- **M1 — MVP browsing** ✅ — app shell, single virtualized pane, async directory
  listing, back/forward/up + editable path bar, column sorting, hidden-file toggle
- **M2 — Panes, tabs & operations** ✅ — dual pane + splitter, per-pane tabs, sidebar
  (favorites + drives with free-space), background copy/move queue, delete-to-trash,
  rename, new file/folder, copy path/name
- **M3 — Search, preview & palette** ✅ — as-you-type filter, recursive streaming fuzzy
  search with extension filters, inspector panel + spacebar quick-preview (text/image/folder),
  fuzzy command palette (Ctrl+P)
- **M4 — Polish** ✅ — font size + row density, portable JSON
  settings, session restore, color tags (filterable), batch rename (regex/counter/date tokens),
  filesystem-watcher auto-refresh, open-with / open-terminal-here
- **M5 — Devices & archives** ✅ — clean removable/GVfs device list with fs-type + eject and
  plug/unplug auto-refresh; sidebar context menu; opt-in disk formatting / filesystem creation
  (mkfs via pkexec, guarded by type-to-confirm); enter archives (ZIP/TAR/7z/… via CompressionWorkbench)
  as virtual folders, extract, nested descent, identify-format
- **M6 — Configurability** ✅ — rebindable hotkeys (live key capture + conflict detection), per-button
  toolbar show/hide, hideable search bar with Ctrl+F reveal — all in a tabbed Settings dialog
- **M7 — Native UI** ✅ — the whole view layer rebuilt on [NativeForms](../NativeForms): real
  platform windows, buttons and text fields, everything else painted in the desktop's own theme.
  The view-models, services and docking model were untouched. Theme variant and accent colour are
  gone as settings — the toolkit takes both from the desktop. Icons are drawn in code instead of
  typed as emoji, and idle RSS is halved (149 MB → 75 MB with NativeAOT).

See [docs/PRD.md](docs/PRD.md) for the checkboxed feature list and milestones — check off what's
built, delete what's not wanted.
