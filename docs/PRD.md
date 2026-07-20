# foileBrowser — Product Requirements Document

**Status:** Draft · **Last updated:** 2026-07-16

> **Documentation policy (hard requirement).** Every change to the code must update the docs in the
> same commit: check off / add the relevant item in §6 of this PRD, and revise the README when
> user-facing behaviour changes. Docs and code stay in sync — a feature isn't done until its
> requirement is recorded here.

## 1. Overview

foileBrowser is a fast, keyboard-first file browser for Windows, Linux, and macOS, built from a single codebase. It combines the rich, organized UI of [OneCommander](https://onecommander.com/) (dual panes, Miller columns, tabs, color tags, batch tools, deep theming) with the instant-feel performance and command-driven workflow of [File Pilot](https://filepilot.tech/) (command palette, fuzzy search, full keyboard operability, inspector panel).

The guiding principle: **every interaction feels instantaneous, and everything is reachable without the mouse** — while remaining pleasant and discoverable for mouse-first users.

## 2. Goals

- One codebase, three first-class desktop platforms.
- Handle very large directories (100k+ entries) without UI freezes.
- Complete keyboard operability; the mouse is optional, never required.
- Power-user file operations (batch rename, operation queue, scripting) without clutter for casual use.

## 3. Non-Goals

- Cloud storage sync clients (Dropbox/Drive/OneDrive integration beyond what the OS exposes as folders).
- Network protocols (FTP/SFTP/SMB client features) — rely on OS-mounted shares.
- Mobile or web versions.
- Implementing our own archive/filesystem parsers — format support comes from CompressionWorkbench (see §5), not in-house code.

## 4. Target Platforms

- Windows 10+ (x64/ARM64)
- Linux with X11 or Wayland (mainstream distros)
- macOS 12+

## 5. Tech Stack

| Concern | Choice |
|---|---|
| Language | C# 14 on .NET 10 |
| UI framework | Avalonia UI 11.x |
| Architecture | MVVM via CommunityToolkit.Mvvm |
| Archives & filesystem images | [CompressionWorkbench](https://github.com/Hawkynt/CompressionWorkbench) (`Hawkynt.FileFormats.Archives` / `.FileSystems` / `Hawkynt.Compression.Core` NuGet packages, LGPL-3.0) |
| Device mounting (Linux) | GVfs/GIO (MTP, GPhoto2, removable media) |
| Tests | nUnit in `tests/` |
| Source layout | App code in `src/`, docs in `docs/` |

## 6. Feature Requirements

Check off items as they're completed; delete lines you decide not to build.

### 6.1 Core Browsing

- [x] Directory listing with name, size, type, and modified date
- [x] Configurable columns: the file list is data-driven from one shared, ordered column set that
  drives both the header and every row, so header and cells always line up. Columns are **resizable**
  (drag a header's right grip), **reorderable** (drag a header onto another), and **add/removable** via
  the header's right-click menu; the visible set, order and widths persist. Clicking a header sorts by
  it (▲/▼ on the active column). The header sits above the file list only (right of the navigation pane).
  Header and cell text share the same box to the pixel: the list contributes no border or padding of its
  own, and the resize grip overlays the header's right edge rather than consuming layout width
- [x] Arbitrary metadata columns, computed lazily in the background only for shown columns / on-screen
  rows: **image** dimensions, megapixels, channels and bit depth (+ distinct colour count, capped) via
  SkiaSharp; **audio/video** resolution, fps, duration, audio channels, bitrate and codec via `ffprobe`
  when it's installed (blank otherwise). New metadata sources plug in behind an `IMetadataService`
  provider interface
- [x] Hidden/system file visibility toggle
- [x] Navigation history: back / forward / up
- [x] Multiple selection (click, Ctrl/Shift-click, and rubber-band: drag a rectangle over the list to
  select the rows it covers): when a selection is present the status bar shows the count and total size
  of the selected items, and copy/move/delete act on the whole selection
- [x] Clicking the empty area below the file list clears the current selection
- [x] Properties window (Alt+Enter): shows the selected item's type, location, full path, size (folders
  are measured in the background), created/modified times and Unix permissions
- [x] Combined path bar (Thunar-style): clickable breadcrumb segments by default; clicking the empty
  area to the right (or Ctrl+L) turns it into an editable path entry, and it reverts on Esc / focus loss
- [x] Switch modified dates between absolute timestamps and relative ("5 min ago", "yesterday"),
  quick-toggle in the toolbar / View menu / command palette (persisted)
- [ ] Editable path bar with autocompletion and recent-folder suggestions (File Pilot "GoTo") — combined breadcrumb/entry path bar with Ctrl+L done; autocompletion/suggestions pending
- [ ] Drive/volume list with free-space indicators
- [ ] Long-path (>260 chars), emoji and full Unicode path support (Windows caveat: `\\?\` prefix handling)

### 6.2 Layout & Views

- [x] Application menu bar (File / Edit / View / Go / Tools / Help) with accelerators and gesture hints
- [x] Toolbar with emoji icons and descriptive tooltips; can be hidden via View ▸ Toolbar (persisted).
  It holds file/view operations only — back/forward/up/refresh live in each pane's own nav bar next to
  the path bar, so they aren't duplicated on the global toolbar
- [x] Tabs are the dockable documents: every folder tab has a tab strip and can be dragged within its
  strip to reorder, onto another pane's strip to move, or onto a pane **edge** to split a new pane there
  (drop-zone highlight shows where it lands). New Tab (Ctrl+T) adds a tab to the active pane; New Pane
  splits one off side by side. The dual-pane toggle is retired — arrange any number of tabs/panes
  freely; the layout (nested splits + tabs) is restored across restart. New Pane works even after every
  tab is closed. The operations toolbar can also be shown/hidden from a button inside each tab's nav bar
  (like the hidden-files toggle). A pane's tab strip is hidden only for a lone tab in a single-pane
  layout; once docking is in play the strips appear so tabs can be grabbed/closed. (Backed by the
  in-house docking model — no Dock.Avalonia dependency; tear-off floating windows are not provided.)
- [ ] Toolbar and copy/move queue as dockable/floatable tool panels — the queue already auto-hides
  when idle and the toolbar can be hidden; turning them into draggable Dock tools is still pending
- [x] Dockable multi-pane layout: open any number of panes and arrange them by splitting and tabbing,
  with draggable splitters; panes tile side by side by default and the layout (nested splits + tabs) is
  restored across restart. Backed by an **in-house, toolkit-agnostic docking model** (`src/Docking`) —
  a pure-C# tree of panes/splits with the split/move/close/reorder operations, no UI dependency — plus a
  thin Avalonia renderer (`Views/DockLayoutView`), so the same model could later drive a non-Avalonia
  front-end. Replaces the Dock.Avalonia dependency. (Tear-off floating windows are not provided.)
- [x] Single-pane mode toggle
- [x] Tabs per pane, restored across restart
- [x] Details (list) view mode
- [ ] Grid view mode with thumbnails (generated with https://github.com/Hawkynt/PNGCrushCS)
- [x] Computed folder sizes: calculated recursively in the background as folders come into view,
  showing a live "…/300 MiB+" counting hint, with results kept in a small bounded in-memory LRU cache
  (lock-free reads via a concurrent dictionary + interlocked recency ticks). The walk **never follows
  symlinks/junctions** (so cyclic links can't loop and link targets aren't double-counted), skips
  pseudo-filesystems (/proc, /sys, /dev, /run — no more bogus multi-TiB sizes), and is capped so a
  huge tree can't blow up RAM/CPU. Computing a folder also caches its immediate subfolders, so
  drilling one level in is instant
- [x] Switch file sizes between binary (KiB/MiB), decimal (KB/MB) and exact bytes, quick-toggle in
  the toolbar / View menu / command palette (persisted)
- [x] Per-pane navigation sidebar (favorites, drives, grouped partitions, devices): each pane has its
  own tree, toggled independently via a button in that pane's nav bar, so you can browse from any
  pane's tree; clicking an item navigates that pane. Both user-pinned *and* the built-in
  (Home/Desktop/Documents/Downloads) favorites can be unpinned via right-click; removed built-ins are
  remembered and can be restored from Settings ▸ Sidebar
- [x] Choose which sidebar sections are shown (Favorites, Drives, Devices, Folder tree) in
  Settings ▸ Sidebar, and reorder the sections by dragging a section header up or down (order persisted)
- [x] Folder-tree navigator: an optional sidebar section with a lazily-loaded directory tree that
  expands on demand and navigates the pane it lives in on selection. Its root is settable
  (Settings ▸ Sidebar): Home & drives, the filesystem root (/), or the current folder (which re-roots
  the tree to the active pane as you navigate)
- [ ] Drag-and-drop reordering of sidebar favorites

### 6.3 File Operations

- [x] Copy / move with progress dialog and background operation queue
- [x] Blazing-fast transfers: overlapped async read/write with configurable buffers, and an adaptive
  strategy that profiles the drives — overlapped read+write for SSD/cross-device, large sequential
  slurp for a single mechanical/optical spindle (avoids head-seek thrashing). Drive profiling works on
  Linux (/proc/mounts + /sys/block rotational) and Windows (DriveType + IncursSeekPenalty query),
  cached per device
- [x] Delete to OS trash (platform-specific: Recycle Bin / gio trash / NSFileManager)
- [ ] Permanent delete with confirmation (optional overwriting disk space with zeros / random to fully erase file)
- [ ] Conflict resolution dialog (overwrite / skip / rename / apply-to-all) — resolver supports overwrite/skip/rename/cancel; interactive dialog + apply-to-all pending (defaults to auto-rename)
- [x] Inline rename (F2) — via a rename prompt; true in-list inline editing pending. F2 (rename) and
  Delete (trash) are scoped to the focused file list, so they never hijack those keys while typing in
  a text box (path bar, filter, dialogs)
- [x] Batch rename with RegEx, counters, and file-date tokens (OneCommander File Automator style)
- [x] New file / new folder
- [ ] multi Undo / redo for rename/move/delete-to-trash
- [ ] Drag & drop within the app (pane ↔ pane, into sidebar)
- [ ] Drag & drop to/from other OS applications
- [x] Copy path / copy name to clipboard
- [x] Right-click context menu on files/folders: open, copy/move to other pane, rename, delete to
  trash, copy path/name, extract archive here, identify file, and assign/clear color tags

### 6.4 Search & Filter

- [x] As-you-type filter within the current folder
- [x] Recursive fuzzy search across a folder tree or whole drive
- [x] Flattened search results view (path shown per hit)
- [x] Extension/type filters on search results, including extension-only search (empty name query +
  extension filter returns every file of that type); Enter in the extension box starts the search
- [x] Search cancellation and progressive result streaming
- [x] The per-pane filter/search bar can be hidden by default (Settings ▸ General) and summoned on
  demand with Ctrl+F, which reveals and focuses it; Escape dismisses a revealed bar and returns focus
  to the file list

### 6.5 Preview

- [x] Spacebar quick-preview popup (images, plain text)
- [x] Inspector side panel: persistent preview of the selected item (File Pilot style)
- [x] Preview of files inside an opened archive — selecting an entry while browsing an archive streams
  just that entry out to a temp file so the inspector and spacebar quick-preview work there too (entries
  above 16 MB are skipped rather than extracted on a whim)
- [x] Folder preview in inspector (item count, size, top-level contents) — count + top-level listing; aggregate byte size pending
- [ ] Thumbnail generation with cache (async, off UI thread) — images decode to bounded width (thumbnail-like); persistent cache pending
- [ ] Syntax-highlighted text/code preview — plain-text preview done; highlighting pending
- [ ] PDF first-page preview

### 6.6 Keyboard & Commands

- [x] Command palette listing every action, fuzzy-searchable
- [x] Searchable context menu: a search box at the top of the file right-click menu fuzzy-filters the
  actions as you type, and Enter runs the first match
- [x] Fully rebindable hotkeys — every window-wide command is rebindable in Settings ▸ Keybinds by
  clicking its shortcut and pressing the new keys (live capture with conflict detection); overrides
  persist in the portable JSON settings and are applied on the fly. The command registry is the single
  source of truth for the palette, menus and the generated window key bindings, so e.g. Alt+←/→/↑ and
  F5 are now real, editable shortcuts. (Multi-key sequences still pending.)
- [ ] Hotkey assignment directly from the command palette — done via Settings ▸ Keybinds instead
- [x] Complete keyboard operability: navigation, selection, dialogs, panels — core flows keyboard-driven (nav, palette, search, dialogs)
- [ ] Type-ahead selection in file lists

### 6.7 Organization

- [x] Color tags on files and folders, filterable
- [ ] Custom folder icons
- [ ] Per-folder notes/to-dos (OneCommander style)

### 6.8 Customization

- [x] Dark and light themes, following OS setting by default
- [x] Custom accent color
- [x] Font size and row-density settings
- [x] Configurable toolbar — each button on the global operations toolbar can be individually shown or
  hidden from Settings ▸ Toolbar, and the buttons can be reordered by dragging one onto another; both
  the hidden set and the custom order are persisted
- [ ] Saved layouts (pane/tab/panel presets), switchable — session (open tabs + layout) restored across restart; multiple named presets pending
- [x] Settings stored as portable JSON

### 6.9 OS Integration

- [ ] searchable Native context-menu passthrough (Windows shell menu; Linux/macOS: curated equivalent)
- [x] "Open terminal here" — the terminal is configurable in Settings ▸ General: leave it empty to
  auto-detect the first installed one, pick from the terminals detected on this machine, or type any
  command line, where `{dir}` is replaced by the folder (without it the folder becomes the working
  directory). A configured terminal that fails to start falls back to auto-detection
- [x] "Open with…" application picker — opens with the OS default handler; explicit app-picker pending
- [ ] Run user scripts on selected items (PowerShell / bash / Python), configurable script library
- [ ] Set as default file manager guidance per platform

### 6.10 Devices & Removable Media

- [x] Detect removable drives (USB sticks, SD cards, external disks) and show them in sidebar/volume list as they appear
- [x] One-click mount/unmount (Linux: UDisks2/GIO; Windows: drive letters + safe-eject; macOS: `diskutil`/NSWorkspace eject) — eject/unmount done; mounting an unmounted device pending
- [ ] Safe-removal feedback (flush pending writes, warn if device is busy, show which process blocks unmount where possible) — eject invoked via gio/udisks; busy-warning UI pending
- [x] Android phones & cameras via GVfs on Linux (MTP `mtp://` and GPhoto2 `gphoto2://` mounts through GIO) — existing GVfs MTP/GPhoto2 mounts are listed & browsable; initiating the mount pending
- [x] Browse existing GVfs/GIO mounts generally (`/run/user/<uid>/gvfs`), not just phones
- [ ] Android on Windows via WPD/MTP shell namespace (read/copy at minimum)
- [x] Auto-refresh volume list on device plug/unplug events
- [x] Sidebar context menu — right-click a favorite/drive/partition/device to Open, Open in New Tab,
  Open in New Pane, Eject/Unmount (removable) or Unpin (favorites)
- [x] Format drives/partitions & create filesystems — an opt-in "Format / create filesystem…" sidebar
  action (Settings ▸ Disks) creates ext4/ext3/ext2/btrfs/xfs/f2fs/FAT32/exFAT/NTFS filesystems (those
  whose mkfs.* tools are installed) on the selected device. It unmounts, wipes old signatures and runs
  mkfs as root via pkexec (polkit), guarded by a type-the-device-name confirmation and a refusal to
  ever touch the running "/" device. Which filesystem types are offered is configurable. (Linux only
  for now; Windows/macOS pending.)
- [x] Per-device free-space bar & value and filesystem-type display
- [x] Recognise partitions of the same physical disk and group them under one drive (indented
  partitions with their fs/free-space), instead of scattering them as separate devices — the disk's
  real removable flag (/sys/block/&lt;disk&gt;/removable) decides Drives vs Devices, so e.g. /boot and /
  on one NVMe show as two partitions of that disk, not a bogus removable device

### 6.11 Archives & Virtual Filesystems (CompressionWorkbench)

- [x] Enter archives (ZIP, TAR, 7z, RAR, CAB, CPIO, …) as virtual folders — navigate the archive
  index in place (no extraction to temp); opening a single file streams just that entry out to temp
- [x] Extract from archives (single items, selections, or whole archive) through the standard copy queue — "Extract Here" extracts the whole archive; per-selection extract pending
- [ ] Create and modify archives where the format supports write (add/remove entries)
- [x] Nested archive descent (archive inside archive) without manual extraction — entering an extracted inner archive re-enters it
- [ ] Mount disk images (ISO9660/UDF, VHD/VHDX, VMDK, VDI, QCOW2, DMG) as browsable virtual folders
- [ ] Read foreign filesystem images (FAT, exFAT, NTFS, ext, HFS+, APFS, SquashFS, …) via `Hawkynt.FileFormats.FileSystems` — package referenced; wiring pending
- [ ] Pseudo-archive browsing (e.g. resources in EXE/DLL, frames in GIF/TIFF, cover art in MP3/FLAC) — optional, low priority
- [x] Unknown-format identification via CompressionWorkbench signature scanning ("what is this file?" action) — "Identify File" action (extension→format via the registry); byte-signature scan pending
- [x] Streamed access for large archives (browse the index without extracting; extract a single
  entry on demand when a file is opened) — disk-image streaming still pending

### 6.12 Performance Targets

- [ ] Cold start to interactive < 1 s
- [x] 100k-entry directory lists without UI freeze (virtualized lists) — ListBox virtualizes; enumeration is off-thread and cancellable
- [x] All I/O async; UI thread never blocks on disks / removeables / opticals / floppys etc (also r/w errors)
- [x] Directory change detection via file-system watchers (auto-refresh)
- [x] Low memory footprint via layered options (down from ~293 MB idle RSS):
  - CPU/software rendering by default (skips the ~120 MB Mesa/GL stack; `FOILE_GPU=1` re-enables GPU),
    InvariantGlobalization (drops ICU), workstation GC + ConserveMemory.
  - **Trimmed self-contained** (`install.sh --self-contained`): ~103 MB RSS, no runtime needed.
  - **NativeAOT** (`install.sh --aot`, needs clang): **~77 MB RSS** (jit-maps = 0, i.e. true AOT), with
    full archive support — the reflective format discovery was replaced by a compile-time **source
    generator** (`src/Generators`) that statically registers every CompressionWorkbench descriptor, so
    no runtime reflection/`Assembly.LoadFrom` is used and the whole app is trim/AOT-safe. The in-house
    dock view (`Views/DockLayoutView`) updates control properties via direct subscriptions rather than
    reflection string-path bindings, keeping it trim/AOT-clean too.
  - No bundled UI font: `Avalonia.Fonts.Inter` was dropped in favour of system fonts, removing its
    ~1.8 MB mapping (and one package) and reading more native.
  - **Where the memory actually goes** (measured on a framework-dependent Release run, software
    rendering, ~137 MB RSS / ~110 MB PSS / ~79 MB private): the .NET runtime (CoreLib, RegularExpressions,
    coreclr, JIT) ≈ 33 MB, **libX11 ≈ 20 MB** (X11 client lib, mapped even under XWayland), the managed
    heap/JIT-code ≈ 27 MB, SkiaSharp ≈ 4 MB, Avalonia managed assemblies a few MB. Trimming/AOT already
    removes the JIT and unused framework code (hence the ~79 MB AOT figure).
  - **Dock.Avalonia was dropped** in favour of an in-house docking model + view (`src/Docking`,
    `Views/DockLayoutView`). Beyond removing ~10 dependency assemblies, it shaved the footprint to
    ~131 MB RSS / ~99 MB PSS (from ~137 / ~110) — a modest win, as expected, since Dock's managed dlls
    were small. The real value is the removed dependency and a portable, toolkit-agnostic layout core.
  - **Going lower means giving something up** — the remaining floor is the .NET runtime + Skia + X11,
    not Avalonia sub-packages. Switching **Fluent → Simple theme** would break the current styling (which
    relies on `SystemControl*` brushes); an experimental **Wayland backend** could shed libX11's ~20 MB
    but is not production-ready. A materially smaller footprint (≪64 MB) would require leaving the
    Avalonia/Skia stack entirely (native GTK/Qt or hand-rolled), i.e. a rewrite — the in-house docking
    model is a first step towards that portability, recorded here as a deliberate direction.

## 7. Milestones

| Milestone | Contents |
|---|---|
| **M0 — Scaffold** ✅ | Repo layout, PRD, README |
| **M1 — MVP browsing** ✅ | Avalonia app shell, single pane, directory listing, navigation, sorting |
| **M2 — Panes, tabs & operations** ✅ | Dual pane, tabs, copy/move/delete queue, rename, sidebar |
| **M3 — Search, preview & palette** ✅ | Fuzzy search, inspector/quick preview, command palette, hotkeys |
| **M4 — Polish** ✅ | Theming, tags, batch rename, saved layouts, OS integration, perf tuning |
| **M5 — Devices & archives** ✅ | Removable-media mount/unmount, GVfs/MTP (Android), archive & disk-image browsing via CompressionWorkbench |

## 8. notes:

- License: LGPL-3.0
- Localization: i18n. english default, german second — **currently deferred**: InvariantGlobalization
  is enabled for the memory savings (drops ICU), so locale-aware formatting/German are off until we
  re-enable ICU (revert `<InvariantGlobalization>` when localization work begins)
- Android device dont support on macOS
