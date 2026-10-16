namespace QuackQuackSearch.Core.Crawler;

public static class PathExclusionValidator
{
    private static bool IsGermanCulture()
    {
        string lang = global::System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return string.IsNullOrEmpty(lang) ||
               lang.Equals("iv", StringComparison.OrdinalIgnoreCase) ||
               lang.Equals("de", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether an exclusion pattern or path is valid for a given monitored root path.
    /// An exclusion must reside within the root path (or be a relative pattern applying within it).
    /// </summary>
    public static bool IsValid(string rootPath, string excludePattern, out string? errorMessage)
    {
        errorMessage = null;
        bool de = IsGermanCulture();

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            errorMessage = de ? "Überwachter Pfad ist ungültig." : "Monitored path is invalid.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(excludePattern))
        {
            errorMessage = de ? "Ausschluss darf nicht leer sein." : "Exclusion pattern cannot be empty.";
            return false;
        }

        string trimmedPattern = excludePattern.Trim();

        // 1. Expand ~ to home directory if present
        string expanded = trimmedPattern;
        if (expanded.StartsWith("~/") || expanded == "~")
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            expanded = expanded == "~" ? home : Path.Combine(home, expanded[2..]);
        }

        string fullRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar);

        // 2. If it is an absolute path
        if (Path.IsPathRooted(expanded))
        {
            string fullExclude = Path.GetFullPath(expanded).TrimEnd(Path.DirectorySeparatorChar);

            // Cannot exclude root path itself (that would disable monitoring the entire path)
            if (string.Equals(fullExclude, fullRoot, StringComparison.Ordinal))
            {
                errorMessage = de
                    ? $"Der Ausschluss darf nicht der überwachte Stammordner selbst sein ({fullRoot})."
                    : $"The exclusion cannot be the monitored root folder itself ({fullRoot}).";
                return false;
            }

            // Must be strictly inside fullRoot
            string prefix = fullRoot + Path.DirectorySeparatorChar;
            if (!fullExclude.StartsWith(prefix, StringComparison.Ordinal))
            {
                errorMessage = de
                    ? $"Der Ausschluss '{trimmedPattern}' liegt nicht innerhalb des überwachten Pfades '{fullRoot}'."
                    : $"The exclusion '{trimmedPattern}' is not inside the monitored path '{fullRoot}'.";
                return false;
            }

            return true;
        }

        // 3. Relative path or pattern
        // Reject path traversal via .. escaping the root
        string[] segments = trimmedPattern.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        int depth = 0;
        foreach (var seg in segments)
        {
            if (seg == "..")
            {
                depth--;
                if (depth < 0)
                {
                    errorMessage = de
                        ? "Relative Pfade dürfen nicht mit '..' aus dem übergeordneten Ordner ausbrechen."
                        : "Relative paths must not escape the monitored root folder using '..'.";
                    return false;
                }
            }
            else if (seg != ".")
            {
                depth++;
            }
        }

        return true;
    }
}
