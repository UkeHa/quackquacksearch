using QuackQuackSearch.Core.Config;
using QuackQuackSearch.Core.Crawler;
using QuackQuackSearch.Core.Index;

namespace QuackQuackSearch.Core.Monitoring;

public enum NetworkShareState
{
    Idle,
    Scanning,
    Offline,
    Error
}

public sealed class NetworkShareStatus(string path)
{
    public string Path { get; } = path;
    public NetworkShareState State { get; set; } = NetworkShareState.Idle;
    public DateTimeOffset LastSuccessfulPoll { get; set; }
    public string? LastError { get; set; }
    public long CurrentCrawlFiles { get; set; }
    public string CurrentCrawlDirectory { get; set; } = string.Empty;
}

internal sealed class DirectProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}

public sealed class NetworkPollingScheduler : IDisposable
{
    private readonly SearchIndexEngine _engine;
    private volatile IgnoreMatcher _ignoreMatcher;
    private readonly List<PathConfigEntry> _networkPaths = [];
    private readonly Dictionary<string, NetworkShareStatus> _statuses = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    private CancellationTokenSource? _cts;
    private Task? _pollLoopTask;
    private bool _isDisposed;

    public event Action<string, NetworkShareState, string?>? OnStatusChanged;
    public event Action<string>? OnPollCompleted;

    public NetworkPollingScheduler(SearchIndexEngine engine, IgnoreMatcher? ignoreMatcher = null)
    {
        _engine = engine;
        _ignoreMatcher = ignoreMatcher ?? IgnoreMatcher.Default;
    }

    public void UpdateIgnoreMatcher(IgnoreMatcher ignoreMatcher)
    {
        ArgumentNullException.ThrowIfNull(ignoreMatcher);
        _ignoreMatcher = ignoreMatcher;
    }

