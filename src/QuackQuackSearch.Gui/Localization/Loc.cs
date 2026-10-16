using System.Globalization;

namespace QuackQuackSearch.Gui.Localization;

public sealed class Loc
{
    public static Loc Instance { get; } = new();

    public bool IsGerman { get; }

    private Loc()
    {
        string lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        // Default to German unless an explicit non-German language (like English) is configured,
        // while treating InvariantCulture ("iv") on Linux systems without LC_ALL as German for consistency.
        IsGerman = string.IsNullOrEmpty(lang) ||
                   lang.Equals("iv", StringComparison.OrdinalIgnoreCase) ||
                   lang.Equals("de", StringComparison.OrdinalIgnoreCase);
    }

    // MainWindow Top Bar
    public string SearchPlaceholder => IsGerman
        ? "Dateiname, Erweiterung oder Wildcards (z.B. *.cs, config, doc, quack)..."
        : "Filename, extension or wildcards (e.g. *.cs, config, doc, quack)...";

    public string ClearSearchTooltip => IsGerman ? "Suche leeren" : "Clear search";

    public string SettingsButton => IsGerman ? "⚙️ Einstellungen" : "⚙️ Settings";

    public string SettingsTooltip => IsGerman ? "Einstellungen & überwachte Pfade" : "Settings & monitored paths";

    // Filter Chips
    public string FilterAll => IsGerman ? "Alle" : "All";
    public string FilterFiles => IsGerman ? "Dateien" : "Files";
    public string FilterDirs => IsGerman ? "Ordner" : "Folders";
    public string FilterDocs => IsGerman ? "Dokumente" : "Documents";
    public string FilterImages => IsGerman ? "Bilder" : "Images";
    public string FilterMedia => IsGerman ? "Audio/Video" : "Audio/Video";
    public string FilterCode => IsGerman ? "Code" : "Code";
    public string FilterArchives => IsGerman ? "Archive" : "Archives";

    // DataGrid Columns & Context Menu
    public string ColumnName => IsGerman ? "Name" : "Name";
    public string ColumnPath => IsGerman ? "Pfad" : "Path";
    public string ColumnSize => IsGerman ? "Größe" : "Size";
    public string ColumnModified => IsGerman ? "Geändert am" : "Modified";

    public string MenuOpen => IsGerman ? "Öffnen (Enter)" : "Open (Enter)";
    public string MenuOpenFolder => IsGerman ? "Im Dateimanager anzeigen (Strg+O)" : "Show in File Manager (Ctrl+O)";
    public string MenuCopyPath => IsGerman ? "Pfad kopieren (Strg+Umschalt+C)" : "Copy Path (Ctrl+Shift+C)";
    public string MenuCopyName => IsGerman ? "Dateiname kopieren (Strg+C)" : "Copy Filename (Ctrl+C)";

    // MainWindow Status Bar
    public string StatusReady => IsGerman ? "Bereit" : "Ready";
    public string StatusCheckingDaemon => IsGerman ? "● Prüfe Daemon..." : "● Checking daemon...";
    public string StatusDaemonConnected => IsGerman ? "● Daemon: Verbunden" : "● Daemon: Connected";
    public string StatusDaemonDisconnected => IsGerman ? "○ Daemon getrennt" : "○ Daemon disconnected";
    public string StatusDaemonInactiveCache => IsGerman ? "○ Daemon: Nicht aktiv (Lade Cache...)" : "○ Daemon: Inactive (Loading cache...)";
    public string StatusNoDaemonOrCache => IsGerman ? "Kein Daemon oder Cache gefunden." : "No daemon or cache found.";
    public string StatusStandalone => IsGerman ? "○ Standalone-Modus" : "○ Standalone mode";
    public string PathCopied => IsGerman ? "Pfad in Zwischenablage kopiert!" : "Path copied to clipboard!";
    public string NameCopied => IsGerman ? "Dateiname kopiert!" : "Filename copied!";

    public string FormatTotalFilesAndDirs(long files, long dirs) => IsGerman
        ? $"{files:N0} Dateien in {dirs:N0} Ordnern"
        : $"{files:N0} files in {dirs:N0} folders";

