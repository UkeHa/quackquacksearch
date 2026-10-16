using QuackQuackSearch.Core.Crawler;
using QuackQuackSearch.Core.Search;

namespace QuackQuackSearch.Core.Index;

public sealed class SearchIndexEngine : IDisposable
{
    private readonly DirectoryTable _directoryTable = new();
    private readonly List<CompactFileEntry> _entries = new(100_000);
    private readonly Dictionary<(uint DirectoryId, string Name), int> _entryIndex = new(100_000);
    private readonly ReaderWriterLockSlim _lock = new();
    private bool _isDisposed;

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
    /// Bulk adds or updates discovered items from crawler without creating duplicates.
    /// </summary>
    public void BulkAdd(IReadOnlyList<DiscoveredItem> items)
    {
        if (items.Count == 0) return;

        var entriesToAdd = PrepareEntries(items);

        _lock.EnterWriteLock();
        try
        {
            UpsertEntriesUnsafe(entriesToAdd);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Atomically replaces all entries under rootPath with the newly discovered items.
    /// </summary>
    public void ReplaceSubtree(string rootPath, IReadOnlyList<DiscoveredItem> items)
    {
        var entriesToAdd = PrepareEntries(items);
        var dirIds = _directoryTable.GetDirectoryIdsInSubtree(rootPath);

        _lock.EnterWriteLock();
        try
        {
            if (dirIds.Count > 0)
            {
                _entries.RemoveAll(e => dirIds.Contains(e.DirectoryId));
                RebuildEntryIndexUnsafe();
            }

            UpsertEntriesUnsafe(entriesToAdd);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private List<CompactFileEntry> PrepareEntries(IReadOnlyList<DiscoveredItem> items)
    {
        var entriesToAdd = new List<CompactFileEntry>(items.Count);
        string? lastDirPath = null;
        uint lastDirId = 0;

        foreach (var item in items)
        {
            uint dirId;
            if (lastDirPath != null && string.Equals(lastDirPath, item.DirectoryPath, StringComparison.Ordinal))
            {
                dirId = lastDirId;
            }
            else
            {
                dirId = _directoryTable.GetOrAddDirectory(item.DirectoryPath);
                lastDirPath = item.DirectoryPath;
                lastDirId = dirId;
            }

            FileEntryFlags flags = FileEntryFlags.None;
            if (item.IsDirectory) flags |= FileEntryFlags.IsDirectory;
            if (item.IsHidden) flags |= FileEntryFlags.IsHidden;
            if (item.IsSymlink) flags |= FileEntryFlags.IsSymlink;

            entriesToAdd.Add(new CompactFileEntry(
                dirId,
                item.Name,
                item.Size,
                item.ModifiedUnixSeconds,
                flags
            ));
        }

        return entriesToAdd;
    }

    private void UpsertEntriesUnsafe(List<CompactFileEntry> entriesToAdd)
    {
        foreach (var entry in entriesToAdd)
        {
            var key = (entry.DirectoryId, entry.Name);
            if (_entryIndex.TryGetValue(key, out int existingIdx))
            {
                _entries[existingIdx] = entry;
            }
            else
            {
                _entryIndex[key] = _entries.Count;
                _entries.Add(entry);
            }
        }
    }

    private void RebuildEntryIndexUnsafe()
    {
        _entryIndex.Clear();
        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            _entryIndex[(e.DirectoryId, e.Name)] = i;
        }
    }

    /// <summary>
    /// Adds or updates a single entry in O(1) time.
    /// </summary>
    public void AddOrUpdate(string fullPath, bool isDirectory, long size, uint modifiedUnixSec, bool isHidden, bool isSymlink = false)
    {
        string? dir = Path.GetDirectoryName(fullPath);
        string name = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return;

        uint dirId = _directoryTable.GetOrAddDirectory(dir);

        FileEntryFlags flags = FileEntryFlags.None;
        if (isDirectory) flags |= FileEntryFlags.IsDirectory;
        if (isHidden) flags |= FileEntryFlags.IsHidden;
        if (isSymlink) flags |= FileEntryFlags.IsSymlink;

        var newEntry = new CompactFileEntry(dirId, name, size, modifiedUnixSec, flags);
        var key = (dirId, name);

        _lock.EnterWriteLock();
        try
        {
            if (_entryIndex.TryGetValue(key, out int index))
            {
                _entries[index] = newEntry;
            }
            else
            {
                _entryIndex[key] = _entries.Count;
                _entries.Add(newEntry);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Removes a file or directory by full path in O(1) time.
    /// </summary>
    public bool Remove(string fullPath)
    {
        string? dir = Path.GetDirectoryName(fullPath);
        string name = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return false;

        if (!_directoryTable.TryGetDirectoryId(dir, out uint dirId))
        {
            return false;
        }

        var key = (dirId, name);

        _lock.EnterWriteLock();
        try
        {
            if (_entryIndex.Remove(key, out int index))
            {
                int lastIndex = _entries.Count - 1;
                if (index < lastIndex)
                {
                    var moved = _entries[lastIndex];
                    _entries[index] = moved;
                    _entryIndex[(moved.DirectoryId, moved.Name)] = index;
                }
                _entries.RemoveAt(lastIndex);
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
            if (removedCount > 0)
            {
                RebuildEntryIndexUnsafe();
            }
            return removedCount;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Removes any entries from the index that match the specified ignore matcher.
    /// </summary>
    public int PurgeIgnored(IgnoreMatcher ignoreMatcher)
    {
        _lock.EnterWriteLock();
        try
        {
            int removed = _entries.RemoveAll(e =>
            {
                string dir = _directoryTable.ResolveFullPath(e.DirectoryId);
                string fullPath = dir == "/" ? $"/{e.Name}" : $"{dir}/{e.Name}";
                return ignoreMatcher.ShouldIgnorePath(fullPath, e.Name, e.IsDirectory);
            });
            if (removed > 0)
            {
                RebuildEntryIndexUnsafe();
            }
            return removed;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Returns the most recently modified entries without requiring a search query.
    /// </summary>
    public IReadOnlyList<SearchResult> GetRecentEntries(int maxResults = 100, bool includeHidden = false, bool includeDirectories = true)
    {
        if (maxResults <= 0) return Array.Empty<SearchResult>();

        _lock.EnterReadLock();
        try
        {
            int count = _entries.Count;
            if (count == 0) return Array.Empty<SearchResult>();

            var topHeap = new PriorityQueue<CompactFileEntry, uint>(Math.Min(count, maxResults + 1));

            for (int i = 0; i < count; i++)
            {
                var entry = _entries[i];
                if (!includeHidden && entry.IsHidden) continue;
                if (!includeDirectories && entry.IsDirectory) continue;

                if (topHeap.Count < maxResults)
                {
                    topHeap.Enqueue(entry, entry.ModifiedUnixSeconds);
                }
                else if ( topHeap.TryPeek(out _, out uint minMod) && entry.ModifiedUnixSeconds > minMod)
                {
                    topHeap.DequeueEnqueue(entry, entry.ModifiedUnixSeconds);
                }
            }

            var matched = new List<CompactFileEntry>(topHeap.Count);
            while (topHeap.Count > 0)
            {
                matched.Add(topHeap.Dequeue());
            }
            matched.Reverse();

            var results = new List<SearchResult>(matched.Count);
            foreach (var entry in matched)
            {
                string dirPath = _directoryTable.ResolveFullPath(entry.DirectoryId);
                string fullPath = dirPath == "/" ? "/" + entry.Name : Path.Combine(dirPath, entry.Name);
                results.Add(new SearchResult(
                    fullPath,
                    entry.Name,
                    entry.Size,
                    entry.ModifiedTime,
                    entry.IsDirectory,
                    100.0
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
    /// Executes a lightning-fast parallel search over all index entries.
    /// </summary>
    public IReadOnlyList<SearchResult> Search(string query, SearchOptions? options = null)
    {
        options ??= new SearchOptions();
        query = query.Trim();
        if (string.IsNullOrEmpty(query)) return Array.Empty<SearchResult>();

        bool hasWildcards = query.Contains('*') || query.Contains('?');
        int maxResults = Math.Max(1, options.MaxResults);

        _lock.EnterReadLock();
        try
        {
            int count = _entries.Count;
            if (count == 0) return Array.Empty<SearchResult>();

            int coreCount = Math.Clamp(Environment.ProcessorCount, 1, 8);
            int chunkSize = (count + coreCount - 1) / coreCount;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var localResults = new List<(CompactFileEntry Entry, double Score)>[coreCount];

            Parallel.For(0, coreCount, i =>
            {
                int start = i * chunkSize;
                int end = Math.Min(count, start + chunkSize);
                if (start >= end) return;

                var topHeap = new PriorityQueue<(CompactFileEntry Entry, double Score), double>(Math.Min(end - start, maxResults + 1));

                void AddCandidate(CompactFileEntry candidateEntry, double candidateScore)
                {
                    if (topHeap.Count < maxResults)
                    {
                        topHeap.Enqueue((candidateEntry, candidateScore), candidateScore);
                    }
                    else if (topHeap.TryPeek(out _, out double minScore) && candidateScore > minScore)
                    {
                        topHeap.DequeueEnqueue((candidateEntry, candidateScore), candidateScore);
                    }
                }

                for (int j = start; j < end; j++)
                {
                    var entry = _entries[j];

                    if (!options.IncludeHidden && entry.IsHidden)
                        continue;

                    if (!options.IncludeDirectories && entry.IsDirectory)
                        continue;

                    bool matched = false;
                    double score = 0;

                    if (hasWildcards)
                    {
                        if (SimdMatcher.Matches(entry.Name.AsSpan(), query.AsSpan(), true))
                        {
                            score = ResultRanker.CalculateScore(entry.Name, query, entry.ModifiedUnixSeconds);
                            matched = true;
                        }
                    }
                    else if (options.Fuzzy)
                    {
                        if (FuzzyMatcher.TryMatch(entry.Name.AsSpan(), query.AsSpan(), out double fuzzyScore))
                        {
                            long age = Math.Max(0, now - entry.ModifiedUnixSeconds);
                            if (age < 86400 * 7) fuzzyScore += 20.0;
                            score = fuzzyScore;
                            matched = true;
                        }
                    }
                    else if (SimdMatcher.Matches(entry.Name.AsSpan(), query.AsSpan(), false))
                    {
                        score = ResultRanker.CalculateScore(entry.Name, query, entry.ModifiedUnixSeconds);
                        matched = true;
                    }

                    if (!matched && options.SearchInPath)
                    {
                        string dirPath = _directoryTable.ResolveFullPath(entry.DirectoryId);
                        string fullPath = dirPath == "/" ? "/" + entry.Name : $"{dirPath}/{entry.Name}";
                        if (SimdMatcher.Matches(fullPath.AsSpan(), query.AsSpan(), hasWildcards))
                        {
                            score = ResultRanker.CalculateScore(fullPath, query, entry.ModifiedUnixSeconds) * 0.8;
                            matched = true;
                        }
                    }

                    if (matched)
                    {
                        AddCandidate(entry, score);
                    }
                }

                var list = new List<(CompactFileEntry Entry, double Score)>(topHeap.Count);
                while (topHeap.Count > 0)
                {
                    list.Add(topHeap.Dequeue());
                }
                localResults[i] = list;
            });

            // Merge bounded per-core top results and sort
            var allMatched = new List<(CompactFileEntry Entry, double Score)>(coreCount * maxResults);
            for (int i = 0; i < coreCount; i++)
            {
                if (localResults[i] != null)
                {
                    allMatched.AddRange(localResults[i]);
                }
            }

            allMatched.Sort((a, b) => b.Score.CompareTo(a.Score));

            int takeCount = Math.Min(allMatched.Count, maxResults);
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
            _entryIndex.Clear();
            foreach (var entry in entries)
            {
                var key = (entry.DirectoryId, entry.Name);
                if (_entryIndex.TryGetValue(key, out int existingIdx))
                {
                    _entries[existingIdx] = entry;
                }
                else
                {
                    _entryIndex[key] = _entries.Count;
                    _entries.Add(entry);
                }
            }
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
            _entryIndex.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _lock.Dispose();
        _directoryTable.Dispose();
    }
}
