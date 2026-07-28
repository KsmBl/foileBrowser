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
| UI framework | [NativeForms](https://github.com/Hawkynt/NativeForms) — a Windows-Forms-shaped toolkit over Win32/GTK via P/Invoke |
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
  (drag a column divider), **reorderable** (drag a header past its neighbour), and **add/removable**
  via the list's right-click menu; the visible set, order and widths persist. Clicking a header sorts by
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
- [x] Multiple selection (click, Ctrl/Shift-click): when a selection is present the status bar shows
  the count and total size of the selected items, and copy/move/delete act on the whole selection
- [ ] Rubber-band selection — dragging a rectangle over the list. Was available on the Avalonia
  front-end; the toolkit's grid owns its own mouse handling, so it needs support there first
- [x] Clicking the empty area below the file list clears the current selection
- [x] Properties window (Alt+Enter): shows the selected item's type, location, full path, size (folders
  are measured in the background), created/modified times and Unix permissions
- [x] Combined path bar (Thunar-style): clickable breadcrumb segments by default; clicking the empty
  area to the right (or Ctrl+L) turns it into an editable path entry, and it reverts on Esc / focus loss.
  A path too long for the bar is cut off on the left — the bar stays pinned to its right end so the
  current folder is always visible — instead of showing a scrollbar on top of the segment buttons
- [x] Switch modified dates between absolute timestamps and relative ("5 min ago", "yesterday"),
  quick-toggle in the toolbar / View menu / command palette (persisted)
