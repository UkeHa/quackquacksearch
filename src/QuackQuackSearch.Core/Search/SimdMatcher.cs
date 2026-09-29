using System.IO.Enumeration;

namespace QuackQuackSearch.Core.Search;

public static class SimdMatcher
{
    /// <summary>
    /// Checks if candidate matches the query.
    /// Uses vectorized span search for ordinary strings, or simple expression matching for wildcards.
    /// </summary>
    public static bool Matches(ReadOnlySpan<char> candidate, ReadOnlySpan<char> query, bool hasWildcards)
    {
        if (query.IsEmpty) return true;
        if (candidate.IsEmpty) return false;

        if (hasWildcards)
        {
            if (FileSystemName.MatchesSimpleExpression(query, candidate, ignoreCase: true))
            {
                return true;
            }

            // Also check filename stem if candidate has an extension and query has no dot (e.g. "*emes" matches "cat_memes.png")
            int dotIdx = candidate.LastIndexOf('.');
            if (dotIdx > 0 && !query.Contains('.'))
            {
                return FileSystemName.MatchesSimpleExpression(query, candidate[..dotIdx], ignoreCase: true);
            }

            return false;
        }

        return candidate.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
