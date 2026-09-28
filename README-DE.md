# 🦆 QuackQuackSearch (qqs)

> **Blitzschnelle Datei-Suche für Linux – nach dem Vorbild von *Everything* (Windows)**

QuackQuackSearch ist ein leichtgewichtiger, extrem schneller Datei-Indexierer und Suchdienst für Linux. Er hält die Verzeichnisstruktur in einem speichereffizienten In-Memory-Index, aktualisiert lokale Pfade über Linux `inotify` in Echtzeit, überwacht Netzwerkfreigaben (NFS, SMB, SSHFS) per Polling und stellt die Suchergebnisse in Millisekunden über CLI, eine moderne Desktop-GUI sowie eine native **KDE Plasma KRunner**-Integration bereit.

[🇬🇧 English Documentation (README.md)](README.md)

---

## ⚡ Hauptmerkmale

- 🚀 **Sub-Millisekunden-Suche:** SIMD-beschleunigte (AVX2/NEON) Teilstring-Suche und Relevanz-Ranking über hunderttausende Dateien in unter 1 ms.
- 💾 **Kompakte Speicherstruktur:** Hierarchische Pfadkompression via `DirectoryTable` (Deduplizierung von Ordnerpfaden, nur ~35–50 Byte RAM pro Datei).
- 🔄 **Echtzeit-Synchronisation:** Linux-nativer `inotify`-Watcher für lokale Dateisysteme (`IN_CREATE`, `IN_DELETE`, `IN_MOVED_FROM`, `IN_MOVED_TO`).
- 🌐 **Netzwerkfreigaben-Support:** Separater Polling-Scheduler mit konfigurierbaren Intervallen und IO-Timeouts für NFS-, CIFS/SMB- und SSHFS-Mounts.
- 🔌 **Native KDE KRunner Integration:** Direktes Durchsuchen über `Alt+Space` / `Alt+F2` per D-Bus (`org.kde.krunner1`).
- 🖥️ **Moderne Desktop-GUI:** Gebaut mit Avalonia UI 11, mit Live-Suche, virtueller Tabelle, Filter-Chips (Dokumente, Bilder, Audio, Code etc.) und automatischer Erkennung des System-Themes (KDE BreezeDark / Light).
- ⚙️ **D-Bus Daemon & systemd-Dienst:** Läuft transparent im Benutzer-Hintergrund mit automatischem Snapshotting bei Beendigung.
- 💻 **Mächtige CLI (`qqs`):** Komplette Verwaltung, Suche, Mount-Erkennung und Benchmarks direkt im Terminal.

---

## 🏗️ Architekturübersicht

QuackQuackSearch ist modular in 4 Komponenten aufgebaut:

```
┌─────────────────────────────────────────────────────────────┐
│                    QuackQuackSearch.Core                    │
│  - DirectoryTable & CompactFileEntry (In-Memory Index)      │
│  - SimdMatcher & ResultRanker (Vektorielle Suche)           │
│  - FastFileSystemCrawler & IgnoreMatcher (Glob-Filter)       │
│  - LocalInotifyWatcher & NetworkPollingScheduler            │
│  - IndexSerializer (MessagePack Schnellspeicherung)        │
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
│  - Befehl: qqs              │
│  - IPC Client & Direct Scan │
└─────────────────────────────┘
```

---

## 📋 Systemvoraussetzungen

