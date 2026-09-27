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
            return FileSystemName.MatchesSimpleExpression(query, candidate, ignoreCase: true);
        }

        return candidate.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
