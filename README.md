# foileBrowser

A fast, keyboard-first, cross-platform (Windows / Linux / macOS) file browser, inspired by [OneCommander](https://onecommander.com/) and [File Pilot](https://filepilot.tech/).

**Stack:** C# 14 · .NET 10 · Avalonia UI · MVVM (CommunityToolkit.Mvvm) · NUnit

## Layout

- `src/` — application code
- `tests/` — xUnit tests
- `docs/` — documentation, including the [PRD](docs/PRD.md)

## Build & run

```sh
dotnet run --project src/FoileBrowser.csproj   # launch the app
dotnet test                                    # run the NUnit suite
```

## Status

- **M0 — Scaffold** ✅ — repo layout, PRD, README
- **M1 — MVP browsing** ✅ — Avalonia app shell, single virtualized pane, async directory
  listing, back/forward/up + editable path bar, column sorting, hidden-file toggle
- **M2 — Panes, tabs & operations** ✅ — dual pane + splitter, per-pane tabs, sidebar
  (favorites + drives with free-space), background copy/move queue, delete-to-trash,
  rename, new file/folder, copy path/name
- **M3 — Search, preview & palette** ✅ — as-you-type filter, recursive streaming fuzzy
  search with extension filters, inspector panel + spacebar quick-preview (text/image/folder),
  fuzzy command palette (Ctrl+P)

See [docs/PRD.md](docs/PRD.md) for the checkboxed feature list and milestones — check off what's
built, delete what's not wanted.