- [ ] Editable path bar with autocompletion and recent-folder suggestions (File Pilot "GoTo") — combined breadcrumb/entry path bar with Ctrl+L done; autocompletion/suggestions pending
- [ ] Drive/volume list with free-space indicators
- [ ] Long-path (>260 chars), emoji and full Unicode path support (Windows caveat: `\\?\` prefix handling)

### 6.2 Layout & Views

- [x] Application menu bar (File / Edit / View / Go / Tools / Help) with accelerators and gesture hints
- [x] Toolbar with drawn icons; can be hidden via View ▸ Toolbar (persisted). The icons are painted
  from pixel masks rather than typed as emoji (§6.12); the two buttons that show a live value ("KiB",
  "Ago") keep their word instead. A strip item has no tooltip of its own, so the bar's right-click
  menu carries each button's full description plus the reorder commands (see §6.8).
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
  in-house docking model; tear-off floating windows are not provided.)
- [ ] Toolbar and copy/move queue as dockable/floatable tool panels — the queue already auto-hides
  when idle and the toolbar can be hidden; turning them into draggable Dock tools is still pending
- [x] Dockable multi-pane layout: open any number of panes and arrange them by splitting and tabbing,
  with draggable splitters; panes tile side by side by default and the layout (nested splits + tabs) is
  restored across restart. Backed by an **in-house, toolkit-agnostic docking model** (`src/Docking`) —
  a pure-C# tree of panes/splits with the split/move/close/reorder operations, no UI dependency — plus a
  thin renderer (`Views/DockLayoutView`) that maps it onto nested splitters and tab controls. The
  model survived the move from Avalonia to NativeForms untouched, which is what it was built for.
  (Tear-off floating windows are not provided; neither is dragging a tab between panes — the
  toolkit's tab control has no tear-off gesture, so splitting and moving are menu commands.)
- [x] Opens with a single pane. A saved session is restored exactly as it was; a profile with
  nothing saved gets one pane rather than two empty ones, and splitting is one command away.
- [x] The pane's tab strip appears only once there is something to switch between — a second pane,
  or a second tab. One pane holding one tab is just a folder view and carries no header.
- [x] The navigation sidebar is on a splitter, so it can be dragged to any width (down to 90 px) or
  collapsed entirely from the pane's own toggle. The width is not yet persisted across restarts.
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
- [x] Permanent delete with confirmation, overwriting the file's bytes with zeroes first ("Delete
  permanently" in the entry context menu and command palette). Recurses into folders, unlinks symlinks
  without following them into their target, clears the read-only attribute so the pass can't be
  silently skipped, and flushes past the OS cache (`WriteThrough` + flush-to-disk) before unlinking.
  The confirmation dialog requires an explicit acknowledgement tick and states plainly that this is a
  best-effort wipe: **overwriting only reliably destroys the old data on a traditional
  overwrite-in-place filesystem on rotating media** — on SSDs (wear levelling/TRIM), copy-on-write
  filesystems (btrfs, ZFS), and journalled, compressed, RAID or network storage the original blocks can
  survive. Multi-pass / random-fill patterns are not offered, as they add cost without fixing that
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

### 6.4a Verification

- [x] The app photographs itself: `--screenshot <path> [--screenshot-delay <ms>]` writes a PNG of
  every window it has on screen and quits (`src/Views/Screenshot.cs`). It composites through the
  toolkit's own draw pipeline rather than asking the desktop for a grab, because a grab is not
  dependable — ImageMagick's `import` built without its X11 delegate exits zero having written
  nothing, and a rootless Xwayland hands the pixels to the compositor rather than to any X client.
  This is how the README image is regenerated and how a UI change gets checked without a person at
  the screen; it caught three real bugs the moment it was first used (grid icons never drawn,
  mirrored back/forward arrows, clipped sidebar rows).
- [x] **Runs on the Win32 backend** as well as GTK — verified under Wine, where the shell realizes
  its full control tree (64 child windows), pumps its message loop and exits cleanly. The *visual*
  check could not be done there: Wine's `PrintWindow` does not descend into owner-drawn child
  windows, and the display it was tested on cannot be grabbed at all, so the Windows-side capture
  falls back through screen-read → `PrintWindow` → per-child print and still comes out mostly blank.
  Confirming how it *looks* on Windows needs a real Windows session.

### 6.5 Preview

- [x] Spacebar quick-preview popup (images, plain text)
- [x] Inspector side panel: persistent preview of the selected item (File Pilot style)
- [x] Image previews across formats — PNG, BMP, GIF (animated), ICO/CUR and PCX decode through the
  toolkit itself; everything else (JPEG, WebP, TIFF, …) falls back to SkiaSharp, which is already
  linked for the metadata columns. Both paths hand the picture box the same 32-bit ARGB, so no
  intermediate bitmap is written. Dimensions are read from the header first and an image too big to
  decode at full size is scaled on the way in (2048 px longest edge), which also stops a
  decompression bomb from being allocated — `PreviewImageTests` pins that.
- [x] Preview of files inside an opened archive — selecting an entry while browsing an archive streams
  just that entry out to a temp file so the inspector and spacebar quick-preview work there too (entries
  above 16 MB are skipped rather than extracted on a whim)
- [x] Combined properties for a multi-item selection: selecting several items shows them summarised
  together instead of previewing an arbitrary one — item/file/folder counts, total, average, largest
  and smallest size, the modified-date range, and a per-type breakdown (count + bytes per extension,
  largest type first, long tails collapsed). Folder contents aren't walked, and the summary says so
  rather than quietly reporting a total that excludes them
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
- [x] **Windows XP (Luna)** skin — a `Styles` layer on top of Fluent that re-colours the theme
  resources and restyles the controls the app uses (beige `#ECE9D8` face, sunken white fields,
  top-lit button gradients, Luna-blue selection, flat Explorer-style column headers, Tahoma).
  Compiled XAML behind an explicit `Styles` subclass, so it stays trim/AOT-safe. The custom accent
  colour is ignored while it's active, since Luna blue is part of the skin
- [x] Dialog outline — every secondary window (Settings, Properties, rename, format, preview, shred
  confirm) draws a 2px border around its content, so dialogs stay visible against the window behind
  them on compositors that add no decoration of their own (sway/i3 and other tiling WMs). Neutral grey
  by default; the Windows XP skin overrides it with Luna title-bar blue
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
- [x] "Open with…" application picker — the context menu's **Open with** submenu lists the applications
  registered for the file's type, exact MIME matches before `type/*` handlers and the current default
  first. On Linux the type comes from `xdg-mime`, the candidates from the installed `.desktop` files
  (XDG_DATA_DIRS + XDG_DATA_HOME, user entries overriding system ones, skipping `NoDisplay`/`Hidden`
  and non-Application entries), and launching goes through `gio`/`gtk-launch`, falling back to the
  entry's own `Exec` line with its field codes expanded. The scan runs when the menu opens, not on
  every selection change. Other platforms keep the OS default handler
