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
}

public sealed class NetworkPollingScheduler : IDisposable
{
    private readonly SearchIndexEngine _engine;
    private readonly IgnoreMatcher _ignoreMatcher;
    private readonly List<PathConfigEntry> _networkPaths = [];
    private readonly Dictionary<string, NetworkShareStatus> _statuses = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    private CancellationTokenSource? _cts;
    private Task? _pollLoopTask;
    private bool _isDisposed;

    public event Action<string, NetworkShareState, string?>? OnStatusChanged;

    public NetworkPollingScheduler(SearchIndexEngine engine, IgnoreMatcher? ignoreMatcher = null)
    {
        _engine = engine;
        _ignoreMatcher = ignoreMatcher ?? IgnoreMatcher.Default;
    }

    public void SetPaths(IEnumerable<PathConfigEntry> paths)
    {
        lock (_lock)
        {
            _networkPaths.Clear();
            foreach (var p in paths.Where(p => p.Type.Equals("network", StringComparison.OrdinalIgnoreCase) && p.Enabled))
            {
                _networkPaths.Add(p);
                if (!_statuses.ContainsKey(p.Path))
                {
                    _statuses[p.Path] = new NetworkShareStatus(p.Path);
                }
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
                LastError = s.LastError
            }).ToArray();
        }
    }

    public void Start()
    {
        if (_pollLoopTask != null) return;

        _cts = new CancellationTokenSource();
        _pollLoopTask = Task.Run(() => RunPollLoopAsync(_cts.Token));
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
        NetworkShareStatus status;
        lock (_lock)
        {
            if (!_statuses.TryGetValue(entry.Path, out status!))
            {
                status = new NetworkShareStatus(entry.Path);
                _statuses[entry.Path] = status;
            }
        }

        // Check if poll interval is due
        int intervalSec = entry.PollIntervalSeconds > 0 ? entry.PollIntervalSeconds : 60;
        if (status.State == NetworkShareState.Idle &&
            (DateTimeOffset.UtcNow - status.LastSuccessfulPoll).TotalSeconds < intervalSec)
        {
            return;
        }

        int timeoutSec = entry.TimeoutSeconds > 0 ? entry.TimeoutSeconds : 4;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

        try
        {
            UpdateStatus(status, NetworkShareState.Scanning, null);

            // Execute reachability check and crawl in a thread with timeout
            await Task.Run(async () =>
            {
                if (!Directory.Exists(entry.Path))
                {
                    throw new DirectoryNotFoundException($"Network path inaccessible: {entry.Path}");
                }

                var crawler = new FastFileSystemCrawler(_ignoreMatcher, maxDegreeOfParallelism: 2);
                await crawler.CrawlAsync(entry.Path, batch =>
                {
                    _engine.BulkAdd(batch);
                    return Task.CompletedTask;
                }, cancellationToken: timeoutCts.Token).ConfigureAwait(false);
            }, timeoutCts.Token).ConfigureAwait(false);

            status.LastSuccessfulPoll = DateTimeOffset.UtcNow;
            UpdateStatus(status, NetworkShareState.Idle, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            UpdateStatus(status, NetworkShareState.Offline, $"Timeout after {timeoutSec}s. Share is unreachable or slow.");
        }
        catch (Exception ex)
        {
            UpdateStatus(status, NetworkShareState.Offline, ex.Message);
        }
    }

    private void UpdateStatus(NetworkShareStatus status, NetworkShareState newState, string? error)
    {
        status.State = newState;
        status.LastError = error;
        OnStatusChanged?.Invoke(status.Path, newState, error);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _cts?.Cancel();
        _cts?.Dispose();
    }
}
