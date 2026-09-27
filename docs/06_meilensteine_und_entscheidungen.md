# Meilensteine & Architekturentscheidungen (ADR)

Dieses Dokument hält die verbindlichen Architekturentscheidungen (Architecture Decision Records) und den aktualisierten Umsetzungsfahrplan fest.

---

## 1. Architecture Decision Records (ADR)

### ADR-001: Plattform und Programmiersprache
- **Entscheidung**: .NET 10 LTS (C#).
- **Begründung**: Hohe Ausführungsgeschwindigkeit durch native Vektorisierung (SIMD/AVX), Zero-Allocation Memory-Primitives (`Span<T>`, `Memory<T>`), moderne Task/Async-Paradigmen und Native-AOT-Unterstützung für minimale Kaltstartzeiten.

### ADR-002: Index-Struktur statt SQLite FTS5
- **Entscheidung**: Eigener kompakter In-Memory-Index (Directory-Tree mit ID-Referenzierung + kompakte Struct-Tabelle für Dateien) kombiniert mit vektorisierter Substring-Suche.
- **Begründung**:
  - SQLite FTS5 ist für tokenbasierte Volltextsuche optimiert, nicht für beliebige Substrings in Dateinamen (`*foo*`).
  - SQLite erfordert Trigram-Indizes mit 3- bis 4-fachem Disk-Overhead und erzeugt IPC/Locking-Latenzen bei parallelen KRunner-Abfragen und inotify-Schreiboperationen.
  - Der eigene Index benötigt bei 1,5 Mio. Dateien unter 90 MB RAM und liefert Ergebnisse in <15 ms.
  - Spätere Ausbaustufe: Ergänzung durch einen In-Memory Trigram-Index für Latenzen unter 2 ms.

### ADR-003: Duales Monitoring (inotify vs. Polling)
- **Entscheidung**:
  - Lokale Pfade: Event-getrieben via rekursivem `inotify`.
  - Netzwerkfreigaben (NFS, SMB, CIFS, GVFS): Entkoppeltes, gedrosseltes Polling in separaten Worker-Threads mit strikten I/O-Timeouts (3–5s).
- **Begründung**: inotify über Netzwerk-Sockets ist im Linux-Kernel unzuverlässig (keine Benachrichtigung über externe Änderungen auf dem Server) und führt bei Verbindungsverlusten zu blockierenden Kernel-Hangs (`D`-State).

### ADR-004: Persistenz über MessagePack-Binärformat
- **Entscheidung**: Serialisierung des In-Memory-Index als kompakte Binärdatei via MessagePack in `$XDG_CACHE_HOME/quackquacksearch/index.cache`.
- **Begründung**: Schnelleres Laden/Speichern als JSON oder SQLite; atomarer Write über temporäre Dateien verhindert Index-Korruption.

### ADR-005: D-Bus Integration (KRunner & Eigene API)
- **Entscheidung**: Session-D-Bus als primäres IPC-Medium via `Tmds.DBus` / `Tmds.DBus.Protocol`.
- **Begründung**:
  - Ermöglicht direkte Einbindung in KDE Plasma (`org.kde.krunner1`) ohne native C++-Komponenten.
  - Ermöglicht modulare Frontends (CLI, Tastatur-Shortcuts, zukünftige Qt-GUI).

### ADR-006: XDG-Standard & Namenskonvention
- **Entscheidung**: Einheitlicher Projektname `quackquacksearch`.
- **Konfiguration**: `~/.config/quackquacksearch/config.json`.
- **Cache**: `~/.cache/quackquacksearch/index.cache`.
- **Begründung**: Saubere Einhaltung der XDG Base Directory Specification ohne Verunreinigung des Root-Homeverzeichnisses.

---

## 2. Detaillierter Umsetzungs-Fahrplan

```
┌────────────────────────────────────────────────────────┐
│ Phase 1: High-Performance Crawler & In-Memory Index    │
│ - Paralleler Verzeichnis-Scan (Zero-Alloc)             │
│ - Compact Directory & File Tables                      │
│ - SIMD Substring-Suche & Relevanz-Ranking              │
│ - CLI-Testbench zur Geschwindigkeitsmessung            │
└──────────────────────────┬─────────────────────────────┘
                           │
┌──────────────────────────▼─────────────────────────────┐
│ Phase 2: Konfiguration & Mount-Erkennung               │
│ - JSON-Konfiguration (XDG-konform)                     │
│ - /proc/mounts Parser & GVFS-Erkennung                 │
│ - Vorschlags-Generator für Mounts                      │
└──────────────────────────┬─────────────────────────────┘
                           │
┌──────────────────────────▼─────────────────────────────┐
│ Phase 3: Filesystem Monitoring                         │
│ - Rekursiver inotify Watch-Manager                     │
│ - Limit-Prüfung (/proc/sys/fs/inotify/max_user_watches)│
│ - Network-Poller mit Timeout-Schutz                    │
│ - Live-Aktualisierung des In-Memory-Index              │
└──────────────────────────┬─────────────────────────────┘
                           │
┌──────────────────────────▼─────────────────────────────┐
│ Phase 4: Persistenz & Snapshotting                     │
│ - MessagePack Serialisierung                           │
│ - Atomares Speichern / Wiederherstellen beim Start     │
└──────────────────────────┬─────────────────────────────┘
                           │
┌──────────────────────────▼─────────────────────────────┐
│ Phase 5: D-Bus Daemon & CLI Client                     │
│ - Service org.quackquacksearch.Daemon                  │
│ - CLI Tool (qqs search, qqs add, qqs status)           │
└──────────────────────────┬─────────────────────────────┘
                           │
┌──────────────────────────▼─────────────────────────────┐
│ Phase 6: KRunner Integration                           │
│ - org.kde.krunner1 Interface-Implementierung           │
│ - .desktop Runner-Registrierung                        │
│ - Aktionen: Öffnen, Dolphin-Ordner anzeigen, Kopieren  │
└──────────────────────────┬─────────────────────────────┘
                           │
┌──────────────────────────▼─────────────────────────────┐
│ Phase 7: System-Integration & Packaging                │
│ - systemd --user Service Unit                          │
│ - Arch/CachyOS PKGBUILD                                │
└────────────────────────────────────────────────────────┘
```