    public string FormatOfflineCacheFiles(long files) => IsGerman
        ? $"{files:N0} Dateien (Offline-Cache)"
        : $"{files:N0} files (offline cache)";

    public string FormatDaemonIndexing(string activePath, long currentFiles) => IsGerman
        ? (currentFiles > 0
            ? $"⟳ Indexiere {activePath} ({currentFiles:N0} Dateien...)"
            : $"⟳ Indexiere {activePath}...")
        : (currentFiles > 0
            ? $"⟳ Indexing {activePath} ({currentFiles:N0} files...)"
            : $"⟳ Indexing {activePath}...");

    public string FormatSearchHits(int count, double ms) => IsGerman
        ? $"{count:N0} Treffer ({ms:F1} ms)"
        : $"{count:N0} hits ({ms:F1} ms)";

    // SettingsWindow
    public string SettingsWindowTitle => IsGerman
        ? "QuackQuackSearch - Einstellungen"
        : "QuackQuackSearch - Settings";

    public string SettingsSectionPathsHeader => IsGerman
        ? "Überwachte Pfade & Ausgeschlossene Unterordner"
        : "Monitored Paths & Excluded Subfolders";

    public string PathEnabledCheckbox => IsGerman ? "Aktiv" : "Enabled";
    public string AddFolderButton => IsGerman ? "📁 Ordner hinzufügen..." : "📁 Add folder...";
    public string RemoveFolderButton => IsGerman ? "🗑️ Entfernen" : "🗑️ Remove";
    public string RescanFolderButton => IsGerman ? "🔄 Neu indexieren" : "🔄 Rescan";
    public string ExcludesHeader => IsGerman ? "Ausschlüsse (Unterordner / Muster):" : "Exclusions (subfolders / patterns):";
    public string SelectPathPrompt => IsGerman ? "Wähle links einen Pfad aus" : "Select a path on the left";
    public string ExcludePlaceholder => IsGerman
        ? "Muster oder Unterordner (z.B. secret/ oder *.tmp)"
        : "Pattern or subfolder (e.g. secret/ or *.tmp)";
    public string AddExcludeButton => IsGerman ? "Hinzufügen" : "Add";
    public string PickSubfolderButton => IsGerman ? "📁 Unterordner wählen..." : "📁 Pick subfolder...";
    public string RemoveExcludeButton => IsGerman ? "🗑️ Ausschluss löschen" : "🗑️ Remove exclusion";
    public string CloseButton => IsGerman ? "Schließen" : "Close";

    public string FormatPathTypeAndStatus(string type, bool enabled, string status, long currentCrawlFiles)
    {
        string typeLabel = string.Equals(type, "network", StringComparison.OrdinalIgnoreCase)
            ? (IsGerman ? "Netzwerk (Polling)" : "Network (Polling)")
            : (IsGerman ? "Lokal (inotify)" : "Local (inotify)");

        if (!enabled || string.Equals(status, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            return IsGerman ? $"{typeLabel} • ⚪ Deaktiviert" : $"{typeLabel} • ⚪ Disabled";
        }

        if (string.Equals(status, "Scanning", StringComparison.OrdinalIgnoreCase))
        {
            string progressText = currentCrawlFiles > 0
                ? (IsGerman ? $" ({currentCrawlFiles:N0} Dateien)" : $" ({currentCrawlFiles:N0} files)")
                : string.Empty;
            return IsGerman
                ? $"{typeLabel} • 🟡 Indexiert gerade...{progressText}"
                : $"{typeLabel} • 🟡 Indexing...{progressText}";
        }

        if (string.Equals(status, "Unreachable", StringComparison.OrdinalIgnoreCase))
        {
            return IsGerman ? $"{typeLabel} • 🔴 Nicht erreichbar" : $"{typeLabel} • 🔴 Unreachable";
        }

        return IsGerman ? $"{typeLabel} • 🟢 Bereit" : $"{typeLabel} • 🟢 Ready";
    }
}
