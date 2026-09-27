using System.IO.Enumeration;

namespace QuackQuackSearch.Core.Crawler;

public sealed class IgnoreMatcher
{
    private readonly HashSet<string> _exactDirNames;
    private readonly List<string> _globPatterns;

    public static IgnoreMatcher Default { get; } = new IgnoreMatcher([
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
    ]);

    public IgnoreMatcher(IEnumerable<string> patterns)
    {
        _exactDirNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _globPatterns = new List<string>();

        foreach (var pattern in patterns)
        {
            string trimmed = pattern.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Extract common simple directory names like "**/.git/**" -> ".git"
            if (trimmed.StartsWith("**/") && trimmed.EndsWith("/**"))
            {
                string dirName = trimmed.Substring(3, trimmed.Length - 6);
                if (!dirName.Contains('/') && !dirName.Contains('*') && !dirName.Contains('?'))
                {
                    _exactDirNames.Add(dirName);
                    continue;
                }
            }

            if (trimmed.StartsWith("**/"))
            {
                trimmed = trimmed.Substring(3);
            }

            _globPatterns.Add(trimmed);
        }
    }

    /// <summary>
    /// Fast check if a directory name itself is ignored before descending.
    /// </summary>
    public bool ShouldIgnoreDirectoryName(string dirName)
    {
        return _exactDirNames.Contains(dirName);
    }

    /// <summary>
    /// Checks whether a full relative or absolute path is ignored.
    /// </summary>
    public bool ShouldIgnorePath(string fullPath, string fileName, bool isDirectory)
    {
        if (isDirectory && _exactDirNames.Contains(fileName))
        {
            return true;
        }

        // Check if any path segment matches an exact ignored directory
        foreach (var dirName in _exactDirNames)
        {
            if (fullPath.Contains($"/{dirName}/", StringComparison.OrdinalIgnoreCase) ||
                fullPath.EndsWith($"/{dirName}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var pattern in _globPatterns)
        {
            if (FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true))
            {
                return true;
            }
        }

        return false;
    }
}
