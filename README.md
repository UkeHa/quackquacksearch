<p align="center">
  <a href="https://github.com/UkeHa/quackquacksearch">
    <img src="qqs.png" alt="QQS the logo for this project. It shows a duck detective holding a magnifier, searching for something" width="300">
  </a>
</p>

# 🦆 QuackQuackSearch (qqs)

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Linux-orange.svg)](https://kernel.org)
[![Desktop](https://img.shields.io/badge/Desktop-KDE%20%7C%20GNOME%20%7C%20XFCE-brightgreen.svg)](#)

> **Lightning-fast file search for Linux – modeled after *Everything* on Windows.**

QuackQuackSearch is a lightweight, ultra-fast file indexer and search service for Linux. It maintains directory paths in a memory-efficient in-memory index, tracks local filesystem changes in real time via Linux `inotify`, polls remote network mounts (NFS, SMB, SSHFS) asynchronously, and serves sub-millisecond search results via CLI, a modern desktop GUI, and native **KDE Plasma KRunner** integration.

[🇩🇪 Deutsche Dokumentation (README-DE.md)](README-DE.md)

---

## ⚡ Key Features

- 🚀 **Sub-Millisecond Search:** SIMD-accelerated (AVX2/NEON) substring matching and relevance ranking across hundreds of thousands of files in under 1 ms.
- ⚡ **Fuzzy Search:** Typo tolerance (Damerau-Levenshtein) and subsequence matching (like `fzf`/`fzy`) with word-boundary scoring across CLI, GUI, and KRunner.
- 💾 **Compact In-Memory Index:** Hierarchical path compression using `DirectoryTable` (folder path deduplication, requiring only ~35–50 bytes of RAM per indexed item).
- 🔄 **Real-Time Synchronization:** Linux-native `inotify` watcher for local filesystems (`IN_CREATE`, `IN_DELETE`, `IN_MOVED_FROM`, `IN_MOVED_TO`).
- 🌐 **Network Mount Support:** Dedicated polling scheduler with configurable intervals and IO timeouts for NFS, CIFS/Samba, and SSHFS shares.
- ⌨️ **Global GUI Hotkey (`Meta+Shift+F`):** Single-Instance window toggle: instantly summon or minimize the desktop search from anywhere.
- 🔌 **Native KDE KRunner Integration:** Search instantly from `Alt+Space` / `Alt+F2` over session D-Bus (`org.kde.krunner1`).
- 🖥️ **Modern Desktop GUI:** Built with Avalonia UI 11, featuring live search-as-you-type, fuzzy toggle, virtualized high-performance DataGrid, category filter chips, and automatic KDE BreezeDark/Light theme sync.
- ⚙️ **Background D-Bus Daemon:** Runs transparently as a systemd user service with fast snapshot persistence on shutdown.
- 💻 **Powerful CLI (`qqs`):** Full control over searches, live monitoring, mount detection, and crawler/search benchmarks.
- 🔄 **Automated CI/CD & Releases:** Continuous integration and automated GitHub Releases on every push to `main`.

---

## 🏗️ Architecture Overview

QuackQuackSearch is designed as a modular solution across 4 projects:

```
┌─────────────────────────────────────────────────────────────┐
│                    QuackQuackSearch.Core                    │
│  - DirectoryTable & CompactFileEntry (In-Memory Index)      │
│  - SimdMatcher & ResultRanker (Vectorized Search)           │
│  - FastFileSystemCrawler & IgnoreMatcher (Glob Filters)     │
│  - LocalInotifyWatcher & NetworkPollingScheduler            │
│  - IndexSerializer (MessagePack Fast Serialization)        │
└──────────────┬───────────────────────────────┬──────────────┘
               │                               │
┌──────────────▼──────────────┐ ┌──────────────▼──────────────┐
│  QuackQuackSearch.Daemon    │ │    QuackQuackSearch.Gui     │
│  - D-Bus Service Host       │ │  - Avalonia UI 11 Desktop   │
│  - org.kde.krunner1 Runner  │ │  - Live Search & DataGrid   │
│  - systemd --user Service   │ │  - BreezeDark Theme Sync    │
└──────────────▲──────────────┘ └──────────────▲──────────────┘
               │                               │
               ├───────────────────────────────┘
┌──────────────┴──────────────┐
│    QuackQuackSearch.Cli     │
│  - Command: qqs             │
│  - IPC Client & Direct Scan │
└─────────────────────────────┘
```

---

## 📋 System Requirements

- **Operating System:** Linux (Kernel 5.x or newer) with x86_64 or ARM64 architecture
- **Runtime / SDK:** [.NET 10.0 SDK or Runtime](https://dotnet.microsoft.com/download)
- **Desktop Environment (optional):** Any desktop environment (KDE Plasma 6 recommended for native KRunner integration)

---

## 📦 Installation

QuackQuackSearch includes an automated installation script (`install.sh`) that compiles all projects in Release mode, sets up binaries and symlinks, installs desktop entries, configures the KRunner plugin, and enables the systemd user service.

### Quick Install (User-level, no root/sudo required)

```bash
git clone https://github.com/quackquacksearch/quackquacksearch.git
cd quackquacksearch

# Run the installer
./install.sh
```

### What gets installed:
- **Binaries & Libraries:** `~/.local/lib/quackquacksearch/`
- **Executables & Symlinks:** `~/.local/bin/qqs`, `~/.local/bin/quackquacksearch-daemon`, `~/.local/bin/quackquacksearch-gui`
- **Desktop Launcher:** `~/.local/share/applications/quackquacksearch-gui.desktop`
- **KRunner D-Bus Plugin:** `~/.local/share/krunner/dbusplugins/quackquacksearch.desktop`
- **systemd User Service:** `~/.config/systemd/user/quackquacksearch.service` (automatically enabled and started)

> **Note:** If `~/.local/bin` is not already in your `PATH`, add the following line to your `~/.bashrc` or `~/.zshrc`:
> ```bash
> export PATH="$HOME/.local/bin:$PATH"
> ```

### Installation Options

```bash
# Install to a custom prefix (e.g. system-wide in /usr/local)
sudo ./install.sh --prefix /usr/local

# Do not enable or start the systemd service (e.g. inside Docker / CI)
./install.sh --no-service
```

---

## ⚡ KDE Plasma KRunner Integration

QuackQuackSearch provides first-class, native integration with **KDE Plasma (Plasma 5 & Plasma 6)** via KRunner. You can locate and open files immediately using the global search prompt (`Alt+Space` or `Alt+F2`) without switching to another application window.

### How It Works

1. The background daemon registers the standard `org.kde.krunner1` D-Bus interface on the Session Bus:
   - **Service Name:** `org.quackquacksearch.Daemon`
   - **Object Path:** `/quackquacksearch`
2. When you type into KRunner, Plasma dispatches asynchronous match requests over D-Bus directly to the QuackQuackSearch daemon.
3. The daemon scans its in-memory index using SIMD vectors and returns ranked results with appropriate MIME icons (documents, source code, folders, media) in fractions of a millisecond.
4. Selecting a result directly executes or opens the file; contextual runner actions allow opening the parent folder in Dolphin with the file highlighted.

### Plugin Registration

The installer automatically deploys the runner descriptor to:
```
~/.local/share/krunner/dbusplugins/quackquacksearch.desktop
```

File content:
```ini
[Desktop Entry]
Name=QuackQuackSearch
Comment=Everything-like ultra fast file search
Type=Service
X-KDE-ServiceTypes=Plasma/Runner
X-Plasma-Runner-Min-Letter-Count=2
X-Plasma-Runner-Match-Regex=^.*$
X-Plasma-Runner-Syntaxes=:q:
X-Plasma-Runner-Syntax-Descriptions=Fast file search matching query
X-Plasma-API=DBus2
X-Plasma-DBusRunner-Service=org.quackquacksearch.Daemon
X-Plasma-DBusRunner-Path=/quackquacksearch
```

### Enabling in KDE System Settings

1. Open **System Settings** (*Systemeinstellungen*).
2. Go to **Search** → **Plasma Search** (*Suchen → Plasma-Suche*).
3. Ensure **QuackQuackSearch** is checked in the list of search providers.
4. You can adjust its position in the list to prioritize QuackQuackSearch results at the top.

### Using KRunner

- **Querying:** Press `Alt+Space` (or `Alt+F2`) and start typing your file query (minimum 2 characters).
- **Default Action (`Enter`):** Opens the matched file in its default associated application.
- **Secondary Action (`Alt+Enter` or action button):** Select *Open Containing Folder* to open Dolphin pointing to the folder with the target file selected.

### Troubleshooting KRunner Integration

- **Restart KRunner:**
  ```bash
  kquitapp6 krunner 2>/dev/null || true
  # KRunner will automatically restart on the next Alt+Space invocation
  ```
- **Verify Daemon is running on D-Bus:**
  ```bash
  qqs status
  ```
- **Test KRunner D-Bus response directly:**
  ```bash
  qdbus6 org.quackquacksearch.Daemon /quackquacksearch org.kde.krunner1.Match "myquery"
  ```
  *(On older systems, use `qdbus` instead of `qdbus6`)*

---

## 💻 CLI Reference (`qqs`)

The `qqs` command-line utility provides instant access to the search index and daemon management. If the daemon is temporarily offline, `qqs search` gracefully falls back to a fast direct local directory scan.

| Command | Description |
|---|---|
| `qqs search <query> [--fuzzy]` | Search for files by substring or extension (with optional fuzzy matching) |
| `qqs status` | Display daemon status, indexed file count, RAM usage, and monitored paths |
| `qqs add <path> [--network]` | Add a new local or network path to live indexing |
| `qqs remove <path>` | Remove a path from live indexing and immediately purge its entries |
| `qqs rescan [path]` | Force a background re-index of a configured path |
| `qqs mounts` | Inspect local and network mount points (`/proc/mounts`) |
| `qqs benchmark [dir]` | Measure filesystem crawler speed and SIMD search throughput |
| `qqs hotkey [register\|status]` | Manage or register global desktop shortcut (`Meta+Shift+F`) |
| `qqs gui` | Launch or summon the graphical desktop interface |
| `qqs config [show\|init]` | View or generate default configuration |
| `qqs service [install\|start\|status]` | Manage the systemd `--user` service unit |

### Examples

```bash
# Search for invoices (exact or substring)
qqs search invoice.pdf

# Fuzzy search (typo tolerance & abbreviation matching like 'qqs' -> 'QuackQuackSearch')
qqs search qqs --fuzzy
qqs search reprot --fuzzy

# Check index statistics
qqs status

# Register global hotkey in KDE Plasma
qqs hotkey register

# Add a local folder to real-time monitoring
qqs add ~/Projects

# Add a remote SMB or NFS network share
qqs add /mnt/nas_share --network

# Remove a path (all its indexed files are removed immediately)
qqs remove ~/Projects

# Run performance benchmark on current directory
qqs benchmark .
```

---

## 🖥️ Desktop Graphical Interface (GUI)

The GUI application (`quackquacksearch-gui` or `qqs gui`) delivers the classic "Everything" experience on Linux:

- **Search-As-You-Type:** Instantaneous result updates with debounced input.
- **⚡ Fuzzy Search Toggle:** Click `⚡ Fuzzy` in the search bar to toggle subsequence abbreviation matching and typo tolerance.
- **Filter Chips:** Rapidly restrict searches to *Documents*, *Images*, *Audio*, *Video*, *Archives*, or *Code*.
- **Virtualized DataGrid:** Fluid scrolling with zero lag across tens of thousands of search hits.
- **Single-Instance IPC & Global Hotkey:**
  - `Meta+Shift+F`: Global hotkey to summon the window or minimize it.
  - `Escape`: First press clears search query, second press minimizes/hides the window.
  - `Ctrl+F` or `Ctrl+L`: Instantly focus search bar and select all text.
  - `Double-click` or `Enter`: Open file with default application.
  - `Right-click`: *Open*, *Open Containing Folder*, *Copy Full Path*.
- **KDE System Theme Support:** Automatic detection of dark/light theme (KDE BreezeDark via `kreadconfig6` and desktop portal).
- **Settings Dialog:** Add or remove indexed paths dynamically with instant index updates.

---

## ⚙️ Configuration

The configuration file is located at:
```
~/.config/quackquacksearch/config.json
```

Example configuration:
```json
{
  "crawler": {
    "maxDegreeOfParallelism": 8,
    "followSymlinks": false,
    "globalExcludes": [
      "**/.git/**",
      "**/node_modules/**",
      "**/bin/**",
      "**/obj/**",
      "**/.cache/**",
      "**/.local/share/Trash/**",
      "**/*.tmp",
      "**/*.lock"
    ]
  },
  "paths": [
    {
      "path": "/home/user",
      "type": "local",
      "enabled": true,
      "pollIntervalSeconds": 0,
      "customExcludes": [
        "**/Downloads/incomplete/**"
      ]
    },
    {
      "path": "/mnt/nas_share",
      "type": "network",
      "enabled": true,
      "pollIntervalSeconds": 60,
      "timeoutSeconds": 5,
      "customExcludes": []
    }
  ],
  "krunner": {
    "enabled": true,
    "serviceName": "org.quackquacksearch.Daemon",
    "objectPath": "/quackquacksearch"
  }
}
```

---

## 🗑️ Uninstallation

To cleanly remove all installed components:

```bash
# Standard uninstall (preserves configuration and index cache)
./uninstall.sh

# Complete purge (removes configuration and cache files as well)
./uninstall.sh --purge
```

---

## 🧪 Testing

The test suite contains 29 automated unit tests verifying the index compression, SIMD matchers, inotify event handling, glob ignore rules, serialization, and mount scanner:

```bash
dotnet test
```

---

## 📄 License

This project is licensed under the MIT License – see the [LICENSE](LICENSE) file for details.
