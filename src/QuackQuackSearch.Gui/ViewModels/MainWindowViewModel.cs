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
using QuackQuackSearch.Gui.Localization;
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
    private string _statusLeft = Loc.Instance.StatusReady;
    private string _statusMiddle = string.Empty;
    private string _statusDaemon = Loc.Instance.StatusCheckingDaemon;
    private bool _isDaemonConnected;
    private bool _isLoading;
    private FileItemModel? _selectedItem;

    private readonly List<FileItemModel> _allCurrentResults = [];

    public Loc Loc => Loc.Instance;

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
            _daemonService = await DaemonClient.TryGetServiceAsync();

            if (_daemonService != null)
            {
                var status = await _daemonService.GetStatusAsync();
                IsDaemonConnected = true;
                StatusDaemon = Loc.StatusDaemonConnected;
                StatusLeft = Loc.FormatTotalFilesAndDirs(status.TotalFiles, status.TotalDirectories);
            }
            else
            {
                StatusDaemon = Loc.StatusDaemonInactiveCache;
                bool loaded = await IndexSerializer.TryLoadAsync(_fallbackEngine);
                if (loaded)
                {
                    StatusLeft = Loc.FormatOfflineCacheFiles(_fallbackEngine.TotalFiles);
                }
                else
                {
                    StatusLeft = Loc.StatusNoDaemonOrCache;
                }
            }
        }
        catch (Exception ex)
        {
            StatusDaemon = Loc.StatusStandalone;
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

    private int _searchVersion;

    public async Task ExecuteSearchAsync()
    {
        int myVersion = Interlocked.Increment(ref _searchVersion);
        string query = SearchText.Trim();
        var sw = Stopwatch.StartNew();

        var fetchedResults = new List<FileItemModel>();

        if (string.IsNullOrEmpty(query))
        {
            if (_daemonService != null)
            {
                try
                {
                    var dtos = await _daemonService.SearchAsync(string.Empty, 100);
                    foreach (var d in dtos)
                    {
                        fetchedResults.Add(ToModel(d));
                    }
                }
                catch { }
            }
            else if (_fallbackEngine.TotalFiles > 0)
            {
                var recent = _fallbackEngine.GetRecentEntries(100);
                foreach (var r in recent)
                {
                    fetchedResults.Add(ToModel(r));
                }
            }
        }
        else
        {
            if (_daemonService != null)
            {
                try
                {
                    var dtos = await _daemonService.SearchWithOptionsAsync(query, 200, fuzzy: true);
                    foreach (var d in dtos)
                    {
                        fetchedResults.Add(ToModel(d));
                    }
                }
                catch (Exception)
                {
                    _daemonService = null;
                    IsDaemonConnected = false;
                    StatusDaemon = Loc.StatusDaemonDisconnected;
                }
            }

            if (_daemonService == null && _fallbackEngine.TotalFiles > 0)
            {
                var matches = _fallbackEngine.Search(query, new SearchOptions { MaxResults = 200, Fuzzy = true });
                foreach (var m in matches)
                {
                    fetchedResults.Add(ToModel(m));
                }
            }
        }

        if (myVersion != Volatile.Read(ref _searchVersion))
        {
            return;
        }

        sw.Stop();
        _allCurrentResults.Clear();
        _allCurrentResults.AddRange(fetchedResults);
        ApplyFilterAndRefreshList();
        StatusMiddle = Loc.FormatSearchHits(Results.Count, sw.Elapsed.TotalMilliseconds);
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

        if (!Core.System.DesktopActions.TryOpenFile(item.FullPath, out string? error))
        {
            StatusMiddle = Loc.FormatOpenError(error);
        }
    }

    public void OpenContainingFolder(FileItemModel? item)
    {
        item ??= SelectedItem;
        if (item == null) return;

        Core.System.DesktopActions.TryOpenContainingFolder(item.FullPath, out _);
    }

    public void CopyPath(FileItemModel? item)
    {
        item ??= SelectedItem;
        if (item == null) return;

        Core.System.DesktopActions.TryCopyToClipboard(item.FullPath);
        StatusMiddle = Loc.PathCopied;
    }

    public void CopyName(FileItemModel? item)
    {
        item ??= SelectedItem;
        if (item == null) return;

        Core.System.DesktopActions.TryCopyToClipboard(item.Name);
        StatusMiddle = Loc.NameCopied;
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
        _fallbackEngine.Dispose();
    }
}
