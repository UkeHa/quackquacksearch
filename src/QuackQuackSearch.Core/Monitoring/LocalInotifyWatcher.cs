using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using QuackQuackSearch.Core.Crawler;
using QuackQuackSearch.Core.Index;

namespace QuackQuackSearch.Core.Monitoring;

public sealed class LocalInotifyWatcher : IDisposable
{
    private readonly SearchIndexEngine _engine;
    private volatile IgnoreMatcher _ignoreMatcher;
    private readonly ConcurrentDictionary<int, string> _wdToPath = new();
    private readonly ConcurrentDictionary<string, int> _pathToWd = new(StringComparer.Ordinal);

    private int _inotifyFd = -1;
    private Thread? _listenThread;
    private volatile bool _isDisposed;
    private readonly CancellationTokenSource _cts = new();

    private const uint WatchMask =
        (uint)(InotifyNative.Mask.IN_CREATE |
               InotifyNative.Mask.IN_DELETE |
               InotifyNative.Mask.IN_DELETE_SELF |
               InotifyNative.Mask.IN_MOVED_FROM |
               InotifyNative.Mask.IN_MOVED_TO |
               InotifyNative.Mask.IN_CLOSE_WRITE |
               InotifyNative.Mask.IN_Q_OVERFLOW);

    public int TotalActiveWatches => _wdToPath.Count;

    public event Action<string, string>? OnError;
    public event Action? OnQueueOverflow;

    public LocalInotifyWatcher(SearchIndexEngine engine, IgnoreMatcher? ignoreMatcher = null)
    {
        _engine = engine;
        _ignoreMatcher = ignoreMatcher ?? IgnoreMatcher.Default;

        InitInotify();
    }

    public void UpdateIgnoreMatcher(IgnoreMatcher ignoreMatcher)
    {
        ArgumentNullException.ThrowIfNull(ignoreMatcher);
        _ignoreMatcher = ignoreMatcher;
    }

    private void InitInotify()
    {
        _inotifyFd = InotifyNative.inotify_init1(InotifyNative.IN_CLOEXEC | InotifyNative.IN_NONBLOCK);
        if (_inotifyFd < 0)
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Failed to initialize inotify (errno: {err})");
        }

