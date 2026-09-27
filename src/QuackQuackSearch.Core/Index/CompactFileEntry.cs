namespace QuackQuackSearch.Core.Index;

[Flags]
public enum FileEntryFlags : ushort
{
    None = 0,
    IsDirectory = 1 << 0,
    IsHidden = 1 << 1,
    IsSymlink = 1 << 2
}

public readonly record struct CompactFileEntry(
    uint DirectoryId,
    string Name,
    long Size,
    uint ModifiedUnixSeconds,
    FileEntryFlags Flags = FileEntryFlags.None
)
{
    public bool IsDirectory => (Flags & FileEntryFlags.IsDirectory) != 0;
    public bool IsHidden => (Flags & FileEntryFlags.IsHidden) != 0;
    public bool IsSymlink => (Flags & FileEntryFlags.IsSymlink) != 0;

    public DateTimeOffset ModifiedTime => DateTimeOffset.FromUnixTimeSeconds(ModifiedUnixSeconds);
}