- **Betriebssystem:** Linux (Kernel 5.x oder neuer) mit x86_64- oder ARM64-Architektur
- **Laufzeitumgebung / SDK:** [.NET 10.0 SDK oder Runtime](https://dotnet.microsoft.com/download)
- **Desktop (optional):** Beliebige Desktop-Umgebung (KDE Plasma 6 empfohlen für KRunner-Integration)

---

## 📦 Installation

QuackQuackSearch liefert ein komfortables `install.sh`-Skript mit, das alle Binärdateien kompiliert, Wrapper-Skripte installiert, den Desktop-Eintrag anlegt, das KRunner-Plugin registriert und den Hintergrunddienst einrichtet.

### Standard-Installation (Benutzer-Ebene, kein root erforderlich)

```bash
git clone https://github.com/quackquacksearch/quackquacksearch.git
cd quackquacksearch

# Installation ausführen
./install.sh
```

### Was das Skript installiert:
- **Binärdateien & Bibliotheken:** `~/.local/lib/quackquacksearch/`
- **CLI- & Start-Befehle:** `~/.local/bin/qqs`, `~/.local/bin/quackquacksearch-daemon`, `~/.local/bin/quackquacksearch-gui`
- **Desktop-Starter:** `~/.local/share/applications/quackquacksearch-gui.desktop`
- **KRunner-Plugin:** `~/.local/share/krunner/dbusplugins/quackquacksearch.desktop`
- **systemd-Benutzerdienst:** `~/.config/systemd/user/quackquacksearch.service` (wird automatisch aktiviert und gestartet)

> **Tipp:** Falls `~/.local/bin` noch nicht in Ihrer `PATH`-Variable enthalten ist, fügen Sie folgende Zeile zu Ihrer `~/.bashrc` oder `~/.zshrc` hinzu:
> ```bash
> export PATH="$HOME/.local/bin:$PATH"
> ```

### Installationsoptionen

```bash
# Anderes Präfix verwenden (z. B. Systemweit nach /usr/local)
sudo ./install.sh --prefix /usr/local

# systemd-Dienst nicht automatisch starten (z. B. im Docker-Container / CI)
./install.sh --no-service
```

---

## ⚡ KDE Plasma KRunner Integration

QuackQuackSearch integriert sich nahtlos als Suchanbieter in **KDE Plasma (Plasma 5 & Plasma 6)**. Dateien können direkt über die globale Suchleiste (`Alt+Space` oder `Alt+F2`) gefunden und geöffnet werden.

### Funktionsweise

1. Der QuackQuackSearch-Daemon registriert das standardisierte D-Bus-Interface `org.kde.krunner1` auf dem Session-Bus unter:
   - **Service:** `org.quackquacksearch.Daemon`
   - **Objekt-Pfad:** `/quackquacksearch`
2. KRunner kommuniziert asynchron über D-Bus mit dem Daemon. Suchabfragen werden direkt im RAM-Index mit SIMD ausgeführt und innerhalb von Bruchteilen einer Millisekunde zurückgegeben.
3. Treffer werden mit passenden MIME-Icons (Dokument, Bild, Ordner etc.) dargestellt.

### KRunner Plugin Registrierung

Das Installationsskript platziert automatisch die Plugin-Deskriptordatei:
```
~/.local/share/krunner/dbusplugins/quackquacksearch.desktop
```

Inhalt der Datei:
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

### Aktivierung in KDE Plasma

1. Öffnen Sie die **KDE Systemeinstellungen** (*System Settings*).
2. Navigieren Sie zu **Suchen** → **Plasma-Suche** (*Plasma Search*).
3. Vergewissern Sie sich, dass **QuackQuackSearch** in der Liste aktiviert ist.
4. Sie können die Priorität von QuackQuackSearch nach Wunsch nach oben verschieben.

### KRunner-Nutzung & Aktionen

- **Suchen:** Drücken Sie `Alt+Space` (oder `Alt+F2`) und tippen Sie den gesuchten Dateinamen ein (mindestens 2 Zeichen).
- **Öffnen:** Drücken Sie `Enter` auf einem Treffer, um die Datei mit der Standardanwendung zu öffnen.
- **Übergeordneten Ordner öffnen:** Jeder Treffer bietet eine KRunner-Aktion (Zahnrad / Menü oder `Alt+Enter`), um direkt den Ordner im Dateimanager (Dolphin) zu öffnen und die Datei hervorzuheben.

### Diagnose & Fehlerbehebung für KRunner

- **KRunner neu starten:**
  ```bash
  kquitapp6 krunner 2>/dev/null || true
  # KRunner startet bei der nächsten Betätigung von Alt+Space automatisch neu
  ```
- **Prüfen, ob der Daemon auf D-Bus lauscht:**
  ```bash
  qqs status
  ```
- **D-Bus KRunner-Methode manuell abfragen:**
  ```bash
  qdbus6 org.quackquacksearch.Daemon /quackquacksearch org.kde.krunner1.Match "test"
  ```
  *(Bei älteren Systemen `qdbus` statt `qdbus6` verwenden)*

---

## 💻 CLI-Referenz (`qqs`)

Das CLI-Tool `qqs` kommuniziert transparent mit dem laufenden Hintergrund-Daemon. Läuft der Daemon nicht, führt `qqs search` automatisch einen direkten Crawl der konfigurierten Pfade durch.

| Befehl | Beschreibung |
|---|---|
| `qqs search <query>` | Sucht Dateien nach Teilstring oder Dateiendung |
| `qqs status` | Zeigt Daemon-Status, RAM-Verbrauch, Dateianzahl und Pfade |
| `qqs add <pfad> [--network]` | Fügt einen neuen lokalen oder Netzwerk-Pfad zur Überwachung hinzu |
| `qqs remove <pfad>` | Entfernt einen Pfad (entfernt Treffer sofort aus dem Index) |
| `qqs rescan [pfad]` | Erzwingt einen Hintergrund-Neu-Scan eines Pfades |
| `qqs mounts` | Scannt und listet alle lokalen und Netzwerk-Laufwerke (`/proc/mounts`) |
| `qqs benchmark [ordner]` | Misst Crawler-Geschwindigkeit und SIMD-Suchdurchsatz |
| `qqs gui` | Startet die grafische Desktop-Oberfläche |
| `qqs config [show\|init]` | Zeigt die aktuelle Konfiguration oder legt Standardwerte an |
| `qqs service [install\|start\|status]` | Verwaltet den systemd `--user` Dienst |

### Beispiele

```bash
# Suche nach PDF-Dateien mit "rechnung" im Namen
qqs search rechnung.pdf

# Status des Indexers abfragen
qqs status

# Lokalen Ordner hinzufügen
qqs add ~/Projects

# Netzwerkfreigabe (NFS/SMB) hinzufügen
qqs add /mnt/nas_share --network

# Ordner aus Index und Überwachung entfernen
qqs remove ~/Projects

# Leistungs-Benchmark im aktuellen Verzeichnis
qqs benchmark .
```

---

## 🖥️ Desktop-Benutzeroberfläche (GUI)

Die grafische Benutzeroberfläche (`quackquacksearch-gui` oder `qqs gui`) bietet das vertraute Everything-Gefühl auf dem Linux-Desktop:

- **Echtzeit-Suche beim Tippen:** Suchergebnisse erscheinen ohne spürbare Verzögerung während der Eingabe.
- **Kategorie-Filter (Chips):** Schnellfilter nach Dokumenten, Bildern, Audio, Video, Archiven oder Quellcode.
- **Virtuelle Tabelle:** Extrem flüssiges Scrolling auch bei zehntausenden Treffern durch UI-Virtualisierung.
- **Kontextmenü & Tastatur-Navigation:**
  - `Doppelklick` oder `Enter`: Datei ausführen / öffnen
  - `Rechtsklick`: *Datei öffnen*, *Im Ordner anzeigen*, *Dateipfad kopieren*
- **KDE System-Theme (BreezeDark) Integration:** Erkennt automatisch das dunkle oder helle Desktop-Farbschema und passt sich nahtlos an KDE Plasma an.
- **Einstellungen-Dialog:** Pfade hinzufügen und entfernen mit sofortiger Live-Bereinigung des Index.

---

## ⚙️ Konfiguration

Die Konfigurationsdatei befindet sich unter:
```
~/.config/quackquacksearch/config.json
```

Beispielkonfiguration:
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

## 🗑️ Deinstallation

Zum sauberen Entfernen aller installierten Komponenten:

```bash
# Standard-Deinstallation (behält Konfiguration & Cache bei)
./uninstall.sh

# Vollständige Deinstallation inklusive Löschen aller Konfigurations- und Cache-Dateien
./uninstall.sh --purge
```

---

## 🧪 Tests

Die Testsuite umfasst 29 automatisierte Unittests für alle Kernmodule (Indexkompression, SIMD-Matching, inotify-Events, Ignore-Pattern, Serialisierung und Mount-Erkennung):

```bash
dotnet test
```

---

## 📄 Lizenz

MIT License – freie Nutzung für private und kommerzielle Zwecke.
