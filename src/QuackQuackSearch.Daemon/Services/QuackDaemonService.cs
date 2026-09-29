using System.Diagnostics;
using QuackQuackSearch.Core.Config;
using QuackQuackSearch.Core.Crawler;
using QuackQuackSearch.Core.DBus;
using QuackQuackSearch.Core.Index;
using QuackQuackSearch.Core.Monitoring;
using QuackQuackSearch.Core.Search;
using QuackQuackSearch.Core.Storage;
using Tmds.DBus;

namespace QuackQuackSearch.Daemon.Services;

public sealed class QuackDaemonService : IDaemonService, IDisposable
{
    private readonly SearchIndexEngine _engine;
    private readonly QuackConfig _config;
    private readonly IgnoreMatcher _ignoreMatcher;
    private readonly LocalInotifyWatcher _inotifyWatcher;
    private readonly NetworkPollingScheduler _networkPoller;
    private readonly System.Timers.Timer _snapshotTimer;
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private bool _isDisposed;

    public ObjectPath ObjectPath => new("/org/quackquacksearch/Daemon");
    public SearchIndexEngine Engine => _engine;
    public QuackConfig Config => _config;

    public QuackDaemonService()
    {
        _config = ConfigManager.LoadOrCreateDefault();
        _engine = new SearchIndexEngine();
        _ignoreMatcher = new IgnoreMatcher(_config.Indexing.GlobalExcludes);
        _inotifyWatcher = new LocalInotifyWatcher(_engine, _ignoreMatcher);
        _networkPoller = new NetworkPollingScheduler(_engine, _ignoreMatcher);

        int intervalMinutes = Math.Max(1, _config.Indexing.SaveIntervalMinutes);
        _snapshotTimer = new System.Timers.Timer(TimeSpan.FromMinutes(intervalMinutes).TotalMilliseconds)
        {
            AutoReset = true
        };
        _snapshotTimer.Elapsed += async (_, _) => await SaveSnapshotSafeAsync();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("[Daemon] Checking for cached index snapshot...");
        bool loaded = await IndexSerializer.TryLoadAsync(_engine, cancellationToken: cancellationToken);

        if (loaded)
        {
            Console.WriteLine($"[Daemon] Restored {_engine.TotalFiles:N0} files from disk cache.");
        }
        else
        {
            Console.WriteLine("[Daemon] No valid cache found. Starting initial crawl...");
            await CrawlConfiguredPathsAsync(cancellationToken);
            await SaveSnapshotSafeAsync();
        }

        // Setup active monitoring for configured paths
        SetupMonitoring();

        // Start periodic snapshot timer
        _snapshotTimer.Start();
    }

    private void SetupMonitoring()
    {
        _networkPoller.SetPaths(_config.Paths);
        _networkPoller.Start();

        Task.Run(() =>
        {
            var localPaths = _config.Paths.Where(p => p.Type.Equals("local", StringComparison.OrdinalIgnoreCase) && p.Enabled);
            foreach (var p in localPaths)
            {
                if (Directory.Exists(p.Path))
                {
                    Console.WriteLine($"[Daemon] Registering inotify watch tree: {p.Path}...");
                    _inotifyWatcher.AddWatchTree(p.Path);
                }
            }
            Console.WriteLine($"[Daemon] Active inotify watches: {_inotifyWatcher.TotalActiveWatches:N0}");
        });
    }

    private async Task CrawlConfiguredPathsAsync(CancellationToken cancellationToken)
    {
        var crawler = new FastFileSystemCrawler(_ignoreMatcher, _config.Indexing.MaxParallelThreads);

        foreach (var pathEntry in _config.Paths.Where(p => p.Enabled))
        {
            if (Directory.Exists(pathEntry.Path))
            {
                Console.WriteLine($"[Daemon] Crawling: {pathEntry.Path}...");
                var sw = Stopwatch.StartNew();
                await crawler.CrawlAsync(pathEntry.Path, batch =>
                {
                    _engine.BulkAdd(batch);
                    return Task.CompletedTask;
                }, cancellationToken: cancellationToken);
                sw.Stop();
                Console.WriteLine($"[Daemon] Crawled {pathEntry.Path} in {sw.Elapsed.TotalSeconds:F2}s. Total files: {_engine.TotalFiles:N0}");
            }
        }
    }

    public Task<SearchResultDto[]> SearchAsync(string query, int maxResults)
    {
        return SearchWithOptionsAsync(query, maxResults, fuzzy: false);
    }

    public Task<SearchResultDto[]> SearchWithOptionsAsync(string query, int maxResults, bool fuzzy)
    {
        var options = new SearchOptions
        {
            MaxResults = maxResults > 0 ? maxResults : 50,
            IncludeHidden = _config.Indexing.IndexHiddenFiles,
            IncludeDirectories = true,
            Fuzzy = fuzzy
        };

        var results = _engine.Search(query, options);
        var dtos = results.Select(r => new SearchResultDto
        {
            FullPath = r.FullPath,
            FileName = r.FileName,
            Size = r.Size,
            ModifiedTime = (uint)r.ModifiedTime.ToUnixTimeSeconds(),
            IsDirectory = r.IsDirectory,
            Score = r.Score
        }).ToArray();

        return Task.FromResult(dtos);
    }

