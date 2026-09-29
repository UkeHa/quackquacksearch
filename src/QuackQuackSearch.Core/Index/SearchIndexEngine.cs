using System.Runtime.InteropServices;
using QuackQuackSearch.Core.Crawler;
using QuackQuackSearch.Core.Search;

namespace QuackQuackSearch.Core.Index;

public sealed class SearchIndexEngine
{
    private readonly DirectoryTable _directoryTable = new();
    private readonly List<CompactFileEntry> _entries = new(100_000);
    private readonly ReaderWriterLockSlim _lock = new();

    public DirectoryTable DirectoryTable => _directoryTable;
    public int TotalDirectories => _directoryTable.Count;

    public int TotalFiles
    {
        get
        {
            _lock.EnterReadLock();
            try { return _entries.Count; }
            finally { _lock.ExitReadLock(); }
        }
    }

    /// <summary>
    /// Bulk adds discovered items from crawler.
    /// </summary>
    public void BulkAdd(IReadOnlyList<DiscoveredItem> items)
    {
        if (items.Count == 0) return;

        // Group or resolve directory IDs
        var entriesToAdd = new List<CompactFileEntry>(items.Count);

        foreach (var item in items)
        {
            uint dirId = _directoryTable.GetOrAddDirectory(item.DirectoryPath);
            FileEntryFlags flags = FileEntryFlags.None;
            if (item.IsDirectory) flags |= FileEntryFlags.IsDirectory;
            if (item.IsHidden) flags |= FileEntryFlags.IsHidden;

            entriesToAdd.Add(new CompactFileEntry(
                dirId,
                item.Name,
                item.Size,
                item.ModifiedUnixSeconds,
                flags
            ));
        }

        _lock.EnterWriteLock();
        try
        {
            _entries.AddRange(entriesToAdd);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Adds or updates a single entry.
    /// </summary>
    public void AddOrUpdate(string fullPath, bool isDirectory, long size, uint modifiedUnixSec, bool isHidden)
    {
        string? dir = Path.GetDirectoryName(fullPath);
        string name = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return;

        uint dirId = _directoryTable.GetOrAddDirectory(dir);

        FileEntryFlags flags = FileEntryFlags.None;
        if (isDirectory) flags |= FileEntryFlags.IsDirectory;
        if (isHidden) flags |= FileEntryFlags.IsHidden;

        var newEntry = new CompactFileEntry(dirId, name, size, modifiedUnixSec, flags);

        _lock.EnterWriteLock();
        try
        {
            int index = _entries.FindIndex(e => e.DirectoryId == dirId && e.Name.Equals(name, StringComparison.Ordinal));
            if (index >= 0)
            {
                _entries[index] = newEntry;
            }
            else
            {
                _entries.Add(newEntry);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Removes a file or directory by full path.
    /// </summary>
    public bool Remove(string fullPath)
    {
        string? dir = Path.GetDirectoryName(fullPath);
        string name = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return false;

        uint dirId = _directoryTable.GetOrAddDirectory(dir);

        _lock.EnterWriteLock();
        try
        {
            int index = _entries.FindIndex(e => e.DirectoryId == dirId && e.Name.Equals(name, StringComparison.Ordinal));
            if (index >= 0)
            {
                _entries.RemoveAt(index);
                return true;
            }
            return false;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Removes all files and subdirectories belonging to the specified root path.
    /// </summary>
    public int RemoveSubtree(string rootPath)
    {
        var dirIds = _directoryTable.GetDirectoryIdsInSubtree(rootPath);
        if (dirIds.Count == 0) return 0;

        _lock.EnterWriteLock();
        try
        {
            int removedCount = _entries.RemoveAll(e => dirIds.Contains(e.DirectoryId));
            return removedCount;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Executes a lightning-fast parallel search over all index entries.
    /// </summary>
    public IReadOnlyList<SearchResult> Search(string query, SearchOptions? options = null)
    {
        options ??= new SearchOptions();
        query = query.Trim();
        if (string.IsNullOrEmpty(query)) return Array.Empty<SearchResult>();

        bool hasWildcards = query.Contains('*') || query.Contains('?');

        _lock.EnterReadLock();
        try
        {
            int count = _entries.Count;
            if (count == 0) return Array.Empty<SearchResult>();

            int coreCount = Math.Clamp(Environment.ProcessorCount, 1, 8);
            int chunkSize = (count + coreCount - 1) / coreCount;

            var localResults = new List<(CompactFileEntry Entry, double Score)>[coreCount];

            Parallel.For(0, coreCount, i =>
            {
                int start = i * chunkSize;
                int end = Math.Min(count, start + chunkSize);
                if (start >= end) return;

                var list = new List<(CompactFileEntry Entry, double Score)>(128);

                for (int j = start; j < end; j++)
                {
                    var entry = _entries[j];

                    if (!options.IncludeHidden && entry.IsHidden)
                        continue;

                    if (!options.IncludeDirectories && entry.IsDirectory)
                        continue;

                    if (options.Fuzzy)
                    {
                        if (FuzzyMatcher.TryMatch(entry.Name.AsSpan(), query.AsSpan(), out double fuzzyScore))
                        {
                            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            long age = Math.Max(0, now - entry.ModifiedUnixSeconds);
                            if (age < 86400 * 7) fuzzyScore += 20.0;
                            list.Add((entry, fuzzyScore));
                        }
                    }
                    else if (SimdMatcher.Matches(entry.Name.AsSpan(), query.AsSpan(), hasWildcards))
                    {
                        double score = ResultRanker.CalculateScore(entry.Name, query, entry.ModifiedUnixSeconds);
                        list.Add((entry, score));
                    }
                }

                localResults[i] = list;
            });

            // Merge and sort results
            var allMatched = new List<(CompactFileEntry Entry, double Score)>();
            for (int i = 0; i < coreCount; i++)
            {
                if (localResults[i] != null)
                {
                    allMatched.AddRange(localResults[i]);
                }
            }

            allMatched.Sort((a, b) => b.Score.CompareTo(a.Score));

            int takeCount = Math.Min(allMatched.Count, options.MaxResults);
            var results = new List<SearchResult>(takeCount);

            for (int i = 0; i < takeCount; i++)
            {
                var item = allMatched[i];
                string dirPath = _directoryTable.ResolveFullPath(item.Entry.DirectoryId);
                string fullPath = dirPath == "/" ? "/" + item.Entry.Name : Path.Combine(dirPath, item.Entry.Name);

                results.Add(new SearchResult(
                    fullPath,
                    item.Entry.Name,
                    item.Entry.Size,
                    item.Entry.ModifiedTime,
                    item.Entry.IsDirectory,
                    item.Score
                ));
            }

            return results;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets all current entries for serialization.
    /// </summary>
    public (IReadOnlyList<DirectoryNode> Nodes, IReadOnlyList<CompactFileEntry> Entries) GetSnapshot()
    {
        _lock.EnterReadLock();
        try
        {
            return (_directoryTable.GetAllNodes(), _entries.ToArray());
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Restores engine state from snapshot.
    /// </summary>
    public void RestoreSnapshot(IEnumerable<DirectoryNode> nodes, IEnumerable<CompactFileEntry> entries)
    {
        _lock.EnterWriteLock();
        try
        {
            _directoryTable.LoadSnapshot(nodes);
            _entries.Clear();
            _entries.AddRange(entries);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            _directoryTable.Clear();
            _entries.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
}
