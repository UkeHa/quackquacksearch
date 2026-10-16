namespace QuackQuackSearch.Core.Index;

/// <summary>
/// Efficient hierarchical directory table.
/// Replaces redundant path prefix strings with parent-child ID pointers.
/// </summary>
public sealed class DirectoryTable : IDisposable
{
    private readonly List<DirectoryNode> _nodes = [new DirectoryNode(0, 0, string.Empty)]; // ID 0 is null/root sentinel
    private readonly Dictionary<(uint ParentId, string Name), uint> _lookup = new();
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

        _lock.EnterReadLock();
        try
        {
            return TryGetDirectoryIdUnsafe(normalized, out directoryId);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    private bool TryGetDirectoryIdUnsafe(string normalized, out uint directoryId)
    {
        directoryId = 0;
        if (normalized == "/")
        {
            return _lookup.TryGetValue((0, "/"), out directoryId);
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
        return true;
    }

    /// <summary>
    /// Gets or adds a directory path, decomposing it into hierarchical parent-child nodes.
    /// Handles both root "/" and nested paths "/home/user/folder".
    /// </summary>
    public uint GetOrAddDirectory(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        string normalized = NormalizePath(fullPath);

        _lock.EnterUpgradeableReadLock();
        try
        {
            if (TryGetDirectoryIdUnsafe(normalized, out uint existingId))
            {
                return existingId;
            }

            _lock.EnterWriteLock();
            try
            {
                if (normalized == "/")
                {
                    return GetOrAddRootUnsafe();
                }

                uint currentParentId = GetOrAddRootUnsafe();
                ReadOnlySpan<char> span = normalized.AsSpan();
                int i = 0;
                while (i < span.Length)
                {
                    while (i < span.Length && span[i] == '/') i++;
                    if (i >= span.Length) break;

                    int start = i;
                    while (i < span.Length && span[i] != '/') i++;
                    string segment = span[start..i].ToString();

                    var key = (currentParentId, segment);
                    if (!_lookup.TryGetValue(key, out uint dirId))
                    {
                        dirId = (uint)_nodes.Count;
                        _nodes.Add(new DirectoryNode(dirId, currentParentId, segment));
                        _lookup[key] = dirId;
                    }
                    currentParentId = dirId;
                }

                return currentParentId;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
        finally
        {
            _lock.ExitUpgradeableReadLock();
        }
    }

    private uint GetOrAddRootUnsafe()
    {
        if (!_lookup.TryGetValue((0, "/"), out uint rootId))
        {
            rootId = (uint)_nodes.Count;
            _nodes.Add(new DirectoryNode(rootId, 0, "/"));
            _lookup[(0, "/")] = rootId;
        }
        return rootId;
    }

    /// <summary>
    /// Resolves the absolute path for a given Directory ID.
    /// </summary>
    public string ResolveFullPath(uint directoryId)
    {
        if (directoryId == 0) return string.Empty;

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

        if (directoryId >= _nodes.Count)
            throw new ArgumentOutOfRangeException(nameof(directoryId), $"Directory ID {directoryId} out of range.");

        int totalLen = 0;
        int depth = 0;
        uint current = directoryId;

        while (current != 0 && current < _nodes.Count)
        {
            DirectoryNode node = _nodes[(int)current];
            if (node.Name == "/")
            {
                break;
            }
            totalLen += 1 + node.Name.Length;
            depth++;
            current = node.ParentId;
        }

        if (depth == 0) return "/";

        return string.Create(totalLen, (Nodes: _nodes, DirId: directoryId), static (span, state) =>
        {
            int pos = span.Length;
            uint curr = state.DirId;
            var nodes = state.Nodes;

            while (curr != 0 && curr < nodes.Count)
            {
                DirectoryNode node = nodes[(int)curr];
                if (node.Name == "/") break;

                ReadOnlySpan<char> nameSpan = node.Name.AsSpan();
                pos -= nameSpan.Length;
                nameSpan.CopyTo(span[pos..]);
                pos -= 1;
                span[pos] = '/';
                curr = node.ParentId;
            }
        });
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

            if (nodes is ICollection<DirectoryNode> col)
            {
                _nodes.EnsureCapacity(col.Count);
                _lookup.EnsureCapacity(col.Count);
            }

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
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Finds all directory IDs that belong to the specified root path or any of its subdirectories
    /// purely by traversing parent-child IDs without string allocations.
    /// </summary>
    public HashSet<uint> GetDirectoryIdsInSubtree(string rootPath)
    {
        string normalized = NormalizePath(Path.GetFullPath(rootPath));
        var result = new HashSet<uint>();

        _lock.EnterReadLock();
        try
        {
            if (!TryGetDirectoryIdUnsafe(normalized, out uint rootId) || rootId == 0)
            {
                return result;
            }

            result.Add(rootId);

            // Nodes are added in topological parent-before-child order, but to be 100% order-independent,
            // we memoize each node's membership in a stackalloc/array state buffer (0=unknown, 1=in, 2=out)
            int count = _nodes.Count;
            byte[] state = new byte[count];
            state[rootId] = 1;

            Span<uint> chain = stackalloc uint[64];

            for (int i = 1; i < count; i++)
            {
                if (state[i] != 0)
                {
                    if (state[i] == 1) result.Add((uint)i);
                    continue;
                }

                int chainLen = 0;
                uint curr = (uint)i;
                byte outcome = 2;

                while (curr != 0 && curr < count)
                {
                    byte s = state[curr];
                    if (s != 0)
                    {
                        outcome = s;
                        break;
                    }
                    if (chainLen < chain.Length)
                    {
                        chain[chainLen++] = curr;
                    }
                    curr = _nodes[(int)curr].ParentId;
                }

                for (int c = 0; c < chainLen; c++)
                {
                    uint id = chain[c];
                    state[id] = outcome;
                    if (outcome == 1)
                    {
                        result.Add(id);
                    }
                }
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }

        return result;
    }

    /// <summary>
    /// Finds all directory IDs whose path matches the query (or contains a matching ancestor).
    /// </summary>
    public Dictionary<uint, double> FindMatchingDirectoryScores(string query, bool hasWildcards)
    {
        var result = new Dictionary<uint, double>();
        ReadOnlySpan<char> querySpan = query.AsSpan();
        bool queryHasSlash = querySpan.Contains('/');

        _lock.EnterReadLock();
        try
        {
            int count = _nodes.Count;
            if (!queryHasSlash && !hasWildcards)
            {
                // Single segment substring search: if a directory node's Name matches, it and all its descendants match!
                byte[] matchedState = new byte[count]; // 0=unknown, 1=matched, 2=unmatched
                for (int i = 1; i < count; i++)
                {
                    var node = _nodes[i];
                    if (node.Name.AsSpan().Contains(querySpan, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedState[i] = 1;
                    }
                }

                Span<uint> chain = stackalloc uint[64];
                for (int i = 1; i < count; i++)
                {
                    if (matchedState[i] == 1)
                    {
                        result[(uint)i] = 450.0;
                        continue;
                    }
                    if (matchedState[i] == 2) continue;

                    int chainLen = 0;
                    uint curr = (uint)i;
                    byte outcome = 2;
                    while (curr != 0 && curr < count)
                    {
                        byte s = matchedState[curr];
                        if (s != 0)
                        {
                            outcome = s;
                            break;
                        }
                        if (chainLen < chain.Length) chain[chainLen++] = curr;
                        curr = _nodes[(int)curr].ParentId;
                    }

                    for (int c = 0; c < chainLen; c++)
                    {
                        uint id = chain[c];
                        matchedState[id] = outcome;
                        if (outcome == 1)
                        {
                            result[id] = 450.0;
                        }
                    }
                }
            }
            else
            {
                for (int i = 1; i < count; i++)
                {
                    string path = ResolveFullPathUnsafe((uint)i);
                    if (Search.SimdMatcher.Matches(path.AsSpan(), querySpan, hasWildcards))
                    {
                        result[(uint)i] = 450.0;
                    }
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
