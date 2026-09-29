using System.IO.Enumeration;
using QuackQuackSearch.Core.System;

namespace QuackQuackSearch.Core.Crawler;

public sealed class IgnoreMatcher
{
    private readonly HashSet<string> _exactDirNames;
    private readonly List<string> _excludedPathPrefixes;
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

    public IgnoreMatcher(IEnumerable<string> patterns) : this(patterns, (string?)null)
    {
    }

    public IgnoreMatcher(IEnumerable<string> globalPatterns, IEnumerable<(string RootPath, IEnumerable<string> Excludes)>? pathExcludes)
    {
        _exactDirNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _excludedPathPrefixes = new List<string>();
        _globPatterns = new List<string>();

        RegisterPatterns(globalPatterns, null);

        if (pathExcludes != null)
        {
            foreach (var (rootPath, excludes) in pathExcludes)
            {
                RegisterPatterns(excludes, rootPath);
            }
        }
    }

    public IgnoreMatcher(IEnumerable<string> patterns, string? rootPath)
    {
        _exactDirNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _excludedPathPrefixes = new List<string>();
        _globPatterns = new List<string>();

        RegisterPatterns(patterns, rootPath);
    }

    private void RegisterPatterns(IEnumerable<string>? patterns, string? rootPath)
    {
        if (patterns == null) return;

        foreach (var pattern in patterns)
        {
            string trimmed = pattern.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            if (rootPath != null && !PathExclusionValidator.IsValid(rootPath, trimmed, out _))
            {
                continue;
            }

            // 1. Expand ~ to home directory
            if (trimmed.StartsWith("~/") || trimmed.StartsWith("~\\"))
            {
                trimmed = Path.Combine(XdgDirectories.Home, trimmed[2..]);
            }

            // 2. Absolute or explicit subpath prefix (e.g. "/home/secret" or "/home/secret/")
            if (Path.IsPathRooted(trimmed))
            {
                string normPrefix = trimmed.TrimEnd('/', '\\');
                if (!_excludedPathPrefixes.Contains(normPrefix, StringComparer.OrdinalIgnoreCase))
                {
                    _excludedPathPrefixes.Add(normPrefix);
                }
                continue;
            }

            // 3. Simple directory name extraction from "**/.git/**" -> ".git"
            if (trimmed.StartsWith("**/") && trimmed.EndsWith("/**"))
            {
                string dirName = trimmed.Substring(3, trimmed.Length - 6);
                if (!dirName.Contains('/') && !dirName.Contains('*') && !dirName.Contains('?'))
                {
                    _exactDirNames.Add(dirName);
                    continue;
                }
            }

            // 4. If rootPath is given and pattern is a relative path or folder without wildcards (e.g. "secret" or "sub/secret")
            if (!string.IsNullOrWhiteSpace(rootPath) && !trimmed.Contains('*') && !trimmed.Contains('?'))
            {
                string resolved = Path.GetFullPath(Path.Combine(rootPath, trimmed.TrimEnd('/', '\\')));
                if (!_excludedPathPrefixes.Contains(resolved, StringComparer.OrdinalIgnoreCase))
                {
                    _excludedPathPrefixes.Add(resolved);
                }

                if (!trimmed.Contains('/') && !trimmed.Contains('\\'))
                {
                    _exactDirNames.Add(trimmed);
                }
                continue;
            }

            // 5. If it's a simple directory name without slashes and wildcards
            if (!trimmed.Contains('/') && !trimmed.Contains('\\') && !trimmed.Contains('*') && !trimmed.Contains('?'))
            {
                _exactDirNames.Add(trimmed);
                continue;
            }

            // 6. Glob pattern
            if (trimmed.StartsWith("**/"))
            {
                trimmed = trimmed.Substring(3);
            }

            _globPatterns.Add(trimmed);
        }
    }

    /// <summary>
    /// Fast check if a directory name itself is globally ignored.
    /// </summary>
    public bool ShouldIgnoreDirectoryName(string dirName)
    {
        return _exactDirNames.Contains(dirName);
    }

    /// <summary>
    /// Checks whether a full relative or absolute path is ignored.
    /// Evaluates explicit path prefixes, excluded directory names, and glob expressions.
    /// </summary>
    public bool ShouldIgnorePath(string fullPath, string fileName, bool isDirectory)
    {
        if (string.IsNullOrEmpty(fullPath)) return false;

        string normPath = fullPath.TrimEnd('/', '\\');

        // 1. Check explicit excluded path prefixes (e.g. "/home/secret" excluding "/home/secret" and "/home/secret/...")
        foreach (var prefix in _excludedPathPrefixes)
        {
            if (normPath.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                normPath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase) ||
                normPath.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // 2. Check exact ignored directory name
        if (isDirectory && _exactDirNames.Contains(fileName))
        {
            return true;
        }

        // 3. Check if any path segment matches an exact ignored directory
        foreach (var dirName in _exactDirNames)
        {
            if (normPath.Contains($"/{dirName}/", StringComparison.OrdinalIgnoreCase) ||
                normPath.EndsWith($"/{dirName}", StringComparison.OrdinalIgnoreCase) ||
                normPath.Contains($"\\{dirName}\\", StringComparison.OrdinalIgnoreCase) ||
                normPath.EndsWith($"\\{dirName}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // 4. Check glob patterns
        foreach (var pattern in _globPatterns)
        {
            if (pattern.Contains('/') || pattern.Contains('\\'))
            {
                if (FileSystemName.MatchesSimpleExpression(pattern, normPath, ignoreCase: true))
                {
                    return true;
                }
            }
            else
            {
                if (FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