        _listenThread = new Thread(ListenLoop)
        {
            IsBackground = true,
            Name = "QuackInotifyListener"
        };
        _listenThread.Start();
    }

    public void AddWatchTree(string rootPath, bool indexDiscoveredFiles = false)
    {
        if (_isDisposed || !Directory.Exists(rootPath)) return;

        var matcher = _ignoreMatcher;
        string normalizedRoot = Path.GetFullPath(rootPath);
        var dirQueue = new Queue<string>();
        dirQueue.Enqueue(normalizedRoot);

        int maxWatches = InotifyNative.GetMaxUserWatches();

        while (dirQueue.Count > 0 && !_isDisposed)
        {
            string currentDir = dirQueue.Dequeue();
            string dirName = Path.GetFileName(currentDir);

            if (!string.IsNullOrEmpty(dirName) && matcher.ShouldIgnorePath(currentDir, dirName, isDirectory: true))
            {
                continue;
            }

            if (_wdToPath.Count >= maxWatches)
            {
                OnError?.Invoke(currentDir, $"Inotify watch limit reached ({maxWatches}). Cannot monitor deeper directories.");
                break;
            }

            AddSingleWatch(currentDir);

            var enumOptions = new EnumerationOptions
            {
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = true,
                RecurseSubdirectories = false
            };

            try
            {
                if (indexDiscoveredFiles)
                {
                    foreach (string file in Directory.GetFiles(currentDir, "*", enumOptions))
                    {
                        string fileName = Path.GetFileName(file);
                        if (!matcher.ShouldIgnorePath(file, fileName, isDirectory: false))
                        {
                            UpdateFileInIndex(file, fileName);
                        }
                    }
                }

                foreach (string subDir in Directory.GetDirectories(currentDir, "*", enumOptions))
                {
                    string subName = Path.GetFileName(subDir);
                    if (!matcher.ShouldIgnoreDirectoryName(subName) &&
                        !matcher.ShouldIgnorePath(subDir, subName, isDirectory: true))
                    {
                        if (indexDiscoveredFiles)
                        {
                            UpdateDirectoryInIndex(subDir, subName);
                        }
                        dirQueue.Enqueue(subDir);
                    }
                }
            }
            catch (Exception)
            {
                // Inaccessible directories skipped
            }
        }
    }

    private int AddSingleWatch(string path)
    {
        if (_isDisposed || _inotifyFd < 0) return -1;

        if (_pathToWd.TryGetValue(path, out int existingWd))
        {
            return existingWd;
        }

        int wd = InotifyNative.inotify_add_watch(_inotifyFd, path, WatchMask);
        if (wd >= 0)
        {
            _wdToPath[wd] = path;
            _pathToWd[path] = wd;
            return wd;
        }
        return -1;
    }

    private void RemoveSingleWatch(int wd)
    {
        if (_wdToPath.TryRemove(wd, out string? path))
        {
            _pathToWd.TryRemove(path, out _);
            if (!_isDisposed && _inotifyFd >= 0)
            {
                InotifyNative.inotify_rm_watch(_inotifyFd, wd);
            }
        }
    }

    /// <summary>
    /// Removes all active inotify watches under the specified path.
    /// </summary>
    public void RemoveWatchTree(string rootPath)
    {
        string normalized = Path.GetFullPath(rootPath).TrimEnd('/');
        var wdsToRemove = new List<int>();

        foreach (var (wd, path) in _wdToPath)
        {
            string p = path.TrimEnd('/');
            if (p.Equals(normalized, StringComparison.Ordinal) ||
                p.StartsWith(normalized + "/", StringComparison.Ordinal))
            {
                wdsToRemove.Add(wd);
            }
        }

        foreach (int wd in wdsToRemove)
        {
            RemoveSingleWatch(wd);
        }
    }

    private void ListenLoop()
    {
        byte[] buffer = new byte[65536];

        while (!_isDisposed && !_cts.IsCancellationRequested)
        {
            try
            {
                int fd = _inotifyFd;
                if (fd < 0) break;

                var pfd = new InotifyNative.PollFd
                {
                    fd = fd,
                    events = InotifyNative.POLLIN,
                    revents = 0
                };

                int pollRes = InotifyNative.poll(ref pfd, (UIntPtr)1, 50);
                if (pollRes <= 0 || (pfd.revents & InotifyNative.POLLIN) == 0)
                {
                    continue;
                }

                IntPtr bytesRead = InotifyNative.read(fd, buffer, (UIntPtr)buffer.Length);
                long readLen = bytesRead.ToInt64();
                if (readLen <= 0)
                {
                    continue;
                }

                int offset = 0;
                int totalBytes = (int)readLen;

                while (offset + 16 <= totalBytes)
                {
                    int wd = BitConverter.ToInt32(buffer, offset);
                    uint mask = BitConverter.ToUInt32(buffer, offset + 4);
                    uint cookie = BitConverter.ToUInt32(buffer, offset + 8);
                    uint len = BitConverter.ToUInt32(buffer, offset + 12);

                    if (len > (uint)(totalBytes - offset - 16))
                    {
                        break;
                    }

                    string name = string.Empty;
                    if (len > 0)
                    {
                        int nameOffset = offset + 16;
                        int nameLength = 0;
                        while (nameLength < len && buffer[nameOffset + nameLength] != 0)
                        {
                            nameLength++;
                        }
                        if (nameLength > 0)
                        {
                            name = Encoding.UTF8.GetString(buffer, nameOffset, nameLength);
                        }
                    }

                    ProcessInotifyEvent(wd, mask, cookie, name);
                    offset += 16 + (int)len;
                }
            }
            catch (Exception ex)
            {
                if (_isDisposed) break;
                OnError?.Invoke("inotify", $"Listener error: {ex.Message}");
            }
        }
    }

    private void ProcessInotifyEvent(int wd, uint mask, uint cookie, string name)
    {
        if ((mask & (uint)InotifyNative.Mask.IN_Q_OVERFLOW) != 0)
        {
            OnError?.Invoke("inotify", "Event queue overflowed (IN_Q_OVERFLOW). Resync advised.");
            OnQueueOverflow?.Invoke();
            return;
        }

        if ((mask & (uint)InotifyNative.Mask.IN_IGNORED) != 0)
        {
            if (_wdToPath.TryRemove(wd, out string? removedPath))
            {
                _pathToWd.TryRemove(removedPath, out _);
            }
            return;
        }

        if (!_wdToPath.TryGetValue(wd, out string? parentPath))
        {
            return;
        }

        bool isDir = (mask & (uint)InotifyNative.Mask.IN_ISDIR) != 0;
        string fullPath = string.IsNullOrEmpty(name) ? parentPath : Path.Combine(parentPath, name);

        var matcher = _ignoreMatcher;
        if (!string.IsNullOrEmpty(name) && matcher.ShouldIgnorePath(fullPath, name, isDir))
        {
            return;
        }

        if ((mask & (uint)InotifyNative.Mask.IN_CREATE) != 0 || (mask & (uint)InotifyNative.Mask.IN_MOVED_TO) != 0)
        {
            if (isDir)
            {
                UpdateDirectoryInIndex(fullPath, name);
                AddWatchTree(fullPath, indexDiscoveredFiles: true);
            }
            else
            {
                UpdateFileInIndex(fullPath, name);
            }
        }
        else if ((mask & (uint)InotifyNative.Mask.IN_CLOSE_WRITE) != 0)
        {
            if (!isDir)
            {
                UpdateFileInIndex(fullPath, name);
            }
        }
        else if ((mask & (uint)InotifyNative.Mask.IN_DELETE) != 0 ||
                 (mask & (uint)InotifyNative.Mask.IN_DELETE_SELF) != 0 ||
                 (mask & (uint)InotifyNative.Mask.IN_MOVED_FROM) != 0)
        {
            bool isSelfDelete = (mask & (uint)InotifyNative.Mask.IN_DELETE_SELF) != 0;
            if (isDir || isSelfDelete)
            {
                RemoveWatchTree(fullPath);
                _engine.RemoveSubtree(fullPath);
            }
            _engine.Remove(fullPath);
        }
    }

    private void UpdateDirectoryInIndex(string fullPath, string name)
    {
        try
        {
            long rawUnixSec = Directory.Exists(fullPath)
                ? new DateTimeOffset(Directory.GetLastWriteTimeUtc(fullPath)).ToUnixTimeSeconds()
                : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            uint unixSec = (uint)Math.Clamp(rawUnixSec, 0L, uint.MaxValue);
            _engine.AddOrUpdate(fullPath, isDirectory: true, size: 0, unixSec, name.StartsWith('.'));
        }
        catch (Exception)
        {
            // Transient directory operations ignored
        }
    }

    private void UpdateFileInIndex(string fullPath, string name)
    {
        try
        {
            if (File.Exists(fullPath))
            {
                var fi = new FileInfo(fullPath);
                long rawUnixSec = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeSeconds();
                uint unixSec = (uint)Math.Clamp(rawUnixSec, 0L, uint.MaxValue);
                bool isSymlink = (fi.Attributes & FileAttributes.ReparsePoint) != 0;
                _engine.AddOrUpdate(
                    fullPath,
                    isDirectory: false,
                    size: fi.Length,
                    modifiedUnixSec: unixSec,
                    isHidden: name.StartsWith('.') || (fi.Attributes & FileAttributes.Hidden) != 0,
                    isSymlink: isSymlink
                );
            }
        }
        catch (Exception)
        {
            // Transient file operations ignored
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _cts.Cancel();

        try
        {
            if (_listenThread != null && _listenThread.IsAlive && Thread.CurrentThread != _listenThread)
            {
                _listenThread.Join(500);
            }
        }
        catch
        {
            // Ignore thread join exceptions on shutdown
        }

        _cts.Dispose();

        int fd = Interlocked.Exchange(ref _inotifyFd, -1);
        if (fd >= 0)
        {
            InotifyNative.close(fd);
        }

        _wdToPath.Clear();
        _pathToWd.Clear();
    }
}
