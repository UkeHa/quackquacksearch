using System.Collections.Concurrent;
using System.IO.Enumeration;

namespace QuackQuackSearch.Core.Crawler;

public readonly record struct DiscoveredItem(
    string DirectoryPath,
    string Name,
    long Size,
    uint ModifiedUnixSeconds,
    bool IsDirectory,
    bool IsHidden,
    bool IsSymlink = false
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
        long pendingDirs = 1;
        const int batchSize = 1000;

        var tasks = Enumerable.Range(0, _maxDegreeOfParallelism).Select(_ => Task.Run(async () =>
        {
            var options = new EnumerationOptions
            {
                AttributesToSkip = FileAttributes.ReparsePoint, // Avoid symlink loops
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                ReturnSpecialDirectories = false
            };

            var localBatch = new List<DiscoveredItem>(batchSize);

            while (!cancellationToken.IsCancellationRequested)
            {
                if (!dirQueue.TryDequeue(out string? currentDir))
                {
                    if (Volatile.Read(ref pendingDirs) <= 0 && dirQueue.IsEmpty)
                    {
                        break;
                    }

                    await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    long currentDirsCount = Interlocked.Increment(ref dirsCount);
                    if (currentDirsCount % 200 == 0 && progress != null)
                    {
                        progress.Report(new CrawlProgress(
                            Interlocked.Read(ref filesCount),
                            currentDirsCount,
                            currentDir
                        ));
                    }

                    var enumerable = new FileSystemEnumerable<DiscoveredItem>(
                        currentDir,
                        transform: (ref FileSystemEntry entry) =>
                        {
                            bool isDir = entry.IsDirectory;
                            string name = entry.FileName.ToString();
                            bool isHidden = (entry.Attributes & FileAttributes.Hidden) != 0 || name.StartsWith('.');
                            bool isSymlink = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
                            long rawUnixSec = entry.LastWriteTimeUtc.ToUnixTimeSeconds();
                            uint unixSec = (uint)Math.Clamp(rawUnixSec, 0L, uint.MaxValue);
                            long size = isDir ? 0 : entry.Length;

                            return new DiscoveredItem(
                                currentDir,
                                name,
                                size,
                                unixSec,
                                isDir,
                                isHidden,
                                isSymlink
                            );
                        },
                        options
                    );

                    foreach (var item in enumerable)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        if (item.IsDirectory && _ignoreMatcher.ShouldIgnoreDirectoryName(item.Name))
                        {
                            continue;
                        }

                        string itemFullPath = Path.Combine(currentDir, item.Name);
                        if (_ignoreMatcher.ShouldIgnorePath(itemFullPath, item.Name, item.IsDirectory))
                        {
                            continue;
                        }

                        if (item.IsDirectory)
                        {
                            Interlocked.Increment(ref pendingDirs);
                            dirQueue.Enqueue(itemFullPath);
                        }
                        else
                        {
                            Interlocked.Increment(ref filesCount);
                        }

                        localBatch.Add(item);
                        if (localBatch.Count >= batchSize)
                        {
                            var batchToFlush = localBatch;
                            localBatch = new List<DiscoveredItem>(batchSize);
                            await onBatchDiscovered(batchToFlush).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception)
                {
                    // Inaccessible directories are ignored
                }
                finally
                {
                    Interlocked.Decrement(ref pendingDirs);
                }
            }

            if (localBatch.Count > 0)
            {
                await onBatchDiscovered(localBatch).ConfigureAwait(false);
            }
        }, cancellationToken)).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);

        progress?.Report(new CrawlProgress(Interlocked.Read(ref filesCount), Interlocked.Read(ref dirsCount), "Done"));
    }
}
