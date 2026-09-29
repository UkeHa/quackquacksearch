# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Added
- **Full XDG Base Directory Specification Compliance:**
  - Implemented centralized `XdgDirectories` in `QuackQuackSearch.Core`.
  - Config directory honors `$XDG_CONFIG_HOME` (fallback: `~/.config/quackquacksearch`).
  - Cache and snapshot index honor `$XDG_CACHE_HOME` (fallback: `~/.cache/quackquacksearch`).
  - Data directory honors `$XDG_DATA_HOME` (fallback: `~/.local/share/quackquacksearch`).
  - State directory honors `$XDG_STATE_HOME` (fallback: `~/.local/state/quackquacksearch`).
  - GUI IPC socket honors `$XDG_RUNTIME_DIR` (fallback: system temp path).
  - KDE Theme detection honors `$XDG_CONFIG_HOME/kdeglobals`.
- **Arch Linux & CachyOS Packaging:**
  - Added `quackquacksearch-bin` PKGBUILD for instant installation from GitHub Release tarballs via `pacman`.
  - Added `quackquacksearch-git` PKGBUILD for building latest VCS source with `.NET SDK`.
  - Included `.SRCINFO`, `quackquacksearch.install` scriptlet, and desktop/service integration files.
- **Automated Changelogs for Releases:**
  - GitHub Actions automated release pipeline now generates and attaches commit logs and release notes to every published release.

### Changed
- `install.sh` and `uninstall.sh` updated to strictly respect `$XDG_DATA_HOME`, `$XDG_CONFIG_HOME`, `$XDG_CACHE_HOME`, and `$XDG_STATE_HOME`.
- Application desktop launcher now uses custom duck icon installed in `$XDG_DATA_HOME/icons/hicolor/512x512/apps/quackquacksearch.png`.

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
