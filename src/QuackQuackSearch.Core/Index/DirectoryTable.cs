using System.Collections.Concurrent;

namespace QuackQuackSearch.Core.Index;

/// <summary>
/// Efficient hierarchical directory table.
/// Replaces redundant path prefix strings with parent-child ID pointers.
/// </summary>
public sealed class DirectoryTable : IDisposable
{
    private readonly List<DirectoryNode> _nodes = [new DirectoryNode(0, 0, string.Empty)]; // ID 0 is null/root sentinel
    private readonly Dictionary<(uint ParentId, string Name), uint> _lookup = new();
    private readonly ConcurrentDictionary<uint, string> _resolvedPathCache = new();
    private readonly ConcurrentDictionary<string, uint> _pathToIdCache = new(StringComparer.Ordinal);
    private readonly ReaderWriterLockSlim _lock = new();
    private bool _isDisposed;

    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try { return _nodes.Count - 1; }
            finally { _lock.ExitReadLock(); }
        }
    }

    private static string NormalizePath(string fullPath)
    {
        return fullPath.Length > 1 && fullPath.EndsWith('/')
            ? fullPath.TrimEnd('/')
            : fullPath;
    }

    /// <summary>
    /// Attempts to look up an existing directory ID without mutating the table.
    /// </summary>
    public bool TryGetDirectoryId(string fullPath, out uint directoryId)
    {
        directoryId = 0;
        if (string.IsNullOrWhiteSpace(fullPath)) return false;

        string normalized = NormalizePath(fullPath);
        if (_pathToIdCache.TryGetValue(normalized, out directoryId))
        {
            return true;
        }

        _lock.EnterReadLock();
        try
        {
            if (normalized == "/")
            {
                if (_lookup.TryGetValue((0, "/"), out directoryId))
                {
                    _pathToIdCache[normalized] = directoryId;
                    return true;
                }
                return false;
            }

            if (!_lookup.TryGetValue((0, "/"), out uint currentParentId))
            {
                return false;
            }

            ReadOnlySpan<char> span = normalized.AsSpan();
            int i = 0;
            while (i < span.Length)
            {
                while (i < span.Length && span[i] == '/') i++;
                if (i >= span.Length) break;

                int start = i;
                while (i < span.Length && span[i] != '/') i++;
                string segment = span[start..i].ToString();

                if (!_lookup.TryGetValue((currentParentId, segment), out uint nextId))
                {
                    directoryId = 0;
                    return false;
                }
                currentParentId = nextId;
            }

            directoryId = currentParentId;
            _pathToIdCache[normalized] = directoryId;
            return true;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets or adds a directory path, decomposing it into hierarchical parent-child nodes.
    /// Handles both root "/" and nested paths "/home/user/folder".
    /// </summary>
    public uint GetOrAddDirectory(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        string normalized = NormalizePath(fullPath);
        if (_pathToIdCache.TryGetValue(normalized, out uint cachedId))
        {
            return cachedId;
        }

        _lock.EnterWriteLock();
        try
        {
            if (_pathToIdCache.TryGetValue(normalized, out cachedId))
            {
                return cachedId;
            }

            if (normalized == "/")
            {
                uint rootId = GetOrAddRootUnsafe();
                _pathToIdCache["/"] = rootId;
                return rootId;
            }

            string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            uint currentParentId = GetOrAddRootUnsafe();

            foreach (string segment in segments)
            {
                var key = (currentParentId, segment);
                if (!_lookup.TryGetValue(key, out uint dirId))
                {
                    dirId = (uint)_nodes.Count;
                    _nodes.Add(new DirectoryNode(dirId, currentParentId, segment));
                    _lookup[key] = dirId;
                }
                currentParentId = dirId;
            }

            _pathToIdCache[normalized] = currentParentId;
            _resolvedPathCache[currentParentId] = normalized.StartsWith('/') ? normalized : "/" + normalized;
            return currentParentId;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private uint GetOrAddRootUnsafe()
    {
        if (!_lookup.TryGetValue((0, "/"), out uint rootId))
        {
            rootId = (uint)_nodes.Count;
            _nodes.Add(new DirectoryNode(rootId, 0, "/"));
            _lookup[(0, "/")] = rootId;
            _resolvedPathCache[rootId] = "/";
            _pathToIdCache["/"] = rootId;
        }
        return rootId;
    }

    /// <summary>
    /// Resolves the absolute path for a given Directory ID.
    /// </summary>
    public string ResolveFullPath(uint directoryId)
    {
        if (directoryId == 0) return string.Empty;
        if (_resolvedPathCache.TryGetValue(directoryId, out string? cached))
        {
            return cached;
        }

        _lock.EnterReadLock();
        try
        {
            return ResolveFullPathUnsafe(directoryId);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    private string ResolveFullPathUnsafe(uint directoryId)
    {
        if (directoryId == 0) return string.Empty;
        if (_resolvedPathCache.TryGetValue(directoryId, out string? cached))
        {
            return cached;
        }

        if (directoryId >= _nodes.Count)
            throw new ArgumentOutOfRangeException(nameof(directoryId), $"Directory ID {directoryId} out of range.");

        var segments = new List<string>(8);
        uint current = directoryId;

        while (current != 0 && current < _nodes.Count)
        {
            DirectoryNode node = _nodes[(int)current];
            if (node.Name == "/")
            {
                break;
            }
            segments.Add(node.Name);
            current = node.ParentId;
        }

        segments.Reverse();
        string resolved = "/" + string.Join('/', segments);
        _resolvedPathCache[directoryId] = resolved;
        _pathToIdCache[resolved] = directoryId;
        return resolved;
    }

    /// <summary>
    /// Export snapshot of nodes for serialization.
    /// </summary>
    public IReadOnlyList<DirectoryNode> GetAllNodes()
    {
        _lock.EnterReadLock();
        try
        {
            return _nodes.ToArray();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Fast bulk load from snapshot.
    /// </summary>
    public void LoadSnapshot(IEnumerable<DirectoryNode> nodes)
    {
        _lock.EnterWriteLock();
        try
        {
            _nodes.Clear();
            _lookup.Clear();
            _resolvedPathCache.Clear();
            _pathToIdCache.Clear();

            foreach (var node in nodes)
            {
                _nodes.Add(node);
                if (node.Id != 0)
                {
                    _lookup[(node.ParentId, node.Name)] = node.Id;
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
            _nodes.Clear();
            _nodes.Add(new DirectoryNode(0, 0, string.Empty));
            _lookup.Clear();
            _resolvedPathCache.Clear();
            _pathToIdCache.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Finds all directory IDs that belong to the specified root path or any of its subdirectories.
    /// </summary>
    public HashSet<uint> GetDirectoryIdsInSubtree(string rootPath)
    {
        string normalized = Path.GetFullPath(rootPath).TrimEnd('/');
        var result = new HashSet<uint>();

        _lock.EnterReadLock();
        try
        {
            for (int i = 1; i < _nodes.Count; i++)
            {
                uint id = (uint)i;
                string path = ResolveFullPathUnsafe(id).TrimEnd('/');
                if (path.Equals(normalized, StringComparison.Ordinal) ||
                    path.StartsWith(normalized + "/", StringComparison.Ordinal))
                {
                    result.Add(id);
                }
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }

        return result;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _lock.Dispose();
    }
}
