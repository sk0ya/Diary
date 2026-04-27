# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Build (Debug)
dotnet build Diary.sln

# Build (Release)
dotnet build Diary.sln -c Release

# Run
dotnet run --project src/Diary.App/Diary.App.csproj
```

There are no automated tests in this project.

## NuGet Authentication

The `Editor.*` packages are hosted on GitHub Packages (`nuget.pkg.github.com/sk0ya`). A PAT with `read:packages` scope must be configured before restoring — typically via `dotnet nuget add source` or `~/.nuget/NuGet/NuGet.Config` credentials.

## Architecture

Diary is a single-project WPF desktop app (`net9.0-windows`) that runs as a system-tray diary tool.

**Startup & tray (`App.xaml.cs`)** — `App` owns the tray icon and a 50 ms polling timer that watches the cursor position. When the cursor is held at a screen edge for 240 ms, `MainWindow.Reveal()` is called. `ShutdownMode` is `OnExplicitShutdown` so the window can hide without exiting.

**Main window (`MainWindow.xaml[.cs]`)** — `WindowStyle="None"`, `ShowInTaskbar="False"`, `Topmost="True"`. The window positions itself left or right of the triggering screen edge at 28% of the monitor width. It hooks `WM_ACTIVATEAPP` via `HwndSource` to auto-hide when the user clicks away (unless `AlwaysVisible` is set). Layout is three rows: header bar, calendar panel, editor area.

**Editor** — `VimEditorControl` from the `Editor.Controls` / `Editor.Controls.Defaults` NuGet packages (GitHub Packages, owner `sk0ya`). The editor is hosted inside a `ContentControl` (`EditorHostContainer`). It fires `BufferChanged`, `SaveRequested`, and `QuitRequested` events. An auto-save fires 2 seconds after the last buffer change.

**Daily notes (`DailyNoteService.cs`)** — Files are stored as Markdown at `{RootDirectory}/{yyyy}/{MM}/{yyyy-MM-dd}.md`. Default root is `Documents\Diary\Entries`. A template is written on first open for a given date.

**Settings (`AppSettings.cs`, `SettingsWindow.xaml[.cs]`)** — Persisted as JSON at `%APPDATA%\Diary\settings.json`. The two settings are `RootDirectory` (string, nullable) and `AlwaysVisible` (bool). `SettingsWindow` commits and closes on deactivation. Changing the root directory reinitialises `DailyNoteService` and reloads today's note.
