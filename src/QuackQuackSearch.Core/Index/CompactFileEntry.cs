using MessagePack;

namespace QuackQuackSearch.Core.Index;

[Flags]
public enum FileEntryFlags : ushort
{
    None = 0,
    IsDirectory = 1 << 0,
    IsHidden = 1 << 1,
    IsSymlink = 1 << 2
}

[MessagePackObject]
public readonly record struct CompactFileEntry(
    [property: Key(0)] uint DirectoryId,
    [property: Key(1)] string Name,
    [property: Key(2)] long Size,
    [property: Key(3)] uint ModifiedUnixSeconds,
    [property: Key(4)] FileEntryFlags Flags = FileEntryFlags.None
)
{
    [IgnoreMember]
    public bool IsDirectory => (Flags & FileEntryFlags.IsDirectory) != 0;
    [IgnoreMember]
    public bool IsHidden => (Flags & FileEntryFlags.IsHidden) != 0;
    [IgnoreMember]
    public bool IsSymlink => (Flags & FileEntryFlags.IsSymlink) != 0;

    [IgnoreMember]
    public DateTimeOffset ModifiedTime => DateTimeOffset.FromUnixTimeSeconds(ModifiedUnixSeconds);
}
