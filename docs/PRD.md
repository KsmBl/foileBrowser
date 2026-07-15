# foileBrowser — Product Requirements Document

**Status:** Draft · **Last updated:** 2026-07-15

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
- [ ] Breadcrumb path bar, click any segment to jump
- [ ] Editable path bar with autocompletion and recent-folder suggestions (File Pilot "GoTo") — basic type-and-Enter path bar done; autocompletion/suggestions pending
- [ ] Drive/volume list with free-space indicators
- [ ] Long-path (>260 chars), emoji and full Unicode path support (Windows caveat: `\\?\` prefix handling)

### 6.2 Layout & Views

- [x] optional multi-pane side-by-side layout with adjustable splitter
- [x] Single-pane mode toggle
- [ ] Tabs per pane, restored across restart — tabs done; restore-across-restart pending (settings persistence, M4)
- [x] Details (list) view mode
- [ ] Grid view mode with thumbnails (generated with https://github.com/Hawkynt/PNGCrushCS)
- [ ] Configurable columns, including computed folder sizes (on demand, async)
- [x] Collapsible sidebar with favorites/pinned folders and drives — sidebar with favorites + drives (free-space bars); collapse toggle pending
- [ ] Drag-and-drop reordering of sidebar favorites

### 6.3 File Operations

- [x] Copy / move with progress dialog and background operation queue
- [x] Delete to OS trash (platform-specific: Recycle Bin / gio trash / NSFileManager)
- [ ] Permanent delete with confirmation (optional overwriting disk space with zeros / random to fully erase file)
- [ ] Conflict resolution dialog (overwrite / skip / rename / apply-to-all) — resolver supports overwrite/skip/rename/cancel; interactive dialog + apply-to-all pending (defaults to auto-rename)
- [x] Inline rename (F2) — via a rename prompt; true in-list inline editing pending
- [ ] Batch rename with RegEx, counters, and file-date tokens (OneCommander File Automator style)
- [x] New file / new folder
- [ ] multi Undo / redo for rename/move/delete-to-trash
- [ ] Drag & drop within the app (pane ↔ pane, into sidebar)
- [ ] Drag & drop to/from other OS applications
- [x] Copy path / copy name to clipboard

### 6.4 Search & Filter

- [ ] As-you-type filter within the current folder
- [ ] Recursive fuzzy search across a folder tree or whole drive
- [ ] Flattened search results view (path shown per hit)
- [ ] Extension/type filters on search results
- [ ] Search cancellation and progressive result streaming

### 6.5 Preview

- [ ] Spacebar quick-preview popup (images, plain text)
- [ ] Inspector side panel: persistent preview of the selected item (File Pilot style)
- [ ] Folder preview in inspector (item count, size, top-level contents)
- [ ] Thumbnail generation with cache (async, off UI thread)
- [ ] Syntax-highlighted text/code preview
- [ ] PDF first-page preview

### 6.6 Keyboard & Commands

- [ ] Command palette listing every action, fuzzy-searchable
- [ ] Fully rebindable hotkeys, including multi-key sequences
- [ ] Hotkey assignment directly from the command palette
- [ ] Complete keyboard operability: navigation, selection, dialogs, panels
- [ ] Type-ahead selection in file lists

### 6.7 Organization

- [ ] Color tags on files and folders, filterable
- [ ] Custom folder icons
- [ ] Per-folder notes/to-dos (OneCommander style)

### 6.8 Customization

- [ ] Dark and light themes, following OS setting by default
- [ ] Custom accent color
- [ ] Font size and row-density settings
- [ ] Saved layouts (pane/tab/panel presets), switchable
- [ ] Settings stored as portable JSON

### 6.9 OS Integration

- [ ] searchable Native context-menu passthrough (Windows shell menu; Linux/macOS: curated equivalent)
- [ ] "Open terminal here" (configurable terminal per platform)
- [ ] "Open with…" application picker
- [ ] Run user scripts on selected items (PowerShell / bash / Python), configurable script library
- [ ] Set as default file manager guidance per platform

### 6.10 Devices & Removable Media

- [ ] Detect removable drives (USB sticks, SD cards, external disks) and show them in sidebar/volume list as they appear
- [ ] One-click mount/unmount (Linux: UDisks2/GIO; Windows: drive letters + safe-eject; macOS: `diskutil`/NSWorkspace eject)
- [ ] Safe-removal feedback (flush pending writes, warn if device is busy, show which process blocks unmount where possible)
- [ ] Android phones & cameras via GVfs on Linux (MTP `mtp://` and GPhoto2 `gphoto2://` mounts through GIO)
- [ ] Browse existing GVfs/GIO mounts generally (`/run/user/<uid>/gvfs`), not just phones
- [ ] Android on Windows via WPD/MTP shell namespace (read/copy at minimum)
- [ ] Auto-refresh volume list on device plug/unplug events
- [ ] Per-device free-space bar & value and filesystem-type display

### 6.11 Archives & Virtual Filesystems (CompressionWorkbench)

- [ ] Enter archives (ZIP, TAR, 7z, RAR, CAB, CPIO, …) as virtual folders — navigate, preview, and copy out like normal directories
- [ ] Extract from archives (single items, selections, or whole archive) through the standard copy queue
- [ ] Create and modify archives where the format supports write (add/remove entries)
- [ ] Nested archive descent (archive inside archive) without manual extraction
- [ ] Mount disk images (ISO9660/UDF, VHD/VHDX, VMDK, VDI, QCOW2, DMG) as browsable virtual folders
- [ ] Read foreign filesystem images (FAT, exFAT, NTFS, ext, HFS+, APFS, SquashFS, …) via `Hawkynt.FileFormats.FileSystems`
- [ ] Pseudo-archive browsing (e.g. resources in EXE/DLL, frames in GIF/TIFF, cover art in MP3/FLAC) — optional, low priority
- [ ] Unknown-format identification via CompressionWorkbench signature scanning ("what is this file?" action)
- [ ] Streamed access for large archives/images (no full extraction to temp unless required)

### 6.12 Performance Targets

- [ ] Cold start to interactive < 1 s
- [x] 100k-entry directory lists without UI freeze (virtualized lists) — ListBox virtualizes; enumeration is off-thread and cancellable
- [x] All I/O async; UI thread never blocks on disks / removeables / opticals / floppys etc (also r/w errors)
- [ ] Directory change detection via file-system watchers (auto-refresh)
- [ ] Memory: idle footprint < 129 MB with two panes open

## 7. Milestones

| Milestone | Contents |
|---|---|
| **M0 — Scaffold** ✅ | Repo layout, PRD, README |
| **M1 — MVP browsing** ✅ | Avalonia app shell, single pane, directory listing, navigation, sorting |
| **M2 — Panes, tabs & operations** ✅ | Dual pane, tabs, copy/move/delete queue, rename, sidebar |
| **M3 — Search, preview & palette** | Fuzzy search, inspector/quick preview, command palette, hotkeys |
| **M4 — Polish** | Theming, tags, batch rename, saved layouts, OS integration, perf tuning |
| **M5 — Devices & archives** | Removable-media mount/unmount, GVfs/MTP (Android), archive & disk-image browsing via CompressionWorkbench |

## 8. notes:

- License: LGPL-3.0
- Localization: i18n. english default, german second
- Android device dont support on macOS
