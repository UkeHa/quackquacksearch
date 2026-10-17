# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

---

## [0.1.15] - 2026-10-17

### Changed
- **Startup Cache Reconciliation & Directory Table Compaction:**
  - Reconcile cached index entries against current monitored paths and exclusion rules on daemon startup (`ReconcileMonitoredRoots`).
  - Compact `DirectoryTable` storage to reclaim dead nodes and string pool memory when directories are removed or purged.
  - Prune active `inotify` watches immediately when subfolder exclusions are added (`LocalInotifyWatcher.PruneExcludedWatches`).
  - Optimize hot-path allocations and ranking comparisons in `FuzzyMatcher`, `ResultRanker`, and `SearchIndexEngine`.
  - Improve KRunner plugin metadata (`X-Plasma-Runner-Unique-Results`, `X-Plasma-Runner-Min-Letter-Count`) and desktop action launching (`DesktopActions`).

---

## [0.1.14] - 2026-10-17

### Changed
- **Index Memory & Speed Optimizations:**
  - Optimize `DirectoryTable`, `CompactFileEntry`, and `IndexSerializer` for reduced memory footprint and faster serialization/lookup.
  - Centralize D-Bus client connections (`DaemonClient`) and desktop file/folder launch helpers (`DesktopActions`) in `QuackQuackSearch.Core`, removing duplicate code across CLI, Daemon, and GUI.

### Fixed
- Fix path-scoped ignore matching in `IgnoreMatcher` and concurrency synchronization across `QuackDaemonService`, `LocalInotifyWatcher`, and `NetworkPollingScheduler`.

---

## [0.1.13] - 2026-10-17

### Added
- **System Theme Color Detection & Appearance Settings:**
  - Added `SystemThemeDetector` supporting KDE Plasma (`kdeglobals`), GNOME/GTK (`gsettings` / `gtk.css`), XFCE, Cinnamon, Cosmic, Hyprland, Qt5/Qt6 (`qt5ct`/`qt6ct`), and freedesktop portal color schemes.
  - Added new Appearance ("Darstellung" / "Appearance") tab in `SettingsWindow` allowing users to choose between System, Dark, and Light themes as well as custom or system accent colors.

---

## [0.1.12] - 2026-10-16

### Added
- Close `SettingsWindow` on `Escape` key press (unless a text input or combobox is actively focused/capturing Escape).

---

## [0.1.11] - 2026-10-16

### Fixed
- Embedded application window icon (`quackquacksearch.png`) in `QuackQuackSearch.Gui` for `MainWindow` and `SettingsWindow`.
- Configured X11 `WM_CLASS` (`quackquacksearch`) and `StartupWMClass` in `.desktop` launcher for proper taskbar/dock icon association under X11 and Wayland.

---

## [0.1.10] - 2026-10-16

### Added
- **Per-Folder Indexing Status & Localized Settings UI:**
  - Added real-time per-folder indexing state, item count, and error reporting via D-Bus (`GetFolderStatusesAsync`) and `PathItemViewModel` in the GUI settings dialog.
  - Added automatic English/German UI localization (`Loc`) based on system locale across `MainWindow` and `SettingsWindow`.
  - Implemented sliding activity timeout for network mount crawls in `NetworkPollingScheduler` so large active crawls do not time out prematurely while making progress.

---

## [0.1.9] - 2026-10-16

### Fixed
- Separated quick mount reachability check timeout from the full crawl timeout in `NetworkPollingScheduler`.
- Auto-detect network filesystem mounts (`MountScanner.IsNetworkMount`) when adding paths in the GUI `SettingsWindow`.

---

## [0.1.8] - 2026-10-16

### Fixed
- Resolved index entry deduplication and atomic snapshot replacement during full and incremental re-crawls in `SearchIndexEngine` and `DirectoryTable`.
- Improved thread-safety, cancellation handling, and `inotify` watch descriptor lifecycle management (`IN_IGNORED`, move/rename tracking, directory deletion) in `LocalInotifyWatcher` and `QuackDaemonService`.
- Hardened external process execution (argument escaping, non-blocking launches) in KRunner and GUI file actions.

---

## [0.1.7] - 2026-09-29

### Added
- Clicking "Hinzufügen" (Add) with an empty exclusion input in `SettingsWindow` now directly opens the subfolder picker dialog, defaulting to the selected monitored path.

### Fixed
- Strict path exclusion validation (`PathExclusionValidator`): Exclusions must reside within the monitored root path; invalid paths or attempts to escape via `..` are rejected across Core, Daemon, CLI, and GUI.

---

## [0.1.6] - 2026-09-29

