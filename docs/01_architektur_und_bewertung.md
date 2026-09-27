# QuackQuackSearch - Architektur & Konzeptbewertung

Dieses Dokument bewertet den ursprünglichen Entwurf für **QuackQuackSearch** (ein Linux-Pendant zu "Everything" unter Windows in .NET 10) und hält grundlegende architektonische Weichenstellungen fest.

---

## 1. Zusammenfassung der Bewertung

Der vorgestellte Entwurf ist **hochgradig durchdacht und architektonisch solide**. Insbesondere die Trennung zwischen lokalen Pfaden (Event-basiert via inotify) und Netzwerk-Mounts (Polling) adressiert eines der größten Probleme bestehender Linux-Dateisuchtools.

### Hauptstärken des Konzepts
1. **Hybrides Monitoring**: inotify für lokale Dateisysteme + entkoppeltes Polling für NFS/SMB/GVFS verhindert fehlerhafte Datei-Events über Netzwerkgrenzen.
2. **KRunner über D-Bus**: Die Nutzung des D-Bus-Protokolls `org.kde.krunner1` erlaubt eine native Desktop-Integration in KDE Plasma ohne C++-Kompilierung gegen KDE-Header.
3. **Opt-in Mount-Auswahl**: Kein aggressives Hintergrund-Scannen aller Verzeichnisse (wie z. B. Baloo oft kritisiert wird), sondern volle Kontrolle beim Benutzer.
4. **Moderne Laufzeitumgebung**: .NET 10 LTS bietet exzellente SIMD-Vektorisierung, Zero-Allocation-APIs (`Span<T>`, P/Invoke-Source-Generators) und Native-AOT-Optionen.

---

## 2. Detaillierte Analyse & Herausforderungen

### 2.1 Der "Everything"-Effekt: Windows vs. Linux

| Eigenschaft | Windows ("Everything") | Linux ("QuackQuackSearch") |
| :--- | :--- | :--- |
| **Index-Quelle** | Direkter Read der Master File Table (`$MFT`) & USN Journal auf NTFS-Volumes via Low-Level-IOCTL. | Traversierung über `opendir` / `readdir` (`getdents64`) auf POSIX-Ebene. |
| **Rechte** | Erfordert Admin/Service-Rechte für rohen Disk-Zugriff. | Läuft vollkommen unprivilegiert im Benutzerkontext. |
| **Dateisysteme** | NTFS (einheitlich strukturiert). | ext4, btrfs, zfs, xfs, f2fs, overlayfs, NFS, CIFS usw. |
| **Initialscan-Dauer** | ~1 Sekunde für 1.000.000 Dateien. | ~3–10 Sekunden für 1.000.000 lokale Dateien (NVMe/SSD, abhängig von I/O & CPU-Parallelität). |

> **Konsequenz für QuackQuackSearch**:
> Ein Auslesen von Raw-Dateisystemstrukturen scheidet aus (erfordert Root, ist extrem instabil bei FS-Updates und erfordert FS-spezifische Parser für ext4, btrfs etc.).
> **Entscheidung**: QuackQuackSearch nutzt hochoptimiertes, paralleles Verzeichnis-Crawling im Userspace mit minimalen Speicherallokationen.

---

### 2.2 In-Memory Suchindex: Datenstruktur & Performance

Ein naiver Ansatz (`List<FileInfo>` oder `class Entry { string Path, Name; long Size; }`) führt bei 2–3 Millionen Dateien zu:
- 100+ MB Overhead allein für C# GC-Objekt-Header und Referenzen
- Extremer String-Duplizierung von Pfadpräfixen
- Hoher GC-Druck (Gen 2)

#### Vergleich der Suchindex-Konzepte:

1. **SQLite FTS5**:
   - *Vorteil*: Schnelle Implementierung, fertige SQL-Abfragen.
   - *Nachteil*: FTS5 ist primär für wortbasierte Volltextsuche gedacht. Dateinamen enthalten oft Sonderzeichen (`file_v1.0.tar.gz`). Eine Substring-Suche benötigt den Trigram-Tokenizer (`tokenize='trigram'`). Zudem führen SQLite-Schreibsperren bei Live-Inotify-Updates zu Latenzen bei gleichzeitigen KRunner-Anfragen.
2. **Trie / Radix-Tree**:
   - *Vorteil*: Extrem schnell bei Präfixsuche (`abc*`).
   - *Nachteil*: Schlecht für Infix/Teilstring-Suche (`*bc*`), es sei denn, man baut einen Suffix-Tree/Array (sehr hoher Speicherverbrauch).
