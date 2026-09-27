using System.Collections.Concurrent;
using System.IO.Enumeration;

namespace QuackQuackSearch.Core.Crawler;

public readonly record struct DiscoveredItem(
    string DirectoryPath,
    string Name,
    long Size,
    uint ModifiedUnixSeconds,
    bool IsDirectory,
    bool IsHidden
);

public sealed record CrawlProgress(
    long FilesFound,
    long DirectoriesFound,
    string CurrentDirectory
);

public sealed class FastFileSystemCrawler
{
    private readonly IgnoreMatcher _ignoreMatcher;
    private readonly int _maxDegreeOfParallelism;

    public FastFileSystemCrawler(IgnoreMatcher? ignoreMatcher = null, int maxDegreeOfParallelism = 0)
    {
        _ignoreMatcher = ignoreMatcher ?? IgnoreMatcher.Default;
        _maxDegreeOfParallelism = maxDegreeOfParallelism > 0 
            ? maxDegreeOfParallelism 
            : Math.Clamp(Environment.ProcessorCount, 2, 8);
    }

    /// <summary>
    /// Crawls a root directory in parallel, yielding items as they are discovered.
    /// Uses zero-allocation FileSystemEntry spans to prevent GC thrashing.
    /// </summary>
    public async Task CrawlAsync(
        string rootPath,
        Func<IReadOnlyList<DiscoveredItem>, Task> onBatchDiscovered,
        IProgress<CrawlProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException($"Directory not found: {rootPath}");

        var dirQueue = new ConcurrentQueue<string>();
        dirQueue.Enqueue(Path.GetFullPath(rootPath));

        long filesCount = 0;
        long dirsCount = 0;
        int activeWorkers = 0;

        var batchQueue = new ConcurrentQueue<DiscoveredItem>();
        const int batchSize = 1000;

        async Task FlushBatchAsync(bool force)
        {
            if (batchQueue.IsEmpty) return;
            if (force || batchQueue.Count >= batchSize)
            {
                var batch = new List<DiscoveredItem>(batchSize);
                while (batch.Count < batchSize && batchQueue.TryDequeue(out var item))
                {
                    batch.Add(item);
                }
                if (batch.Count > 0)
                {
                    await onBatchDiscovered(batch).ConfigureAwait(false);
                }
            }
        }

        var tasks = Enumerable.Range(0, _maxDegreeOfParallelism).Select(workerId => Task.Run(async () =>
        {
            var options = new EnumerationOptions
            {
                AttributesToSkip = FileAttributes.ReparsePoint, // Avoid symlink loops
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                ReturnSpecialDirectories = false
            };

            int idleRetries = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                if (!dirQueue.TryDequeue(out string? currentDir))
                {
                    if (Volatile.Read(ref activeWorkers) == 0 && dirQueue.IsEmpty)
                    {
                        idleRetries++;
                        if (idleRetries > 3)
                        {
                            // Truly done
                            break;
                        }
                    }
                    else
                    {
                        idleRetries = 0;
                    }

                    await Task.Delay(2, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                idleRetries = 0;
                Interlocked.Increment(ref activeWorkers);

                try
                {
                    Interlocked.Increment(ref dirsCount);
                    if (dirsCount % 200 == 0 && progress != null)
                    {
                        progress.Report(new CrawlProgress(
                            Interlocked.Read(ref filesCount),
                            dirsCount,
                            currentDir
                        ));
                    }

                    // Enumerate current directory items
                    var enumerable = new FileSystemEnumerable<DiscoveredItem>(
                        currentDir,
                        transform: (ref FileSystemEntry entry) =>
                        {
                            bool isDir = entry.IsDirectory;
                            string name = entry.FileName.ToString();
                            bool isHidden = (entry.Attributes & FileAttributes.Hidden) != 0 || name.StartsWith('.');
                            uint unixSec = (uint)entry.LastWriteTimeUtc.ToUnixTimeSeconds();
                            long size = isDir ? 0 : entry.Length;

                            return new DiscoveredItem(
                                currentDir,
                                name,
                                size,
                                unixSec,
                                isDir,
                                isHidden
                            );
                        },
                        options
                    );

                    foreach (var item in enumerable)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        if (item.IsDirectory)
                        {
                            if (!_ignoreMatcher.ShouldIgnoreDirectoryName(item.Name))
                            {
                                string subDir = Path.Combine(currentDir, item.Name);
                                dirQueue.Enqueue(subDir);
                            }
                        }
                        else
                        {
                            Interlocked.Increment(ref filesCount);
                        }

                        batchQueue.Enqueue(item);
                        if (batchQueue.Count >= batchSize)
                        {
                            await FlushBatchAsync(false).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception)
                {
                    // Inaccessible directories are ignored
                }
                finally
                {
                    Interlocked.Decrement(ref activeWorkers);
                }
            }

            await FlushBatchAsync(true).ConfigureAwait(false);
        }, cancellationToken)).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);

        // Final flush
        while (!batchQueue.IsEmpty)
        {
            await FlushBatchAsync(true).ConfigureAwait(false);
        }

        progress?.Report(new CrawlProgress(filesCount, dirsCount, "Done"));
    }
}