### Added
- **Wildcard Prefix Search & Intelligent Ranking:**
  - Full support for prefix wildcards (e.g. `*emes` matches directory `memes`, `themes`, and stem matches like `cat_memes.png`).
  - Wildcard ranking calculates prefix/suffix closeness so closer matches (`memes` with 1 char prefix) rank higher than longer prefixes (`themes` with 2 char prefix).
- **Subfolder & Path Exclusions Per Monitored Path:**
  - Ability to exclude specific subfolders or files within monitored paths (e.g. index `/home/` while excluding `/home/secret/` or `/home/secret/password.txt`).
  - Fast crawler skips excluded paths during recursive enumeration.
  - Inotify watcher avoids watching and processing events for excluded paths.
  - Real-time index purging via `PurgeIgnored` when exclusions are registered.
  - CLI management via `qqs exclude <add|remove|list>` and `qqs add <path> -x <pattern>`.
  - Daemon status (`qqs status`) displays active exclusions per monitored directory.
  - GUI Settings dialog enhanced with split view for managing per-path exclusions.

---

## [0.1.5] - 2026-09-29

### Added
- **Full XDG Base Directory Specification Compliance:**
  - Implemented centralized `XdgDirectories` in `QuackQuackSearch.Core`.
  - Config directory honors `$XDG_CONFIG_HOME` (fallback: `~/.config/quackquacksearch`).
  - Cache and snapshot index honor `$XDG_CACHE_HOME` (fallback: `~/.cache/quackquacksearch`).
  - Data directory honors `$XDG_DATA_HOME` (fallback: `~/.local/share/quackquacksearch`).
  - State directory honors `$XDG_STATE_HOME` (fallback: `~/.local/state/quackquacksearch`).
  - GUI IPC socket honors `$XDG_RUNTIME_DIR` (fallback: system temp path).
  - KDE Theme detection honors `$XDG_CONFIG_HOME/kdeglobals`.
- **Automated Changelogs for Releases:**
  - Added `CHANGELOG.md` following the Keep a Changelog standard.
  - GitHub Actions automated release pipeline now generates and attaches commit logs and release notes to every published release.

### Changed
- `install.sh` and `uninstall.sh` updated to strictly respect `$XDG_DATA_HOME`, `$XDG_CONFIG_HOME`, `$XDG_CACHE_HOME`, and `$XDG_STATE_HOME`.
- Application desktop launcher now uses custom duck icon installed in `$XDG_DATA_HOME/icons/hicolor/512x512/apps/quackquacksearch.png`.

---

## [0.1.4] - 2026-09-29

### Added
- **Arch Linux & CachyOS Packaging:**
  - Added `quackquacksearch-bin` PKGBUILD for instant installation from GitHub Release tarballs via `pacman`.
  - Added `quackquacksearch-git` PKGBUILD for building latest VCS source with `.NET SDK`.
  - Included `.SRCINFO`, `quackquacksearch.install` scriptlet, and desktop/service integration files.

---

## [0.1.3] - 2026-09-29

### Added
- Default fuzzy search across all application components (Core Engine, Daemon, CLI, and GUI).
- Direct CLI search syntax: `qqs <query>` now works directly without the `search` subcommand.
- Optional `--exact` / `-e` flag in CLI to enforce strict substring matching.
- Conflict prevention for numbered filenames (`HasConflictingDigits`) to ensure distinct versions (e.g. `file1` vs `file2`) are never conflated as typos.

### Changed
- Removed redundant `⚡ Fuzzy` toggle button from the GUI header (fuzzy search is always-on).
- Updated documentation in `README.md` and `README-DE.md` to reflect new default search syntax.

---

## [0.1.2] - 2026-09-29

### Fixed
- Improved fuzzy matcher ranking: stem typo matches with distance 1 (e.g. `meems` -> `Memes`) now score higher than scattered loose subsequences across large paths.

---

## [0.1.1] - 2026-09-29

### Added
- Typo-tolerant and subsequence fuzzy search (`FuzzyMatcher`).
- Global GUI hotkey (`Meta+Shift+F`) with single-instance Unix Domain Socket IPC.
- KDE Plasma KRunner integration over D-Bus (`org.kde.krunner1`).
- GitHub Actions CI workflow and automated release pipeline compiling `linux-x64` and `linux-arm64` release bundles.
- Comprehensive installer (`install.sh`), uninstaller (`uninstall.sh`), and documentation in English and German.

---

## [0.1.0] - 2026-09-29

### Added
- Initial project release:
  - Lightning-fast SIMD-accelerated in-memory indexing (`QuackQuackSearch.Core`).
  - Linux `inotify` file watcher and network mount scheduler.
  - Background D-Bus service daemon (`QuackQuackSearch.Daemon`).
  - Terminal client (`QuackQuackSearch.Cli` / `qqs`).
  - Avalonia UI 11 desktop GUI (`QuackQuackSearch.Gui`).
