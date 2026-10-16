namespace QuackQuackSearch.Core.Search;

public static class ResultRanker
{
    public static double CalculateScore(string fileName, string query, uint modifiedUnixSeconds, long? nowUnixSeconds = null)
    {
        if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(query))
            return 0.0;

        ReadOnlySpan<char> fileSpan = fileName.AsSpan();
        ReadOnlySpan<char> querySpan = query.AsSpan();

        double score = 0.0;

        bool hasWildcards = querySpan.Contains('*') || querySpan.Contains('?');

        if (fileSpan.Equals(querySpan, StringComparison.OrdinalIgnoreCase))
        {
            score += 1000.0;
        }
        else if (hasWildcards)
        {
            if (querySpan.StartsWith("*") && !querySpan[1..].Contains('*') && !querySpan[1..].Contains('?'))
            {
                // Pure suffix wildcard, e.g. "*emes" or "*.pdf"
                ReadOnlySpan<char> suffix = querySpan[1..];
                if (fileSpan.Equals(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    score += 950.0;
                }
                else if (fileSpan.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    int prefixLen = fileSpan.Length - suffix.Length;
                    score += Math.Max(250.0, 850.0 - (prefixLen * 15.0));
                }
                else
                {
                    score += 200.0;
                }
            }
            else if (querySpan.EndsWith("*") && !querySpan[..^1].Contains('*') && !querySpan[..^1].Contains('?'))
            {
                // Pure prefix wildcard, e.g. "meme*"
                ReadOnlySpan<char> prefix = querySpan[..^1];
                if (fileSpan.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    score += 950.0;
                }
                else if (fileSpan.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    int suffixLen = fileSpan.Length - prefix.Length;
                    score += Math.Max(250.0, 850.0 - (suffixLen * 15.0));
                }
                else
                {
                    score += 200.0;
                }
            }
            else if (querySpan.Length >= 2 && querySpan.StartsWith("*") && querySpan.EndsWith("*") && !querySpan[1..^1].Contains('*') && !querySpan[1..^1].Contains('?'))
            {
                // Substring wildcard, e.g. "*emes*"
                ReadOnlySpan<char> sub = querySpan[1..^1];
                if (fileSpan.Equals(sub, StringComparison.OrdinalIgnoreCase))
                {
                    score += 900.0;
                }
                else if (fileSpan.Contains(sub, StringComparison.OrdinalIgnoreCase))
                {
                    int extraLen = fileSpan.Length - sub.Length;
                    score += Math.Max(200.0, 750.0 - (extraLen * 10.0));
                }
                else
                {
                    score += 150.0;
                }
            }
            else
            {
                int nonWildcardChars = 0;
                foreach (char c in querySpan) if (c != '*' && c != '?') nonWildcardChars++;
                int extraLen = Math.Max(0, fileSpan.Length - nonWildcardChars);
                score += Math.Max(150.0, 700.0 - (extraLen * 8.0));
            }
        }
        else if (fileSpan.StartsWith(querySpan, StringComparison.OrdinalIgnoreCase))
        {
            score += 800.0;
        }
        else if (IsWordBoundaryMatch(fileName, query))
        {
            score += 650.0;
        }
        else if (fileSpan.Contains(querySpan, StringComparison.OrdinalIgnoreCase))
        {
            score += 450.0;
        }
        else
        {
            score += 100.0;
        }

        // Length bonus: shorter file names matching the query get higher relevance
        int lenDiff = Math.Max(0, fileSpan.Length - querySpan.Length);
        score += Math.Max(0, 100.0 - (lenDiff * 2.0));

        // Recency bonus: files modified within recent days get up to 50 points bonus
        long now = nowUnixSeconds ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long ageSeconds = Math.Max(0, now - modifiedUnixSeconds);
        if (ageSeconds < 86400 * 7) // 7 days
        {
            score += 30.0;
        }
        else if (ageSeconds < 86400 * 30) // 30 days
        {
            score += 15.0;
        }

        return score;
    }

    private static bool IsWordBoundaryMatch(string fileName, string query)
    {
        int index = 0;
        while ((index = fileName.IndexOf(query, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            if (index > 0)
            {
                char prev = fileName[index - 1];
                if (prev is '_' or '-' or '.' or ' ' or '/' or '\\')
                {
                    return true;
                }
            }
            index += query.Length;
        }
        return false;
    }
}