    public void SetPaths(IEnumerable<PathConfigEntry> paths)
    {
        lock (_lock)
        {
            _networkPaths.Clear();
            var activeSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in paths.Where(p => p.Type.Equals("network", StringComparison.OrdinalIgnoreCase) && p.Enabled))
            {
                _networkPaths.Add(p);
                activeSet.Add(p.Path);
                if (!_statuses.ContainsKey(p.Path))
                {
                    _statuses[p.Path] = new NetworkShareStatus(p.Path);
                }
            }

            var toRemove = _statuses.Keys.Where(k => !activeSet.Contains(k)).ToList();
            foreach (var k in toRemove)
            {
                _statuses.Remove(k);
            }
        }
    }

    public IReadOnlyList<NetworkShareStatus> GetStatuses()
    {
        lock (_lock)
        {
            return _statuses.Values.Select(s => new NetworkShareStatus(s.Path)
            {
                State = s.State,
                LastSuccessfulPoll = s.LastSuccessfulPoll,
                LastError = s.LastError,
                CurrentCrawlFiles = s.CurrentCrawlFiles,
                CurrentCrawlDirectory = s.CurrentCrawlDirectory
            }).ToArray();
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_isDisposed || _pollLoopTask != null) return;

            _cts = new CancellationTokenSource();
            _pollLoopTask = Task.Run(() => RunPollLoopAsync(_cts.Token));
        }
    }

    private async Task RunPollLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            List<PathConfigEntry> currentPaths;
            lock (_lock)
            {
                currentPaths = [.. _networkPaths];
            }

            foreach (var pathEntry in currentPaths)
            {
                if (cancellationToken.IsCancellationRequested) break;
                await ProcessPathWithTimeoutAsync(pathEntry, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ProcessPathWithTimeoutAsync(PathConfigEntry entry, CancellationToken cancellationToken)
    {
        int intervalSec = entry.PollIntervalSeconds > 0 ? entry.PollIntervalSeconds : 60;
        NetworkShareStatus status;
        lock (_lock)
        {
            if (!_statuses.TryGetValue(entry.Path, out status!))
            {
                status = new NetworkShareStatus(entry.Path);
                _statuses[entry.Path] = status;
            }

            if (status.State == NetworkShareState.Idle &&
                (DateTimeOffset.UtcNow - status.LastSuccessfulPoll).TotalSeconds < intervalSec)
            {
                return;
            }
        }

        int reachabilityTimeoutSec = entry.TimeoutSeconds > 0 ? entry.TimeoutSeconds : 5;
        var inactivityTimeout = TimeSpan.FromSeconds(Math.Max(reachabilityTimeoutSec * 6, 30));

        try
        {
            lock (_lock)
            {
                status.CurrentCrawlFiles = 0;
                status.CurrentCrawlDirectory = entry.Path;
            }
            UpdateStatus(status, NetworkShareState.Scanning, null);

            using (var reachabilityCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                reachabilityCts.CancelAfter(TimeSpan.FromSeconds(reachabilityTimeoutSec));
                await Task.Run(() =>
                {
                    if (!Directory.Exists(entry.Path))
                    {
                        throw new DirectoryNotFoundException($"Network path inaccessible: {entry.Path}");
                    }
                    // Touch root directory enumeration to trigger any systemd automount within reachability timeout
                    using var enumerator = Directory.EnumerateFileSystemEntries(entry.Path).GetEnumerator();
                    _ = enumerator.MoveNext();
                }, reachabilityCts.Token).ConfigureAwait(false);
            }

            using var crawlCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            crawlCts.CancelAfter(inactivityTimeout);

            var progress = new DirectProgress<CrawlProgress>(p =>
            {
                try { crawlCts.CancelAfter(inactivityTimeout); } catch (ObjectDisposedException) { }
                lock (_lock)
                {
                    status.CurrentCrawlFiles = p.FilesFound;
                    status.CurrentCrawlDirectory = p.CurrentDirectory;
                }
            });

            await Task.Run(async () =>
            {
                var stagedItems = new List<DiscoveredItem>();
                var stageLock = new Lock();
                var crawler = new FastFileSystemCrawler(_ignoreMatcher, maxDegreeOfParallelism: 4);
                await crawler.CrawlAsync(entry.Path, batch =>
                {
                    try { crawlCts.CancelAfter(inactivityTimeout); } catch (ObjectDisposedException) { }
                    long count;
                    lock (stageLock)
                    {
                        stagedItems.AddRange(batch);
                        count = stagedItems.Count;
                    }
                    _engine.BulkAdd(batch);
                    lock (_lock)
                    {
                        status.CurrentCrawlFiles = count;
                    }
                    return Task.CompletedTask;
                }, progress: progress, cancellationToken: crawlCts.Token).ConfigureAwait(false);

                crawlCts.Token.ThrowIfCancellationRequested();
                _engine.ReplaceSubtree(entry.Path, stagedItems);
            }, crawlCts.Token).ConfigureAwait(false);

            lock (_lock)
            {
                status.LastSuccessfulPoll = DateTimeOffset.UtcNow;
                status.CurrentCrawlFiles = 0;
                status.CurrentCrawlDirectory = string.Empty;
            }
            UpdateStatus(status, NetworkShareState.Idle, null);
            OnPollCompleted?.Invoke(entry.Path);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            UpdateStatus(status, NetworkShareState.Offline, $"Timeout ({reachabilityTimeoutSec}s). Share is unreachable or slow.");
        }
        catch (Exception ex)
        {
            UpdateStatus(status, NetworkShareState.Offline, ex.Message);
        }
    }

    private void UpdateStatus(NetworkShareStatus status, NetworkShareState newState, string? error)
    {
        lock (_lock)
        {
            status.State = newState;
            status.LastError = error;
        }
        OnStatusChanged?.Invoke(status.Path, newState, error);
    }

    public void Dispose()
    {
        CancellationTokenSource? cts;
        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            cts = _cts;
            _cts = null;
        }

        try { cts?.Cancel(); } catch { }
        try { _pollLoopTask?.Wait(TimeSpan.FromMilliseconds(500)); } catch { }
        cts?.Dispose();
    }
}
