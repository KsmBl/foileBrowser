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
- [x] Sorting by any column, ascending/descending
- [x] Hidden/system file visibility toggle
- [x] Navigation history: back / forward / up
- [x] Combined path bar (Thunar-style): clickable breadcrumb segments by default; clicking the empty
  area to the right (or Ctrl+L) turns it into an editable path entry, and it reverts on Esc / focus loss
- [x] Switch modified dates between absolute timestamps and relative ("5 min ago", "yesterday"),
  quick-toggle in the toolbar / View menu / command palette (persisted)
- [ ] Editable path bar with autocompletion and recent-folder suggestions (File Pilot "GoTo") — combined breadcrumb/entry path bar with Ctrl+L done; autocompletion/suggestions pending
- [ ] Drive/volume list with free-space indicators
- [ ] Long-path (>260 chars), emoji and full Unicode path support (Windows caveat: `\\?\` prefix handling)

### 6.2 Layout & Views

- [x] Application menu bar (File / Edit / View / Go / Tools / Help) with accelerators and gesture hints
- [x] Toolbar with emoji icons and descriptive tooltips; can be hidden via View ▸ Toolbar (persisted)
- [x] Tabs are the dockable documents: every folder tab has a real tab bar and can be dragged into a
  new pane, tabbed together, or floated into its own window. New Tab (Ctrl+T) adds a tab to the active
  pane; New Pane splits one off side by side. The dual-pane toggle is retired — arrange any number of
  tabs/panes freely; the layout (panes + tabs) is restored across restart. New Pane works even after
  every tab is closed. The operations toolbar can also be shown/hidden from a button inside each tab's
  nav bar (like the hidden-files toggle).
- [ ] Toolbar and copy/move queue as dockable/floatable tool panels — the queue already auto-hides
  when idle and the toolbar can be hidden; turning them into draggable Dock tools is still pending
- [x] Dockable multi-pane layout: open any number of panes and arrange them by splitting, tabbing,
  or floating them into their own windows, with draggable splitters (via Dock.Avalonia); panes tile
  side by side by default and the layout (pane count + tabs) is restored across restart
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
  drilling one level in is instant — configurable column set still pending
- [x] Switch file sizes between binary (KiB/MiB), decimal (KB/MB) and exact bytes, quick-toggle in
  the toolbar / View menu / command palette (persisted)
- [x] Collapsible sidebar with favorites/pinned folders and drives — sidebar with favorites + drives (free-space bars); collapse toggle pending. Pinned favorites can be unpinned via a right-click context menu
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

### 6.5 Preview

- [x] Spacebar quick-preview popup (images, plain text)
- [x] Inspector side panel: persistent preview of the selected item (File Pilot style)
- [x] Folder preview in inspector (item count, size, top-level contents) — count + top-level listing; aggregate byte size pending
- [ ] Thumbnail generation with cache (async, off UI thread) — images decode to bounded width (thumbnail-like); persistent cache pending
- [ ] Syntax-highlighted text/code preview — plain-text preview done; highlighting pending
- [ ] PDF first-page preview

### 6.6 Keyboard & Commands

- [x] Command palette listing every action, fuzzy-searchable
- [ ] Fully rebindable hotkeys, including multi-key sequences — palette shows default gestures; persisted rebinding pending (M4 settings)
- [ ] Hotkey assignment directly from the command palette
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
- [ ] Saved layouts (pane/tab/panel presets), switchable — session (open tabs + layout) restored across restart; multiple named presets pending
- [x] Settings stored as portable JSON

### 6.9 OS Integration

- [ ] searchable Native context-menu passthrough (Windows shell menu; Linux/macOS: curated equivalent)
- [x] "Open terminal here" (configurable terminal per platform) — launches the platform terminal (auto-detected on Linux)
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
- [x] Per-device free-space bar & value and filesystem-type display

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
- [ ] Memory: idle footprint < 129 MB with two panes open

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
- Localization: i18n. english default, german second
- Android device dont support on macOS
