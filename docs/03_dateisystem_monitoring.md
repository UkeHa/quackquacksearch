# Dateisystem-Monitoring: inotify & Polling-Architektur

Die Dateisystem-Überwachung teilt sich bewusst in zwei völlig unterschiedliche Strategien auf: **Lokale Pfade (Event-getrieben via inotify)** und **Netzwerkpfade (Polling mit Ausfallschutz)**.

---

## 1. Lokale Pfade: inotify Watch Manager

### 1.1 Technische Realität von inotify unter Linux
`inotify` ist im Linux-Kernel **nicht rekursiv**. Um ein Verzeichnis mit 10.000 Unterordnern zu überwachen, müssen 10.000 separate Watch-Deskriptoren registriert werden (`inotify_add_watch`).

`System.IO.FileSystemWatcher` in .NET kapselt `inotify` unter Linux zwar, leidet jedoch unter folgenden Problemen:
1. Schlechte Fehlerdiagnose, wenn das Kernel-Limit (`max_user_watches`) erreicht wird.
2. Unzureichende Kontrolle über Verzeichnis-Umbenennungen mit zusammenhängenden Cookies (`IN_MOVED_FROM` -> `IN_MOVED_TO`).
3. Hoher Allokations- und Event-Overhead bei Massenänderungen.

**Entscheidung**:
Wir implementieren einen dedizierten, schlanken P/Invoke-basierten `InotifyWatchManager` (oder nutzen eine minimale C#-Wrapper-Struktur auf `sys/inotify.h`).

---

### 1.2 Die wichtigsten inotify-Events

```csharp
[Flags]
public enum InotifyMask : uint
{
    IN_ACCESS        = 0x00000001,
    IN_MODIFY        = 0x00000002,
    IN_ATTRIB        = 0x00000004,
    IN_CLOSE_WRITE   = 0x00000008,
    IN_MOVED_FROM    = 0x00000040,
    IN_MOVED_TO      = 0x00000080,
    IN_CREATE        = 0x00000100,
    IN_DELETE        = 0x00000200,
    IN_DELETE_SELF   = 0x00000400,
    IN_MOVE_SELF     = 0x00000800,
    IN_Q_OVERFLOW    = 0x00004000,
    IN_IGNORED       = 0x00008000,
    IN_ISDIR         = 0x40000000
}
```

Für QuackQuackSearch sind primär relevant:
- `IN_CREATE`: Neue Datei oder neuer Ordner (wenn `IN_ISDIR`, wird sofort rekursiv überwacht).
- `IN_DELETE`: Datei oder Ordner entfernt (Watch-Deskriptor wird freigegeben).
- `IN_MOVED_FROM` & `IN_MOVED_TO`: Atomare Umbenennungen (Zusammenführung über das 32-Bit `cookie`-Feld).
- `IN_CLOSE_WRITE`: Dateigrößen- und Zeitstempel-Aktualisierung (erst wenn Schreibzugriff beendet ist).

---

### 1.3 Behandlung von Grenzwerten und Fehlern

1. **`fs.inotify.max_user_watches`**:
   - Vor dem Start liest QuackQuackSearch `/proc/sys/fs/inotify/max_user_watches`.
   - Bei Erreichen von 85% des Limits wird eine Warnung protokolliert.
   - Tritt `ENOSPC` (No space left on device) auf, verfällt der Watcher in einen Teil-Degraded-Modus und gibt dem Benutzer einen klaren Befehl an die Hand:
     ```bash
     echo "fs.inotify.max_user_watches=524288" | sudo tee /etc/sysctl.d/40-quackquacksearch.conf && sudo sysctl --system
     ```
2. **`IN_Q_OVERFLOW` (Event-Puffer Überlauf)**:
   - Passiert bei Massen-I/O (z. B. `git checkout` großer Repos oder Entpacken).
   - Der Watcher markiert den betroffenen Pfad als "dirty" und startet im Hintergrund einen sanften Rescan des Unterbaums.
3. **Event-Debouncing & Batching**:
   - Schnelle aufeinanderfolgende Änderungen an derselben Datei (z. B. Editor Auto-Save) werden über ein 100–300 ms Zeitfenster gebündelt, bevor der Index aktualisiert wird.

---

## 2. Netzwerk-Pfade (NFS / SMB / GVFS)

### 2.1 Warum inotify im Netzwerk versagt
- **NFS / SMB**: Kernel-inotify wird auf dem Client nur dann ausgelöst, wenn lokale Prozesse auf dem Mount-Pfad schreiben. Wenn ein anderer Rechner auf dem NAS eine Datei erstellt oder löscht, erhält der lokale Linux-Kernel **kein** inotify-Signal!
- **Hangs bei Verbindungsverlust**: Typische POSIX-Dateisystemaufrufe (`stat()`, `opendir()`) auf NFS/SMB-Mounts können im Kernel in den Status `D` (Uninterruptible Sleep) geraten, wenn der Server nicht antwortet.

---

### 2.2 Die Polling-Architektur

```
┌────────────────────────────────────────────────────────┐
│               NetworkPollingScheduler                  │
└──────────────────────────┬─────────────────────────────┘
                           │
             Prüfe Intervall (z.B. 60s)
                           │
       ┌───────────────────▼───────────────────┐
       │ IsMountReachableAsync(MountPoint)     │
       │ (Timeout nach z. B. 3 Sekunden)       │
       └───────────────────┬───────────────────┘
               Erreichbar? │
            ┌──────────────┴──────────────┐
       Nein │                         Ja  │
            ▼                             ▼
┌────────────────────────┐  ┌───────────────────────────────────┐
│ Status = "Offline"     │  │ Inkrementeller Diff-Scan:         │
│ Keine weiteren Aufrufe │  │ 1. Vergleiche mtime oberer Ebenen │
│ Retry nach 120s        │  │ 2. Aktualisiere geänderte Chunks  │
└────────────────────────┘  └───────────────────────────────────┘
```

#### Besonderheiten bei GVFS:
- GVFS-Mounts liegen unter `/run/user/<UID>/gvfs/`.
- GVFS nutzt FUSE (`gvfsd-fuse`).
- Erkennung via `/proc/mounts`: Dateisystem-Typ `fuse.gvfsd-fuse`.
- GVFS-Pfade werden exakt wie Netzwerk-Pfade behandelt: Nicht via inotify überwachen, sondern mit gedrosseltem Polling.

---

## 3. Ignorier-Filter (Exclusions)

Um Speicherplatz, I/O und CPU-Zyklen zu schonen, schließt QuackQuackSearch standardmäßig bestimmte System- und volatile Entwickler-Verzeichnisse aus:

### 3.1 Virtuelle & Pseudo-Dateisysteme (nie indexieren)
- `/proc`, `/sys`, `/dev`, `/run` (außer GVFS-Subpfade), `/tmp`

### 3.2 Standard-Ausschlüsse (Konfigurierbar)
- Entwickler-Artefakte: `**/.git/**`, `**/node_modules/**`, `**/bin/**`, `**/obj/**`, `**/.venv/**`
- Caches & Papierkorb: `**/.cache/**`, `**/.local/share/Trash/**`
- System-Locks: `**/*.lock`, `**/*.tmp`, `**/*.swp`
- Der Nutzer kann in der Konfiguration weitere benutzerdefinierte Glob-Patterns oder Regex-Filter hinzufügen oder Standard-Ausschlüsse deaktivieren.
