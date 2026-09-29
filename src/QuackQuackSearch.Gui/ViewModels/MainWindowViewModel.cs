using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using QuackQuackSearch.Core.Config;
using QuackQuackSearch.Core.DBus;
using QuackQuackSearch.Core.Index;
using QuackQuackSearch.Core.Search;
using QuackQuackSearch.Core.Storage;
using QuackQuackSearch.Gui.Models;
using Tmds.DBus;

namespace QuackQuackSearch.Gui.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SearchIndexEngine _fallbackEngine = new();
    private IDaemonService? _daemonService;
    private readonly DispatcherTimer _debounceTimer;

    private string _searchText = string.Empty;
    private string _activeFilter = "all";
    private string _statusLeft = "Bereit";
    private string _statusMiddle = string.Empty;
    private string _statusDaemon = "● Prüfe Daemon...";
    private bool _isDaemonConnected;
    private bool _isLoading;
    private FileItemModel? _selectedItem;

    private readonly List<FileItemModel> _allCurrentResults = [];

    public ObservableCollection<FileItemModel> Results { get; } = [];

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText != value)
            {
                _searchText = value;
                OnPropertyChanged();
                RestartDebounceTimer();
            }
        }
    }

    public string ActiveFilter
    {
        get => _activeFilter;
        set
        {
            if (_activeFilter != value)
            {
                _activeFilter = value;
                OnPropertyChanged();
                ApplyFilterAndRefreshList();
            }
        }
    }

    private bool _isFuzzy;
    public bool IsFuzzy
    {
        get => _isFuzzy;
        set
        {
            if (_isFuzzy != value)
            {
                _isFuzzy = value;
                OnPropertyChanged();
                RestartDebounceTimer();
            }
        }
    }

    public string StatusLeft
    {
        get => _statusLeft;
        private set { _statusLeft = value; OnPropertyChanged(); }
    }

    public string StatusMiddle
    {
        get => _statusMiddle;
        private set { _statusMiddle = value; OnPropertyChanged(); }
    }

    public string StatusDaemon
    {
        get => _statusDaemon;
        private set { _statusDaemon = value; OnPropertyChanged(); }
    }

    public bool IsDaemonConnected
    {
        get => _isDaemonConnected;
        private set { _isDaemonConnected = value; OnPropertyChanged(); }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set { _isLoading = value; OnPropertyChanged(); }
    }

    public FileItemModel? SelectedItem
    {
        get => _selectedItem;
        set { _selectedItem = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindowViewModel()
    {
        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _debounceTimer.Tick += async (_, _) =>
        {
            _debounceTimer.Stop();
            await ExecuteSearchAsync();
        };

        _ = InitializeAsync();
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            var conn = Connection.Session;
            await conn.ConnectAsync();
            bool active = await conn.IsServiceActiveAsync("org.quackquacksearch.Daemon");

            if (active)
            {
                _daemonService = conn.CreateProxy<IDaemonService>("org.quackquacksearch.Daemon", "/org/quackquacksearch/Daemon");
                var status = await _daemonService.GetStatusAsync();
                IsDaemonConnected = true;
                StatusDaemon = "● Daemon: Verbunden";
                StatusLeft = $"{status.TotalFiles:N0} Dateien in {status.TotalDirectories:N0} Ordnern";
            }
            else
            {
                StatusDaemon = "○ Daemon: Nicht aktiv (Lade Cache...)";
                bool loaded = await IndexSerializer.TryLoadAsync(_fallbackEngine);
                if (loaded)
                {
                    StatusLeft = $"{_fallbackEngine.TotalFiles:N0} Dateien (Offline-Cache)";
                }
                else
                {
                    StatusLeft = "Kein Daemon oder Cache gefunden.";
                }
            }
        }
        catch (Exception ex)
        {
            StatusDaemon = "○ Standalone-Modus";
            StatusLeft = $"Offline: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }

        // Initial search for recent or top items
        await ExecuteSearchAsync();
    }

    private void RestartDebounceTimer()
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    public async Task ExecuteSearchAsync()
    {
        string query = SearchText.Trim();
        var sw = Stopwatch.StartNew();

        _allCurrentResults.Clear();

        if (string.IsNullOrEmpty(query))
        {
            // Empty search: if fallback engine has data, show recent entries
            if (_fallbackEngine.TotalFiles > 0)
            {
                var sample = _fallbackEngine.Search("a", new SearchOptions { MaxResults = 100 });
                foreach (var r in sample)
                {
                    _allCurrentResults.Add(ToModel(r));
                }
            }
            else if (_daemonService != null)
            {
                try
                {
                    var dtos = await _daemonService.SearchAsync("a", 100);
                    foreach (var d in dtos)
                    {
                        _allCurrentResults.Add(ToModel(d));
                    }
                }
                catch { }
            }
        }
        else
        {
            if (_daemonService != null)
            {
                try
                {
                    var dtos = await _daemonService.SearchWithOptionsAsync(query, 200, IsFuzzy);
                    foreach (var d in dtos)
                    {
                        _allCurrentResults.Add(ToModel(d));
                    }
                }
                catch (Exception)
                {
                    _daemonService = null;
                    IsDaemonConnected = false;
                    StatusDaemon = "○ Daemon getrennt";
                }
            }

            if (_daemonService == null && _fallbackEngine.TotalFiles > 0)
            {
                var matches = _fallbackEngine.Search(query, new SearchOptions { MaxResults = 200, Fuzzy = IsFuzzy });
                foreach (var m in matches)
                {
                    _allCurrentResults.Add(ToModel(m));
                }
            }
        }

        sw.Stop();
        ApplyFilterAndRefreshList();
        StatusMiddle = $"{Results.Count:N0} Treffer ({sw.Elapsed.TotalMilliseconds:F1} ms)";
    }

    private void ApplyFilterAndRefreshList()
    {
        Results.Clear();

        IEnumerable<FileItemModel> filtered = _allCurrentResults;

        filtered = _activeFilter switch
        {
            "files" => filtered.Where(x => !x.IsDirectory),
            "dirs" => filtered.Where(x => x.IsDirectory),
            "docs" => filtered.Where(x => MatchesExt(x.Name, ".pdf", ".txt", ".md", ".doc", ".docx", ".odt", ".rtf")),
            "images" => filtered.Where(x => MatchesExt(x.Name, ".png", ".jpg", ".jpeg", ".webp", ".svg", ".gif", ".bmp")),
            "media" => filtered.Where(x => MatchesExt(x.Name, ".mp4", ".mkv", ".avi", ".webm", ".mov", ".mp3", ".flac", ".wav", ".ogg")),
            "code" => filtered.Where(x => MatchesExt(x.Name, ".cs", ".rs", ".py", ".js", ".ts", ".cpp", ".c", ".go", ".json", ".xml", ".html", ".css", ".sh")),
            "archives" => filtered.Where(x => MatchesExt(x.Name, ".zip", ".tar", ".gz", ".bz2", ".xz", ".7z", ".zst")),
            _ => filtered
        };

        foreach (var item in filtered)
        {
            Results.Add(item);
        }
    }

    private static bool MatchesExt(string name, params string[] extensions)
    {
        string ext = Path.GetExtension(name).ToLowerInvariant();
        return extensions.Contains(ext);
    }

    public void ClearSearch()
    {
        SearchText = string.Empty;
    }

    public void OpenFile(FileItemModel? item)
    {
        item ??= SelectedItem;
        if (item == null) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = $"\"{item.FullPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            StatusMiddle = $"Fehler beim Öffnen: {ex.Message}";
        }
    }

    public void OpenContainingFolder(FileItemModel? item)
    {
        item ??= SelectedItem;
        if (item == null) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "dolphin",
                Arguments = $"--select \"{item.FullPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            string dir = Path.GetDirectoryName(item.FullPath) ?? "/";
            Process.Start(new ProcessStartInfo("xdg-open", $"\"{dir}\"") { UseShellExecute = false });
        }
    }

    public void CopyPath(FileItemModel? item)
    {
        item ??= SelectedItem;
        if (item == null) return;

        CopyToClipboard(item.FullPath);
        StatusMiddle = "Pfad in Zwischenablage kopiert!";
    }

    public void CopyName(FileItemModel? item)
    {
        item ??= SelectedItem;
        if (item == null) return;

        CopyToClipboard(item.Name);
        StatusMiddle = "Dateiname kopiert!";
    }

    private static void CopyToClipboard(string text)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "wl-copy",
                Arguments = $"\"{text}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit(500);
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xclip",
                    Arguments = $"-selection clipboard -i",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch { }
        }
    }

    private static FileItemModel ToModel(SearchResult r) => new(
        r.FileName,
        Path.GetDirectoryName(r.FullPath) ?? "/",
        r.FullPath,
        r.Size,
        r.ModifiedTime,
        r.IsDirectory,
        r.Score
    );

    private static FileItemModel ToModel(SearchResultDto d) => new(
        d.FileName,
        Path.GetDirectoryName(d.FullPath) ?? "/",
        d.FullPath,
        d.Size,
        DateTimeOffset.FromUnixTimeSeconds(d.ModifiedTime),
        d.IsDirectory,
        d.Score
    );

    private void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }

    public void Dispose()
    {
        _debounceTimer.Stop();
    }
}
