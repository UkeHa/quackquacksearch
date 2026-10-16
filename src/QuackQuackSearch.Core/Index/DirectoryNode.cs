using MessagePack;

namespace QuackQuackSearch.Core.Index;

[MessagePackObject]
public readonly record struct DirectoryNode(
    [property: Key(0)] uint Id,
    [property: Key(1)] uint ParentId,
    [property: Key(2)] string Name
);