- [x] The Properties window (Alt+Enter) shows an **Opens with** picker for a file's type, listing the
  same candidates and preselecting the current default; "Set default" re-registers the association for
  every file of that type and reports back what actually stuck. Hidden for folders and on platforms
  without association support
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
- [x] Low memory footprint via layered options:
  - No renderer to choose and nothing to composite: the window, buttons and text fields are real
    platform widgets and every other control is painted onto them, so no GPU/Mesa stack, no
    rendering engine and no bundled font are mapped in. `FOILE_GPU` is gone — there is no GPU path
    to switch on. SkiaSharp is still linked, but only as a decoder: it loads the first time a
    metadata column or an image preview needs a format the toolkit does not read itself.
    InvariantGlobalization (drops ICU), workstation GC + ConserveMemory still apply.
  - **Icons are drawn, not typed** (`src/Views/Icons.cs`). The UI used emoji and symbol characters
    as iconography; that reads fine but makes the text stack go looking for fonts covering them, and
    on a stock desktop it maps **Noto Color Emoji (8 MB) plus a CJK fallback (12 MB)** into the
    process for a dozen little pictures. Replacing them with 16×16 ARGB bitmaps built from pixel
    masks cost a few kilobytes and took **28 MB off RSS and 19 MB off PSS** — the single largest
    saving of the whole port, and the one that was entirely self-inflicted. It also makes the icons
    look the same everywhere instead of depending on which emoji font is installed. `IconsTests`
    pins that each one is actually drawn, since nothing else can see them.
  - **Measured idle**, framework-dependent Release on one Linux/GTK (Wayland) desktop:

    | Build | RSS | PSS | private-dirty |
    |---|---:|---:|---:|
    | Avalonia (the previous UI) | 149 MB | 107 MB | — |
    | NativeForms, emoji icons | 131 MB | 68 MB | 47 MB |
    | NativeForms, drawn icons | 103 MB | 49 MB | 35 MB |
    | NativeForms, drawn icons, **NativeAOT** | **75 MB** | **44 MB** | **31 MB** |

  - **Where the AOT build's memory goes**: the window's own GDK surface buffer ≈ 11 MB, the binary
    itself ≈ 9 MB, the GTK stack (gtk/gio/glib/harfbuzz) ≈ 10 MB shared with the rest of the
    desktop, the malloc arena ≈ 5 MB, and glibc's locale archive plus GTK's ICU ≈ 4 MB, which are
    the toolkit's dependencies rather than ours. Roughly half of RSS is shared library text, which
    is why PSS is a little over half of it.
  - **NativeAOT** (`install.sh --aot`, needs clang) produces a single 19 MB binary with full archive
    support: the reflective format discovery is a compile-time **source generator**
    (`src/Generators`) that statically registers every CompressionWorkbench descriptor, and the view
    layer subscribes to properties directly rather than through reflection string-path bindings, so
    the whole app stays trim/AOT-safe. **Trimmed self-contained**
    (`install.sh --self-contained`) also still builds; its footprint has not been re-measured since
    the toolkit change and sits between the two rows above.
  - **What is left to give up.** The floor is now the .NET runtime plus the desktop's own toolkit —
    there is no framework layer left to trade away, and RSS has halved from the Avalonia build.
    Lower still would mean a smaller runtime or fewer GTK dependencies, not a different UI stack.
    The earlier note that a materially smaller footprint "would require leaving the Avalonia/Skia
    stack entirely" is what this port did; the toolkit-agnostic docking model that was called "a
    first step towards that portability" carried over without a single change.

## 7. Milestones

| Milestone | Contents |
|---|---|
| **M0 — Scaffold** ✅ | Repo layout, PRD, README |
| **M1 — MVP browsing** ✅ | App shell, single pane, directory listing, navigation, sorting |
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
