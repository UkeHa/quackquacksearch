using System.Collections.Concurrent;
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
    private volatile IgnoreMatcher _ignoreMatcher;
    private readonly LocalInotifyWatcher _inotifyWatcher;
    private readonly NetworkPollingScheduler _networkPoller;
    private readonly ConcurrentDictionary<string, CrawlProgress> _activeCrawls = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _daemonCts = new();
    private Task? _snapshotLoopTask;
    private readonly SemaphoreSlim _configLock = new(1, 1);
    private readonly SemaphoreSlim _snapshotLock = new(1, 1);
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private volatile bool _isDisposed;
    private int _isRescanningOverflow;

    public ObjectPath ObjectPath => new("/org/quackquacksearch/Daemon");
    public SearchIndexEngine Engine => _engine;
    public QuackConfig Config => _config;

    public QuackDaemonService()
    {
        _config = ConfigManager.LoadOrCreateDefault();
        _engine = new SearchIndexEngine();
        _ignoreMatcher = CreateCombinedIgnoreMatcher(_config);
        _inotifyWatcher = new LocalInotifyWatcher(_engine, _ignoreMatcher);
        _inotifyWatcher.OnError += (src, msg) => Console.WriteLine($"[Daemon] Watcher warning ({src}): {msg}");
        _inotifyWatcher.OnQueueOverflow += HandleInotifyQueueOverflow;
        _networkPoller = new NetworkPollingScheduler(_engine, _ignoreMatcher);
        _networkPoller.OnPollCompleted += path => { _ = SaveSnapshotSafeAsync(); };
    }

    private void HandleInotifyQueueOverflow()
    {
        if (_isDisposed || Interlocked.CompareExchange(ref _isRescanningOverflow, 1, 0) != 0)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                Console.WriteLine("[Daemon] Performing background rescan after inotify queue overflow...");
                await CrawlConfiguredPathsAsync(_daemonCts.Token);
                await SaveSnapshotSafeAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Daemon] Overflow rescan failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _isRescanningOverflow, 0);
            }
        });
    }

    private async Task RunPeriodicSnapshotLoopAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await SaveSnapshotSafeAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
    }

    private static IgnoreMatcher CreateCombinedIgnoreMatcher(QuackConfig config)
    {
        var pathExcludes = config.Paths
            .Where(p => p.CustomExcludes != null && p.CustomExcludes.Count > 0)
            .Select(p => (p.Path, (IEnumerable<string>)p.CustomExcludes));

        return new IgnoreMatcher(config.Indexing.GlobalExcludes, pathExcludes);
    }

    private void RefreshIgnoreMatcherUnsafe()
    {
        var matcher = CreateCombinedIgnoreMatcher(_config);
        _ignoreMatcher = matcher;
        _inotifyWatcher.UpdateIgnoreMatcher(matcher);
        _networkPoller.UpdateIgnoreMatcher(matcher);
    }

    private void SaveConfigUnsafe()
    {
        // Sync Appearance from disk in case the GUI changed ThemeMode while Daemon was running
        try
        {
            string filePath = ConfigManager.GetConfigFilePath();
            if (File.Exists(filePath))
            {
                var diskConfig = ConfigManager.LoadOrCreateDefault();
                _config.Appearance = diskConfig.Appearance;
            }
        }
        catch { }

        ConfigManager.Save(_config);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("[Daemon] Checking for cached index snapshot...");
        bool loaded = await IndexSerializer.TryLoadAsync(_engine, cancellationToken: cancellationToken);

        if (loaded)
        {
            Console.WriteLine($"[Daemon] Restored {_engine.TotalFiles:N0} files from disk cache.");
            int purged = _engine.PurgeIgnored(_ignoreMatcher);
            if (purged > 0)
            {
                Console.WriteLine($"[Daemon] Purged {purged:N0} cached files matching active ignore rules.");
            }
        }
        else
        {
            Console.WriteLine("[Daemon] No valid cache found. Starting initial crawl...");
            await CrawlConfiguredPathsAsync(cancellationToken);
            await SaveSnapshotSafeAsync();
        }

        // Setup active monitoring for configured paths
        SetupMonitoring();

        // Start periodic snapshot loop
        int intervalMinutes = Math.Max(1, _config.Indexing.SaveIntervalMinutes);
        _snapshotLoopTask = Task.Run(() => RunPeriodicSnapshotLoopAsync(TimeSpan.FromMinutes(intervalMinutes), _daemonCts.Token));
    }

    private void SetupMonitoring()
    {
        List<PathConfigEntry> pathsSnapshot;
        _configLock.Wait();
        try
        {
            pathsSnapshot = [.. _config.Paths];
        }
        finally
        {
            _configLock.Release();
        }

        _networkPoller.SetPaths(pathsSnapshot);
        _networkPoller.Start();

        _ = Task.Run(() =>
        {
            var localPaths = pathsSnapshot.Where(p => p.Type.Equals("local", StringComparison.OrdinalIgnoreCase) && p.Enabled);
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

    private sealed class SyncCrawlProgress(Action<CrawlProgress> callback) : IProgress<CrawlProgress>
    {
        public void Report(CrawlProgress value) => callback(value);
    }

    private async Task CrawlPathWithProgressAsync(
        string normalizedPath,
        Func<IReadOnlyList<DiscoveredItem>, Task> onBatch,
        CancellationToken cancellationToken = default)
    {
        _activeCrawls[normalizedPath] = new CrawlProgress(0, 0, normalizedPath);
        try
        {
            var crawler = new FastFileSystemCrawler(_ignoreMatcher, _config.Indexing.MaxParallelThreads);
            var progress = new SyncCrawlProgress(p => _activeCrawls[normalizedPath] = p);
            await crawler.CrawlAsync(normalizedPath, onBatch, progress, cancellationToken);
        }
        finally
        {
            _activeCrawls.TryRemove(normalizedPath, out _);
        }
    }

    private async Task CrawlConfiguredPathsAsync(CancellationToken cancellationToken)
    {
        List<PathConfigEntry> enabledPaths;
        await _configLock.WaitAsync(cancellationToken);
        try
        {
            enabledPaths = _config.Paths.Where(p => p.Enabled).ToList();
        }
        finally
        {
            _configLock.Release();
        }

        foreach (var pathEntry in enabledPaths)
        {
            if (Directory.Exists(pathEntry.Path))
            {
                Console.WriteLine($"[Daemon] Crawling: {pathEntry.Path}...");
                var sw = Stopwatch.StartNew();
                await CrawlPathWithProgressAsync(pathEntry.Path, batch =>
                {
                    _engine.BulkAdd(batch);
                    return Task.CompletedTask;
                }, cancellationToken);
                sw.Stop();
                Console.WriteLine($"[Daemon] Crawled {pathEntry.Path} in {sw.Elapsed.TotalSeconds:F2}s. Total files: {_engine.TotalFiles:N0}");
            }
        }
    }

    public Task<SearchResultDto[]> SearchAsync(string query, int maxResults)
    {
        return SearchWithOptionsAsync(query, maxResults, fuzzy: true);
    }

    public Task<SearchResultDto[]> SearchWithOptionsAsync(string query, int maxResults, bool fuzzy)
    {
        int limit = maxResults > 0 ? maxResults : 50;
        IReadOnlyList<SearchResult> results;

        if (string.IsNullOrWhiteSpace(query))
        {
            results = _engine.GetRecentEntries(limit, _config.Indexing.IndexHiddenFiles, includeDirectories: true);
        }
        else
        {
            var options = new SearchOptions
            {
                MaxResults = limit,
                IncludeHidden = _config.Indexing.IndexHiddenFiles,
                IncludeDirectories = true,
                Fuzzy = fuzzy
            };
            results = _engine.Search(query, options);
        }

        var dtos = results.Select(r => new SearchResultDto
        {
            FullPath = r.FullPath,
            FileName = r.FileName,
            Size = r.Size,
            ModifiedTime = (uint)Math.Clamp(r.ModifiedTime.ToUnixTimeSeconds(), 0L, uint.MaxValue),
            IsDirectory = r.IsDirectory,
            Score = r.Score
        }).ToArray();

        return Task.FromResult(dtos);
    }

    public async Task<bool> AddPathAsync(string path, string type)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        string normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (string.IsNullOrEmpty(normalized)) normalized = "/";

        PathConfigEntry entry;
        List<PathConfigEntry> pathsSnapshot;

        await _configLock.WaitAsync();
        try
        {
            if (_config.Paths.Any(p => Path.GetFullPath(p.Path).TrimEnd(Path.DirectorySeparatorChar).Equals(normalized, StringComparison.Ordinal)))
                return true;

            entry = new PathConfigEntry
            {
                Path = normalized,
                Type = type.Equals("network", StringComparison.OrdinalIgnoreCase) ? "network" : "local",
                Enabled = true
            };

            _config.Paths.Add(entry);
            SaveConfigUnsafe();
            RefreshIgnoreMatcherUnsafe();
            pathsSnapshot = [.. _config.Paths];
        }
        finally
        {
            _configLock.Release();
        }

        if (entry.Type == "local")
        {
            await CrawlPathWithProgressAsync(normalized, batch =>
            {
                _engine.BulkAdd(batch);
                return Task.CompletedTask;
            });
            _inotifyWatcher.AddWatchTree(normalized);
        }
        else
        {
            _networkPoller.SetPaths(pathsSnapshot);
        }

        await SaveSnapshotSafeAsync();
        return true;
    }

    public async Task<bool> RemovePathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        string normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (string.IsNullOrEmpty(normalized)) normalized = "/";

        int removedCount;
        List<PathConfigEntry> pathsSnapshot;

        await _configLock.WaitAsync();
        try
        {
            removedCount = _config.Paths.RemoveAll(p =>
                Path.GetFullPath(p.Path).TrimEnd(Path.DirectorySeparatorChar).Equals(normalized, StringComparison.Ordinal));
            if (removedCount > 0)
            {
                SaveConfigUnsafe();
                RefreshIgnoreMatcherUnsafe();
            }
            pathsSnapshot = [.. _config.Paths];
        }
        finally
        {
            _configLock.Release();
        }

        if (removedCount > 0)
        {
            _inotifyWatcher.RemoveWatchTree(normalized);
            _engine.RemoveSubtree(normalized);
            _engine.Remove(normalized);
            _networkPoller.SetPaths(pathsSnapshot);

            await SaveSnapshotSafeAsync();
            Console.WriteLine($"[Daemon] Removed path and purged subtree: {normalized}. Remaining files: {_engine.TotalFiles:N0}");
            return true;
        }

        return false;
    }

    public async Task<bool> SetPathEnabledAsync(string path, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        string normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (string.IsNullOrEmpty(normalized)) normalized = "/";

        string entryType;
        List<PathConfigEntry> pathsSnapshot;

        await _configLock.WaitAsync();
        try
        {
            var entry = FindEntryUnsafe(normalized);
            if (entry == null) return false;
            if (entry.Enabled == enabled) return true;

            entry.Enabled = enabled;
            entryType = entry.Type;
            SaveConfigUnsafe();
            pathsSnapshot = [.. _config.Paths];
        }
        finally
        {
            _configLock.Release();
        }

        if (!enabled)
        {
            _inotifyWatcher.RemoveWatchTree(normalized);
            _engine.RemoveSubtree(normalized);
            _engine.Remove(normalized);
            _networkPoller.SetPaths(pathsSnapshot);
            await SaveSnapshotSafeAsync();
            Console.WriteLine($"[Daemon] Disabled path and purged subtree: {normalized}. Remaining files: {_engine.TotalFiles:N0}");
        }
        else
        {
            if (entryType == "local")
            {
                await CrawlPathWithProgressAsync(normalized, batch =>
                {
                    _engine.BulkAdd(batch);
                    return Task.CompletedTask;
                });
                _inotifyWatcher.AddWatchTree(normalized);
            }
            else
            {
                _networkPoller.SetPaths(pathsSnapshot);
            }
            await SaveSnapshotSafeAsync();
            Console.WriteLine($"[Daemon] Enabled and re-indexed path: {normalized}. Total files: {_engine.TotalFiles:N0}");
        }

        return true;
    }

    public async Task<PathInfoDto[]> ListPathsAsync()
    {
        var netStatuses = _networkPoller.GetStatuses().ToDictionary(s => s.Path, s => s, StringComparer.Ordinal);

        await _configLock.WaitAsync();
        try
        {
            return _config.Paths.Select(p =>
            {
                string normPath = Path.GetFullPath(p.Path).TrimEnd(Path.DirectorySeparatorChar);
                if (string.IsNullOrEmpty(normPath)) normPath = "/";

                string status = "Active";
                long currentFiles = 0;
                string currentDir = string.Empty;

                if (!p.Enabled)
                {
                    status = "Disabled";
                }
                else if (_activeCrawls.TryGetValue(normPath, out var activeProg) || _activeCrawls.TryGetValue(p.Path, out activeProg))
                {
                    status = "Scanning";
                    currentFiles = activeProg.FilesFound;
                    currentDir = activeProg.CurrentDirectory ?? string.Empty;
                }
                else if (p.Type == "network" && (netStatuses.TryGetValue(p.Path, out var netStatus) || netStatuses.TryGetValue(normPath, out netStatus)))
                {
                    status = netStatus.State.ToString();
                    currentFiles = netStatus.CurrentCrawlFiles;
                    currentDir = netStatus.CurrentCrawlDirectory ?? string.Empty;
                }

                return new PathInfoDto
                {
                    Path = p.Path ?? string.Empty,
                    Type = p.Type ?? "local",
                    Enabled = p.Enabled,
                    Status = status,
                    ExcludesCsv = string.Join(";", p.CustomExcludes ?? []),
                    CurrentCrawlFiles = currentFiles,
                    CurrentCrawlDirectory = currentDir
                };
            }).ToArray();
        }
        finally
        {
            _configLock.Release();
        }
    }

    private PathConfigEntry? FindEntryUnsafe(string rootPath)
    {
        string normTrimmed = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar);
        if (string.IsNullOrEmpty(normTrimmed)) normTrimmed = "/";
        return _config.Paths.FirstOrDefault(p =>
        {
            string pNorm = Path.GetFullPath(p.Path).TrimEnd(Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(pNorm)) pNorm = "/";
            return pNorm.Equals(normTrimmed, StringComparison.Ordinal);
        });
    }

    public async Task<bool> AddExcludeAsync(string rootPath, string excludePattern)
    {
        if (string.IsNullOrWhiteSpace(excludePattern)) return false;

        bool added = false;
        string entryPath = string.Empty;

        await _configLock.WaitAsync();
        try
        {
            var entry = FindEntryUnsafe(rootPath);
            if (entry == null) return false;

            if (!PathExclusionValidator.IsValid(entry.Path, excludePattern, out string? error))
            {
                Console.WriteLine($"[Daemon] Rejected exclude '{excludePattern}' for '{entry.Path}': {error}");
                return false;
            }

            entryPath = entry.Path;
            entry.CustomExcludes ??= [];
            if (!entry.CustomExcludes.Contains(excludePattern, StringComparer.Ordinal))
            {
                entry.CustomExcludes.Add(excludePattern);
                SaveConfigUnsafe();
                RefreshIgnoreMatcherUnsafe();
                added = true;
            }
        }
        finally
        {
            _configLock.Release();
        }

        if (added)
        {
            int purged = _engine.PurgeIgnored(_ignoreMatcher);
            Console.WriteLine($"[Daemon] Added exclude '{excludePattern}' to '{entryPath}'. Purged {purged:N0} indexed entries.");
            await SaveSnapshotSafeAsync();
        }

        return true;
    }

    public async Task<bool> RemoveExcludeAsync(string rootPath, string excludePattern)
    {
        if (string.IsNullOrWhiteSpace(excludePattern)) return false;

        string? entryPath = null;
        string? entryType = null;

        await _configLock.WaitAsync();
        try
        {
            var entry = FindEntryUnsafe(rootPath);
            if (entry == null || entry.CustomExcludes == null) return false;

            int removed = entry.CustomExcludes.RemoveAll(x => x.Equals(excludePattern, StringComparison.Ordinal));
            if (removed > 0)
            {
                SaveConfigUnsafe();
                RefreshIgnoreMatcherUnsafe();
                entryPath = entry.Path;
                entryType = entry.Type;
            }
        }
        finally
        {
            _configLock.Release();
        }

        if (entryPath != null)
        {
            _ = Task.Run(async () =>
            {
                await CrawlPathWithProgressAsync(entryPath, batch =>
                {
                    _engine.BulkAdd(batch);
                    return Task.CompletedTask;
                });
                if (string.Equals(entryType, "local", StringComparison.OrdinalIgnoreCase))
                {
                    _inotifyWatcher.AddWatchTree(entryPath);
                }
                await SaveSnapshotSafeAsync();
                Console.WriteLine($"[Daemon] Removed exclude '{excludePattern}' from '{entryPath}' and re-indexed path.");
            });

            return true;
        }

        return false;
    }

    public async Task<string[]> GetPathExcludesAsync(string rootPath)
    {
        await _configLock.WaitAsync();
        try
        {
            var entry = FindEntryUnsafe(rootPath);
            return entry?.CustomExcludes?.ToArray() ?? Array.Empty<string>();
        }
        finally
        {
            _configLock.Release();
        }
    }

    public async Task<bool> SetPathExcludesAsync(string rootPath, string[] excludes)
    {
        string entryPath;
        string entryType;

        await _configLock.WaitAsync();
        try
        {
            var entry = FindEntryUnsafe(rootPath);
            if (entry == null) return false;

            entryPath = entry.Path;
            entryType = entry.Type;
            entry.CustomExcludes = excludes
                .Where(e => !string.IsNullOrWhiteSpace(e) && PathExclusionValidator.IsValid(entry.Path, e, out _))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            SaveConfigUnsafe();
            RefreshIgnoreMatcherUnsafe();
        }
        finally
        {
            _configLock.Release();
        }

        _engine.PurgeIgnored(_ignoreMatcher);

        _ = Task.Run(async () =>
        {
            await CrawlPathWithProgressAsync(entryPath, batch =>
            {
                _engine.BulkAdd(batch);
                return Task.CompletedTask;
            });
            if (string.Equals(entryType, "local", StringComparison.OrdinalIgnoreCase))
            {
                _inotifyWatcher.AddWatchTree(entryPath);
            }
            await SaveSnapshotSafeAsync();
            Console.WriteLine($"[Daemon] Updated excludes for '{entryPath}' and refreshed index.");
        });

        return true;
    }

    public Task<DaemonStatusDto> GetStatusAsync()
    {
        bool isIndexing = false;
        string activePath = string.Empty;
        long currentCrawlFiles = 0;

        var activeLocal = _activeCrawls.FirstOrDefault();
        if (activeLocal.Key != null)
        {
            isIndexing = true;
            activePath = activeLocal.Key;
            currentCrawlFiles = activeLocal.Value.FilesFound;
        }
        else
        {
            var scanningNet = _networkPoller.GetStatuses().FirstOrDefault(s => s.State == NetworkShareState.Scanning);
            if (scanningNet != null)
            {
                isIndexing = true;
                activePath = scanningNet.Path ?? string.Empty;
                currentCrawlFiles = scanningNet.CurrentCrawlFiles;
            }
        }

        var status = new DaemonStatusDto
        {
            TotalFiles = _engine.TotalFiles,
            TotalDirectories = _engine.TotalDirectories,
            ActiveWatches = _inotifyWatcher.TotalActiveWatches,
            MemoryBytes = GC.GetTotalMemory(false),
            UptimeSeconds = (long)(DateTimeOffset.UtcNow - _startTime).TotalSeconds,
            IsIndexing = isIndexing,
            ActiveIndexingPath = activePath,
            CurrentCrawlFiles = currentCrawlFiles
        };

        return Task.FromResult(status);
    }

    public async Task TriggerRescanAsync(string path)
    {
        string targetPath = string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

        if (string.IsNullOrEmpty(targetPath))
        {
            _engine.Clear();
            await CrawlConfiguredPathsAsync(CancellationToken.None);
        }
        else if (Directory.Exists(targetPath))
        {
            bool isAllowed;
            await _configLock.WaitAsync();
            try
            {
                isAllowed = _config.Paths.Any(p =>
                {
                    if (!p.Enabled) return false;
                    string root = Path.GetFullPath(p.Path).TrimEnd(Path.DirectorySeparatorChar);
                    return targetPath.Equals(root, StringComparison.Ordinal) ||
                           targetPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
                });
            }
            finally
            {
                _configLock.Release();
            }

            if (!isAllowed)
            {
                Console.WriteLine($"[Daemon] Rejected rescan for unconfigured path: {targetPath}");
                return;
            }

            var stagedItems = new List<DiscoveredItem>();
            var stageLock = new Lock();
            await CrawlPathWithProgressAsync(targetPath, batch =>
            {
                lock (stageLock)
                {
                    stagedItems.AddRange(batch);
                }
                return Task.CompletedTask;
            });

            _engine.ReplaceSubtree(targetPath, stagedItems);
            _inotifyWatcher.AddWatchTree(targetPath);
        }

        await SaveSnapshotSafeAsync();
    }

    public async Task SaveSnapshotSafeAsync()
    {
        if (_isDisposed) return;

        bool acquired = false;
        try
        {
            acquired = await _snapshotLock.WaitAsync(0);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (!acquired)
        {
            // A snapshot save is already in progress
            return;
        }

        try
        {
            if (!_isDisposed)
            {
                await IndexSerializer.SaveAsync(_engine);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Daemon] Failed to save snapshot: {ex.Message}");
        }
        finally
        {
            try { _snapshotLock.Release(); } catch (ObjectDisposedException) { }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try { _daemonCts.Cancel(); } catch { }
        try { _snapshotLoopTask?.Wait(TimeSpan.FromMilliseconds(500)); } catch { }
        _daemonCts.Dispose();

        _inotifyWatcher.Dispose();
        _networkPoller.Dispose();

        _snapshotLock.Wait();
        try
        {
            IndexSerializer.SaveAsync(_engine).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Daemon] Failed to save final snapshot: {ex.Message}");
        }
        finally
        {
            _snapshotLock.Release();
        }

        _engine.Dispose();
        _configLock.Dispose();
        _snapshotLock.Dispose();
    }
}
