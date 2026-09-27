namespace QuackQuackSearch.Core.Index;

public readonly record struct DirectoryNode(
    uint Id,
    uint ParentId,
    string Name
);
