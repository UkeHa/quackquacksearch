using System.Runtime.InteropServices;

namespace QuackQuackSearch.Core.Monitoring;

public static class InotifyNative
{
    private const string LibC = "libc";

    public const int IN_CLOEXEC = 0x00080000;
    public const int IN_NONBLOCK = 0x00000800;

    [Flags]
    public enum Mask : uint
    {
        IN_ACCESS        = 0x00000001,
        IN_MODIFY        = 0x00000002,
        IN_ATTRIB        = 0x00000004,
        IN_CLOSE_WRITE   = 0x00000008,
        IN_CLOSE_NOWRITE = 0x00000010,
        IN_OPEN          = 0x00000020,
        IN_MOVED_FROM    = 0x00000040,
        IN_MOVED_TO      = 0x00000080,
        IN_CREATE        = 0x00000100,
        IN_DELETE        = 0x00000200,
        IN_DELETE_SELF   = 0x00000400,
        IN_MOVE_SELF     = 0x00000800,
        IN_UNMOUNT       = 0x00002000,
        IN_Q_OVERFLOW    = 0x00004000,
        IN_IGNORED       = 0x00008000,
        IN_ONLYDIR       = 0x01000000,
        IN_DONT_FOLLOW   = 0x02000000,
        IN_EXCL_UNLINK   = 0x04000000,
        IN_MASK_CREATE   = 0x10000000,
        IN_MASK_ADD      = 0x20000000,
        IN_ISDIR         = 0x40000000,
        IN_ONESHOT       = 0x80000000
    }

    [DllImport(LibC, SetLastError = true)]
    public static extern int inotify_init1(int flags);

    [DllImport(LibC, SetLastError = true)]
    public static extern int inotify_add_watch(int fd, string pathname, uint mask);

    [DllImport(LibC, SetLastError = true)]
    public static extern int inotify_rm_watch(int fd, int wd);

    [DllImport(LibC, SetLastError = true)]
    public static extern IntPtr read(int fd, byte[] buf, UIntPtr count);

    [DllImport(LibC, SetLastError = true)]
    public static extern int close(int fd);

    /// <summary>
    /// Reads fs.inotify.max_user_watches from /proc.
    /// </summary>
    public static int GetMaxUserWatches()
    {
        const string path = "/proc/sys/fs/inotify/max_user_watches";
        if (File.Exists(path))
        {
            string content = File.ReadAllText(path).Trim();
            if (int.TryParse(content, out int val))
            {
                return val;
            }
        }
        return 8192; // Default fallback
    }
}
