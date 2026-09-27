using System.Text.Json.Serialization;

namespace QuackQuackSearch.Core.Config;

public sealed class QuackConfig
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("indexing")]
    public IndexingConfig Indexing { get; set; } = new();

    [JsonPropertyName("paths")]
    public List<PathConfigEntry> Paths { get; set; } = [];

    [JsonPropertyName("krunner")]
    public KRunnerConfig KRunner { get; set; } = new();
}

public sealed class IndexingConfig
{
    [JsonPropertyName("saveIntervalMinutes")]
    public int SaveIntervalMinutes { get; set; } = 5;

    [JsonPropertyName("maxParallelThreads")]
    public int MaxParallelThreads { get; set; } = 0;

    [JsonPropertyName("indexHiddenFiles")]
    public bool IndexHiddenFiles { get; set; } = false;

    [JsonPropertyName("globalExcludes")]
    public List<string> GlobalExcludes { get; set; } =
    [
        "**/.git/**",
        "**/node_modules/**",
        "**/bin/**",
        "**/obj/**",
        "**/.cache/**",
        "**/.local/share/Trash/**",
        "**/.venv/**",
        "**/__pycache__/**",
        "**/*.tmp",
        "**/*.lock"
    ];
}

public sealed class PathConfigEntry
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "local"; // "local" or "network"

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("pollIntervalSeconds")]
    public int PollIntervalSeconds { get; set; } = 60; // For network

    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 5;

    [JsonPropertyName("customExcludes")]
    public List<string> CustomExcludes { get; set; } = [];
}

public sealed class KRunnerConfig
{
    [JsonPropertyName("minCharCount")]
    public int MinCharCount { get; set; } = 2;

    [JsonPropertyName("maxResults")]
    public int MaxResults { get; set; } = 25;

    [JsonPropertyName("categoryName")]
    public string CategoryName { get; set; } = "QuackQuackSearch";
}
