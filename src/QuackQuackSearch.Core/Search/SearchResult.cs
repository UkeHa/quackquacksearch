namespace QuackQuackSearch.Core.Search;

public sealed record SearchResult(
    string FullPath,
    string FileName,
    long Size,
    DateTimeOffset ModifiedTime,
    bool IsDirectory,
    double Score
);

public sealed record SearchOptions(
    int MaxResults = 50,
    bool IncludeDirectories = true,
    bool IncludeHidden = false,
    bool SearchInPath = false
);
