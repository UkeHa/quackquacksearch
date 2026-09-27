# D-Bus Schnittstellen & KRunner Integration

QuackQuackSearch wird als Hintergrunddienst (Daemon) im Session-Bus des Benutzers betrieben. Er exponiert zwei Schnittstellen:
1. Eine **eigene API (`org.quackquacksearch.Daemon`)** für CLI-Werkzeuge, Web-/GUI-Frontends und Skripte.
2. Das standardisierte **KDE KRunner Interface (`org.kde.krunner1`)** für die nahtlose Desktop-Integration.

---

## 1. D-Bus Basis-Konfiguration

- **Bus-Typ**: Session Bus (`DBusScope.Session`)
- **Service-Name**: `org.quackquacksearch.Daemon`
- **Objekt-Pfade**:
  - Eigene API: `/org/quackquacksearch/Daemon`
  - KRunner Runner: `/quackquacksearch`

---

## 2. Eigene API: `org.quackquacksearch.Daemon`

### 2.1 Methoden

#### `Search(string query, uint32 maxResults, IDictionary<string, object> options) -> Struct[]`
Führt eine Dateinamenssuche durch.
- **Rückgabe**: Liste von Treffern:
  - `string FullPath`: Absoluter Dateipfad
  - `string FileName`: Dateiname
  - `uint64 Size`: Größe in Bytes
  - `uint32 ModifiedTime`: Unix-Zeitstempel
  - `bool IsDirectory`: Kennzeichen, ob Ordner
  - `double Score`: Ranking-Wert

#### `AddPath(string path, string type, uint32 pollIntervalSeconds) -> bool`
Fügt einen Pfad zur Überwachung hinzu.
- `type`: `"local"` oder `"network"`

#### `RemovePath(string path) -> bool`
Entfernt einen Pfad aus der Überwachung und dem Index.

#### `ListMonitoredPaths() -> Struct[]`
Gibt die aktuell überwachten Pfade mit aktuellem Status zurück (`Active`, `Scanning`, `Offline`, `Error`).

#### `GetStatus() -> Struct`
Liefert allgemeine Telemetriedaten:
- Gesamtanzahl indexierter Dateien & Ordner
- Arbeitsspeicherverbrauch des Index
- Letzte Aktualisierungszeit
- Inotify Watch-Count & System-Limit

#### `TriggerRescan(string path) -> void`
Erzwingt einen Neu-Scan eines bestimmten Verzeichnisses oder des gesamten Index.

### 2.2 Signale
- `IndexUpdated(uint32 totalFiles, uint32 totalDirectories)`
- `PathStatusChanged(string path, string newStatus, string message)`

---

## 3. KRunner Integration: `org.kde.krunner1`

KDE Plasma (KRunner, Kickoff, Application Launcher) kommuniziert direkt über das `org.kde.krunner1`-Protokoll.

### 3.1 Schnittstellen-Methoden

#### 1. `Actions() -> a(sss)`
Gibt die für Treffer verfügbaren Aktionen zurück:
- `id`: Eindeutiger Aktions-Schlüssel (z. B. `"open"`, `"open_folder"`, `"copy_path"`)
- `text`: Lokalisierter Text (z. B. `"Im Dateimanager anzeigen"`)
- `icon`: Freedesktop Icon Name (z. B. `"system-file-manager"`, `"edit-copy"`)

#### 2. `Match(s query) -> a(sssida{sv})`
Wird von KRunner bei jedem Tastenanschlag asynchron aufgerufen.

**Signatur des Rückgabe-Arrays `a(sssida{sv})`**:
1. `s` (**id**): Eindeutige ID des Treffers (hier: der absolute Dateipfad).
2. `s` (**text**): Haupttext (Dateiname, hervorgehoben).
3. `s` (**icon**): Icon-Name (MIME-Type Icon wie `text-x-csharp`, `image-png`, oder Fallback `unknown`).
4. `i` (**type**): KRunner Match-Typ:
   - `100` = ExactMatch
   - `50` = PossibleMatch
   - `20` = InformationalMatch
5. `d` (**relevance**): Relevanz-Score zwischen `0.0` und `1.0`.
6. `a{sv}` (**properties**): Zusätzliche Eigenschaften:
   - `"subtext"` (String): Pfad zum übergeordneten Verzeichnis (erscheint klein unter dem Dateinamen).
   - `"multiline"` (Bool): Optional mehrzeilige Darstellung.
   - `"category"` (String): `"QuackQuackSearch"`.

#### 3. `Run(s id, s action_id) -> void`
Wird aufgerufen, wenn der Benutzer einen Treffer oder eine Aktion auswählt:
- Wenn `action_id == ""` oder `"open"`: Öffnet die Datei mit dem Standardprogramm (`xdg-open` bzw. `KRun`).
- Wenn `action_id == "open_folder"`: Öffnet das übergeordnete Verzeichnis in Dolphin (`dolphin --select <path>`).
- Wenn `action_id == "copy_path"`: Kopiert den Pfad ins Klipper-Clipboard.

#### 4. `Teardown() -> void`
Signalisiert das Schließen des KRunner-Fensters; laufende Ressourcen können freigegeben werden.

---

## 4. KRunner Desktop-Registrierung

Damit KRunner das Plugin ohne KDE-Build-Tools erkennt, wird eine Desktop-Datei hinterlegt:

**Pfad**: `~/.local/share/krunner/dbusplugins/quackquacksearch.desktop`

```ini
[Desktop Entry]
Name=QuackQuackSearch
Comment=Lightning fast file search via QuackQuackSearch
X-KDE-ServiceTypes=Plasma/Runner
Type=Service
Icon=system-search
X-KDE-PluginInfo-Name=quackquacksearch
X-KDE-PluginInfo-Version=1.0
X-KDE-PluginInfo-License=GPL-3.0
X-KDE-PluginInfo-EnabledByDefault=true
X-Plasma-API=DBus
X-Plasma-DBusRunner-Service=org.quackquacksearch.Daemon
X-Plasma-DBusRunner-Path=/quackquacksearch
X-Plasma-Request-Actions-Once=true
X-Plasma-Runner-Min-Letter-Count=2
X-Plasma-Runner-Unique-Results=true
X-Plasma-Runner-Weak-Results=true
```

Nach Platzieren der Datei genügt ein Neustart von KRunner:
```bash
kquitapp6 krunner 2>/dev/null || krunner --replace &
```
