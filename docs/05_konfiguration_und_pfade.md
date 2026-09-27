# Konfiguration, Pfade & Mount-Scanner

Dieses Dokument beschreibt das Konfigurationsformat, die Einhaltung der XDG-Spezifikationen sowie die Erkennung von Speichermedien und Freigaben über `/proc/mounts`.

---

## 1. XDG-Verzeichnisstruktur

Im ursprünglichen Entwurf wurde noch der Pfad `~/.config/everything-linux/config.json` erwähnt. Dieser wird nun konsequent an den Projektnamen **QuackQuackSearch** und die XDG-Base-Directory-Spezifikation angepasst:

| Verwendungszweck | Standard-Pfad | Umgebungsvariable (XDG) |
| :--- | :--- | :--- |
| **Konfiguration** | `~/.config/quackquacksearch/config.json` | `$XDG_CONFIG_HOME/quackquacksearch/` |
| **Index-Persistenz & Cache** | `~/.cache/quackquacksearch/index.cache` | `$XDG_CACHE_HOME/quackquacksearch/` |
| **Laufzeit-Daten & PID** | `/run/user/<UID>/quackquacksearch/` | `$XDG_RUNTIME_DIR/quackquacksearch/` |
| **KRunner D-Bus Service** | `~/.local/share/krunner/dbusplugins/quackquacksearch.desktop` | `$XDG_DATA_HOME/krunner/dbusplugins/` |

---

## 2. Konfigurationsdatei (`config.json`)

```json
{
  "$schema": "https://raw.githubusercontent.com/quackquacksearch/schema/v1/config.schema.json",
  "version": 1,
  "indexing": {
    "saveIntervalMinutes": 5,
    "maxParallelThreads": 0,
    "indexHiddenFiles": false,
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
    "minCharCount": 2,
    "maxResults": 20,
    "categoryName": "QuackQuackSearch"
  }
}
```

---

## 3. Mount-Scanner: `/proc/mounts` & GVFS

Der Mount-Scanner dient als interaktiver Assistent, der dem Benutzer erkannte Speichergeräte vorschlägt, ohne ungefragt Hintergrundaktivität zu erzeugen.

### 3.1 Auslesen von `/proc/mounts`

Jede Zeile in `/proc/mounts` besteht aus 6 Feldern:
`[Device] [MountPoint] [FSType] [Options] [Dump] [Pass]`

```
/dev/nvme0n1p2 / btrfs rw,noatime,compress=zstd:3 0 0
/dev/nvme0n1p3 /home btrfs rw,noatime,compress=zstd:3 0 0
//nas.local/media /mnt/nas cifs rw,vers=3.1.1 0 0
gvfsd-fuse /run/user/1000/gvfs fuse.gvfsd-fuse rw,nosuid,nodev 0 0
```

### 3.2 Filter-Logik: Pseudodateisysteme ignorieren

Folgende Dateisystem-Typen werden automatisch aus der Vorschlagsliste gefiltert:
- Virtuelle/Kernel-FS: `sysfs`, `proc`, `devtmpfs`, `devpts`, `securityfs`, `cgroup`, `cgroup2`, `pstore`, `bpf`, `configfs`, `autofs`, `mqueue`, `hugetlbfs`, `debugfs`, `tracefs`, `fusectl`, `ramfs`, `overlay`.
- Read-Only Systemabbilder: `squashfs` (z. B. Snaps / Flatpaks).

### 3.3 Klassifizierung der Mounts

1. **Lokale Datenträger**:
   - Dateisysteme: `ext4`, `btrfs`, `xfs`, `f2fs`, `vfat`, `exfat`, `ntfs3`.
   - Empfohlene Überwachung: `local` (inotify).
2. **Netzwerk-Mounts (Kernel)**:
   - Dateisysteme: `nfs`, `nfs4`, `cifs`, `smbfs`, `fuse.sshfs`, `fuse.davfs`.
   - Empfohlene Überwachung: `network` (Polling, z. B. alle 60–120s).
3. **GVFS (Userspace / Desktop-Freigaben)**:
   - Pfad: `/run/user/<UID>/gvfs/...` (z. B. SFTP-, SMB- oder Google-Drive-Mounts über Dolphin/Nautilus).
   - Dateisystem: `fuse.gvfsd-fuse`.
   - Empfohlene Überwachung: `network` (Polling mit striktem I/O-Timeout).
