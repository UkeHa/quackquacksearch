using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using QuackQuackSearch.Core.Crawler;
using QuackQuackSearch.Core.Index;

namespace QuackQuackSearch.Core.Monitoring;

public sealed class LocalInotifyWatcher : IDisposable
{
    private readonly SearchIndexEngine _engine;
    private readonly IgnoreMatcher _ignoreMatcher;
    private readonly ConcurrentDictionary<int, string> _wdToPath = new();
    private readonly ConcurrentDictionary<string, int> _pathToWd = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<uint, (string Path, bool IsDir)> _cookiePendingMoves = new();

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

    public LocalInotifyWatcher(SearchIndexEngine engine, IgnoreMatcher? ignoreMatcher = null)
    {
        _engine = engine;
        _ignoreMatcher = ignoreMatcher ?? IgnoreMatcher.Default;

        InitInotify();
    }

    private void InitInotify()
    {
        _inotifyFd = InotifyNative.inotify_init1(InotifyNative.IN_CLOEXEC);
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

    public void AddWatchTree(string rootPath)
    {
        if (_isDisposed || !Directory.Exists(rootPath)) return;

        string normalizedRoot = Path.GetFullPath(rootPath);
        var dirQueue = new Queue<string>();
        dirQueue.Enqueue(normalizedRoot);

        int maxWatches = InotifyNative.GetMaxUserWatches();

        while (dirQueue.Count > 0)
        {
            string currentDir = dirQueue.Dequeue();
            string dirName = Path.GetFileName(currentDir);

            if (!string.IsNullOrEmpty(dirName) && _ignoreMatcher.ShouldIgnorePath(currentDir, dirName, isDirectory: true))
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
                foreach (string subDir in Directory.GetDirectories(currentDir, "*", enumOptions))
                {
                    string subName = Path.GetFileName(subDir);
                    if (!_ignoreMatcher.ShouldIgnoreDirectoryName(subName) &&
                        !_ignoreMatcher.ShouldIgnorePath(subDir, subName, isDirectory: true))
                    {
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
            InotifyNative.inotify_rm_watch(_inotifyFd, wd);
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
            if (p.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase))
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
                IntPtr bytesRead = InotifyNative.read(_inotifyFd, buffer, (UIntPtr)buffer.Length);
                if (bytesRead.ToInt64() <= 0)
                {
                    if (_isDisposed) break;
                    Thread.Sleep(10);
                    continue;
                }

                int offset = 0;
                int totalBytes = (int)bytesRead;

                while (offset < totalBytes)
                {
                    int wd = BitConverter.ToInt32(buffer, offset);
                    uint mask = BitConverter.ToUInt32(buffer, offset + 4);
                    uint cookie = BitConverter.ToUInt32(buffer, offset + 8);
                    uint len = BitConverter.ToUInt32(buffer, offset + 12);

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
            return;
        }

        if (!_wdToPath.TryGetValue(wd, out string? parentPath))
        {
            return;
        }

        bool isDir = (mask & (uint)InotifyNative.Mask.IN_ISDIR) != 0;
        string fullPath = string.IsNullOrEmpty(name) ? parentPath : Path.Combine(parentPath, name);

        if (!string.IsNullOrEmpty(name) && _ignoreMatcher.ShouldIgnorePath(fullPath, name, isDir))
        {
            return;
        }

        if ((mask & (uint)InotifyNative.Mask.IN_CREATE) != 0)
        {
            if (isDir)
            {
                AddSingleWatch(fullPath);
                AddWatchTree(fullPath);
                _engine.AddOrUpdate(fullPath, isDirectory: true, size: 0, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), name.StartsWith('.'));
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
        else if ((mask & (uint)InotifyNative.Mask.IN_DELETE) != 0 || (mask & (uint)InotifyNative.Mask.IN_DELETE_SELF) != 0)
        {
            if (isDir)
            {
                if (_pathToWd.TryGetValue(fullPath, out int childWd))
                {
                    RemoveSingleWatch(childWd);
                }
            }
            _engine.Remove(fullPath);
        }
        else if ((mask & (uint)InotifyNative.Mask.IN_MOVED_FROM) != 0)
        {
            if (cookie != 0)
            {
                _cookiePendingMoves[cookie] = (fullPath, isDir);
            }
            _engine.Remove(fullPath);
        }
        else if ((mask & (uint)InotifyNative.Mask.IN_MOVED_TO) != 0)
        {
            _cookiePendingMoves.TryRemove(cookie, out _);

            if (isDir)
            {
                AddSingleWatch(fullPath);
                AddWatchTree(fullPath);
                _engine.AddOrUpdate(fullPath, isDirectory: true, size: 0, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), name.StartsWith('.'));
            }
            else
            {
                UpdateFileInIndex(fullPath, name);
            }
        }
    }

    private void UpdateFileInIndex(string fullPath, string name)
    {
        try
        {
            if (File.Exists(fullPath))
            {
                var fi = new FileInfo(fullPath);
                _engine.AddOrUpdate(
                    fullPath,
                    isDirectory: false,
                    size: fi.Length,
                    modifiedUnixSec: (uint)new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeSeconds(),
                    isHidden: name.StartsWith('.') || (fi.Attributes & FileAttributes.Hidden) != 0
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
        _cts.Dispose();

        if (_inotifyFd >= 0)
        {
            InotifyNative.close(_inotifyFd);
            _inotifyFd = -1;
        }

        _wdToPath.Clear();
        _pathToWd.Clear();
    }
}