    public async Task<bool> AddPathAsync(string path, string type)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        string normalized = Path.GetFullPath(path);
        if (_config.Paths.Any(p => p.Path.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            return true;

        var entry = new PathConfigEntry
        {
            Path = normalized,
            Type = type.ToLowerInvariant() == "network" ? "network" : "local",
            Enabled = true
        };

        _config.Paths.Add(entry);
        ConfigManager.Save(_config);

        if (entry.Type == "local")
        {
            var crawler = new FastFileSystemCrawler(_ignoreMatcher);
            await crawler.CrawlAsync(normalized, batch =>
            {
                _engine.BulkAdd(batch);
                return Task.CompletedTask;
            });
            _inotifyWatcher.AddWatchTree(normalized);
        }
        else
        {
            _networkPoller.SetPaths(_config.Paths);
        }

        await SaveSnapshotSafeAsync();
        return true;
    }

    public async Task<bool> RemovePathAsync(string path)
    {
        string normalized = Path.GetFullPath(path);
        int removedCount = _config.Paths.RemoveAll(p => p.Path.Equals(normalized, StringComparison.OrdinalIgnoreCase));

        if (removedCount > 0)
        {
            ConfigManager.Save(_config);

            _inotifyWatcher.RemoveWatchTree(normalized);
            _engine.RemoveSubtree(normalized);
            _engine.Remove(normalized);
            _networkPoller.SetPaths(_config.Paths);

            await SaveSnapshotSafeAsync();
            Console.WriteLine($"[Daemon] Removed path and purged subtree: {normalized}. Remaining files: {_engine.TotalFiles:N0}");
            return true;
        }

        return false;
    }

    public async Task<bool> SetPathEnabledAsync(string path, bool enabled)
    {
        string normalized = Path.GetFullPath(path);
        var entry = _config.Paths.FirstOrDefault(p => p.Path.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return false;

        if (entry.Enabled == enabled) return true;

        entry.Enabled = enabled;
        ConfigManager.Save(_config);

        if (!enabled)
        {
            _inotifyWatcher.RemoveWatchTree(normalized);
            _engine.RemoveSubtree(normalized);
            _engine.Remove(normalized);
            _networkPoller.SetPaths(_config.Paths);
            await SaveSnapshotSafeAsync();
            Console.WriteLine($"[Daemon] Disabled path and purged subtree: {normalized}. Remaining files: {_engine.TotalFiles:N0}");
        }
        else
        {
            if (entry.Type == "local")
            {
                var crawler = new FastFileSystemCrawler(_ignoreMatcher);
                await crawler.CrawlAsync(normalized, batch =>
                {
                    _engine.BulkAdd(batch);
                    return Task.CompletedTask;
                });
                _inotifyWatcher.AddWatchTree(normalized);
            }
            else
            {
                _networkPoller.SetPaths(_config.Paths);
            }
            await SaveSnapshotSafeAsync();
            Console.WriteLine($"[Daemon] Enabled and re-indexed path: {normalized}. Total files: {_engine.TotalFiles:N0}");
        }

        return true;
    }

    public Task<PathInfoDto[]> ListPathsAsync()
    {
        var netStatuses = _networkPoller.GetStatuses().ToDictionary(s => s.Path, s => s.State.ToString());

        var infos = _config.Paths.Select(p =>
        {
            string status = "Active";
            if (!p.Enabled) status = "Disabled";
            else if (p.Type == "network" && netStatuses.TryGetValue(p.Path, out string? netState))
            {
                status = netState;
            }

            return new PathInfoDto
            {
                Path = p.Path,
                Type = p.Type,
                Enabled = p.Enabled,
                Status = status
            };
        }).ToArray();

        return Task.FromResult(infos);
    }

    public Task<DaemonStatusDto> GetStatusAsync()
    {
        var status = new DaemonStatusDto
        {
            TotalFiles = _engine.TotalFiles,
            TotalDirectories = _engine.TotalDirectories,
            ActiveWatches = _inotifyWatcher.TotalActiveWatches,
            MemoryBytes = GC.GetTotalMemory(false),
            UptimeSeconds = (long)(DateTimeOffset.UtcNow - _startTime).TotalSeconds
        };

        return Task.FromResult(status);
    }

    public async Task TriggerRescanAsync(string path)
    {
        string targetPath = string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path);

        if (string.IsNullOrEmpty(targetPath))
        {
            _engine.Clear();
            await CrawlConfiguredPathsAsync(CancellationToken.None);
        }
        else if (Directory.Exists(targetPath))
        {
            var crawler = new FastFileSystemCrawler(_ignoreMatcher);
            await crawler.CrawlAsync(targetPath, batch =>
            {
                _engine.BulkAdd(batch);
                return Task.CompletedTask;
            });
        }

        await SaveSnapshotSafeAsync();
    }

    public async Task SaveSnapshotSafeAsync()
    {
        try
        {
            await IndexSerializer.SaveAsync(_engine);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Daemon] Failed to save snapshot: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _snapshotTimer.Stop();
        _snapshotTimer.Dispose();

        _inotifyWatcher.Dispose();
        _networkPoller.Dispose();

        SaveSnapshotSafeAsync().GetAwaiter().GetResult();
    }
}