3. **Kompaktes Flat-Index mit Pfadkompression + SIMD-Teilstringscan (Empfohlen für Phase 1 & 2)**:
   - Pfade werden hierarchisch getrennt (Verzeichnistabelle mit `ParentId` + Dateinamen-Tabelle).
   - Dateinamen liegen in einem zusammenhängenden Speicherblock (UTF-8 oder UTF-16).
   - Moderne CPUs scannen 1 Million Zeichenketten mit SIMD (`Span<char>.IndexOf` bzw. AVX2/AVX-512) in **10 bis 25 Millisekunden**.
   - Optionaler invertierter Trigram-Index für Abfragen unter 2 Millisekunden.

---

### 2.3 Dateisystem-Monitoring: inotify & Grenzwerte

- **`fs.inotify.max_user_watches`**: Jedes Verzeichnis benötigt einen eigenen Watch Descriptor (`wd`). Bei tiefen Verzeichnisbäumen (z. B. `$HOME` mit Entwicklerprojekten) kann das Systemlimit (Standard auf vielen Distributionen 8.192 oder 65.536) schnell erreicht werden.
- **Event-Queue-Überlauf**: Bei Massenoperationen (z. B. `git checkout` oder Entpacken eines Archivs) kann der inotify-Puffer überlaufen (`IN_Q_OVERFLOW`).
- **Ausschlüsse**: Große volatile Verzeichnisse wie `.git`, `node_modules`, `.cache` müssen standardmäßig filterbar/ausschließbar sein.

---

### 2.4 Netzwerk-Mounts & GVFS-Besonderheiten

- GVFS (`/run/user/<UID>/gvfs/...`) und Kernel-Mounts (NFS, SMB) neigen zu **blockierenden I/O-Hangs**, wenn die Gegenstelle unerreichbar wird (z. B. WLAN-Wechsel, VPN-Verbindungsabbruch).
- **Entscheidung**: Polling von Netzwerkpfaden darf niemals im UI- oder Hauptsuch-Thread erfolgen und muss strikt asynchron mit Timeouts und Abbruchbedingungen (`CancellationToken`) isoliert werden.

---

### 2.5 D-Bus & KRunner

- KRunner (`org.kde.krunner1`) erwartet Antworten innerhalb von ca. 50–200 ms. Längere Latenzen führen dazu, dass KRunner die Ergebnisse verwirft.
- Eine eigene D-Bus-Schnittstelle (`org.quackquacksearch.Daemon`) für CLI/GUI und `org.kde.krunner1` für Plasma können auf demselben D-Bus-Objektpfad oder separaten Pfaden bereitgestellt werden.
- .NET D-Bus-Treiber: `Tmds.DBus` oder das modernere `Tmds.DBus.Protocol` (High-Performance, Allokationsarm).

---

## 3. Architektur-Übersicht

```
                     ┌───────────────────────────────────────┐
                     │    Benutzer-Konfiguration (JSON)      │
                     │  $XDG_CONFIG_HOME/quackquacksearch/   │
                     └──────────────────┬────────────────────┘
                                        │
                     ┌──────────────────▼────────────────────┐
                     │            MountScanner               │
                     │    /proc/mounts + GVFS-Erkennung      │
                     └──────────────────┬────────────────────┘
                                        │
          ┌─────────────────────────────┴────────────────────────────┐
          │                                                          │
┌─────────▼───────────────┐                                ┌─────────▼─────────────────┐
│     LocalWatcher        │                                │      NetworkPoller        │
│   (inotify Manager)     │                                │  (isolierter Scheduler)   │
│ - rekursive Verzeichnisse│                               │ - Timeout / Offline-Erk.  │
│ - Ignore-Filter         │                                │ - Rate-Limiting           │
└─────────┬───────────────┘                                └─────────┬─────────────────┘
          │                                                          │
          └─────────────────────────────┬────────────────────────────┘
                                        │ FileSystemEvents (Add, Remove, Rename)
                                        ▼
                     ┌───────────────────────────────────────┐
                     │           SearchIndexEngine           │
                     │ - DirectoryTree (Pfadkompression)     │
                     │ - Flat / Trigram In-Memory Index      │
                     │ - SIMD-beschleunigte Filterung        │
                     └───────────────┬───────▲───────────────┘
               Snapshot speichern    │       │ Snapshot laden
                                     ▼       │
                     ┌───────────────────────┴───────────────┐
                     │      Persistenz-Engine (Disk)         │
                     │    $XDG_CACHE_HOME/quackquacksearch/  │
                     │       (MessagePack / Binary)          │
                     └───────────────────────────────────────┘
                                        │
                                        ▼
                     ┌───────────────────────────────────────┐
                     │             D-Bus Host                │
                     │  (Tmds.DBus / Tmds.DBus.Protocol)     │
                     ├───────────────────┬───────────────────┤
                     │ org.quackquack-   │ org.kde.krunner1  │
                     │ search.Daemon     │                   │
                     └─────────┬─────────┴─────────┬─────────┘
                               │                   │
                               ▼                   ▼
                     ┌──────────────────┐ ┌──────────────────┐
                     │ CLI / Future GUI │ │  KDE KRunner     │
                     └──────────────────┘ └──────────────────┘
```
