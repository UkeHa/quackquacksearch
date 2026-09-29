using System.Diagnostics;
using QuackQuackSearch.Core.Config;
using QuackQuackSearch.Core.DBus;
using QuackQuackSearch.Core.Index;
using QuackQuackSearch.Core.Search;
using Tmds.DBus;

namespace QuackQuackSearch.Daemon.DBus;

public sealed class KRunnerDbusObject(SearchIndexEngine engine, QuackConfig config) : IKRunner1
{
    private readonly SearchIndexEngine _engine = engine;
    private readonly QuackConfig _config = config;

    public ObjectPath ObjectPath => new("/quackquacksearch");

    public Task<(string id, string text, string icon)[]> ActionsAsync()
    {
        (string id, string text, string icon)[] actions =
        [
            ("open", "Open", "system-run"),
            ("open_folder", "Open Containing Folder", "system-file-manager"),
            ("copy_path", "Copy Path to Clipboard", "edit-copy")
        ];

        return Task.FromResult(actions);
    }

    public Task<(string id, string text, string icon, int type, double relevance, IDictionary<string, object> properties)[]> MatchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < _config.KRunner.MinCharCount)
        {
            return Task.FromResult(Array.Empty<(string, string, string, int, double, IDictionary<string, object>)>());
        }

        bool forceFuzzy = false;
        if (query.StartsWith('~'))
        {
            forceFuzzy = true;
            query = query[1..].TrimStart();
        }
        else if (query.StartsWith(":f:", StringComparison.OrdinalIgnoreCase))
        {
            forceFuzzy = true;
            query = query[3..].TrimStart();
        }

        var options = new SearchOptions
        {
            MaxResults = _config.KRunner.MaxResults > 0 ? _config.KRunner.MaxResults : 20,
            IncludeHidden = _config.Indexing.IndexHiddenFiles,
            IncludeDirectories = true,
            Fuzzy = forceFuzzy
        };

        var matches = _engine.Search(query, options);

        // Fallback to fuzzy search if exact match gave zero results
        if (matches.Count == 0 && !forceFuzzy && query.Length >= 3)
        {
            matches = _engine.Search(query, options with { Fuzzy = true });
        }

        var results = matches.Select(m =>
        {
            string icon = GetIconForFile(m.FileName, m.IsDirectory);
            int matchType = m.Score >= 900 ? 100 : 50; // Plasma::QueryMatch::ExactMatch (100) or PossibleMatch (50)
            double relevance = Math.Clamp(m.Score / 1000.0, 0.05, 1.0);

            var props = new Dictionary<string, object>
            {
                ["subtext"] = Path.GetDirectoryName(m.FullPath) ?? "/",
                ["category"] = _config.KRunner.CategoryName
            };

            return (
                id: m.FullPath,
                text: m.FileName,
                icon: icon,
                type: matchType,
                relevance: relevance,
                properties: (IDictionary<string, object>)props
            );
        }).ToArray();

        return Task.FromResult(results);
    }

    public Task RunAsync(string id, string action_id)
    {
        if (string.IsNullOrWhiteSpace(id)) return Task.CompletedTask;

        try
        {
            if (action_id == "open_folder")
            {
                string? parentDir = Path.GetDirectoryName(id);
                if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                {
                    // Prefer dolphin --select if dolphin is installed
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "dolphin",
                        Arguments = $"--select \"{id}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
            }
            else if (action_id == "copy_path")
            {
                // Try wl-copy (Wayland) or xclip (X11)
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "wl-copy",
                        Arguments = $"\"{id}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    Process.Start(psi);
                }
                catch
                {
                    // Fallback to xclip
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "xclip",
                        Arguments = $"-selection clipboard -i",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
            }
            else
            {
                // Default action: open file with default handler
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = $"\"{id}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KRunner] Failed to execute action '{action_id}' on '{id}': {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public Task TeardownAsync()
    {
        return Task.CompletedTask;
    }

    private static string GetIconForFile(string name, bool isDirectory)
    {
        if (isDirectory) return "folder";

        string ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".svg" or ".gif" => "image-x-generic",
            ".mp4" or ".mkv" or ".avi" or ".webm" or ".mov" => "video-x-generic",
            ".mp3" or ".flac" or ".wav" or ".ogg" or ".m4a" => "audio-x-generic",
            ".pdf" => "application-pdf",
            ".zip" or ".tar" or ".gz" or ".bz2" or ".xz" or ".7z" => "package-x-generic",
            ".cs" or ".rs" or ".cpp" or ".c" or ".py" or ".js" or ".ts" or ".go" or ".java" => "text-x-source",
            ".json" or ".xml" or ".yaml" or ".yml" or ".toml" => "text-x-generic",
            ".txt" or ".md" or ".log" => "text-plain",
            ".sh" or ".bash" or ".zsh" => "application-x-shellscript",
            _ => "unknown"
        };
    }
}
