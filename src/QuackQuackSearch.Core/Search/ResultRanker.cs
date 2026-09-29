namespace QuackQuackSearch.Core.Search;

public static class ResultRanker
{
    public static double CalculateScore(string fileName, string query, uint modifiedUnixSeconds)
    {
        if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(query))
            return 0.0;

        double score = 0.0;

        bool hasWildcards = query.Contains('*') || query.Contains('?');

        if (fileName.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 1000.0;
        }
        else if (hasWildcards)
        {
            if (query.StartsWith('*') && !query[1..].Contains('*') && !query[1..].Contains('?'))
            {
                // Pure suffix wildcard, e.g. "*emes" or "*.pdf"
                string suffix = query[1..];
                if (fileName.Equals(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    score += 950.0;
                }
                else if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    int prefixLen = fileName.Length - suffix.Length;
                    score += Math.Max(250.0, 850.0 - (prefixLen * 15.0));
                }
                else
                {
                    score += 200.0;
                }
            }
            else if (query.EndsWith('*') && !query[..^1].Contains('*') && !query[..^1].Contains('?'))
            {
                // Pure prefix wildcard, e.g. "meme*"
                string prefix = query[..^1];
                if (fileName.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    score += 950.0;
                }
                else if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    int suffixLen = fileName.Length - prefix.Length;
                    score += Math.Max(250.0, 850.0 - (suffixLen * 15.0));
                }
                else
                {
                    score += 200.0;
                }
            }
            else if (query.StartsWith('*') && query.EndsWith('*') && !query[1..^1].Contains('*') && !query[1..^1].Contains('?'))
            {
                // Substring wildcard, e.g. "*emes*"
                string sub = query[1..^1];
                if (fileName.Equals(sub, StringComparison.OrdinalIgnoreCase))
                {
                    score += 900.0;
                }
                else if (fileName.Contains(sub, StringComparison.OrdinalIgnoreCase))
                {
                    int extraLen = fileName.Length - sub.Length;
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
                foreach (char c in query) if (c != '*' && c != '?') nonWildcardChars++;
                int extraLen = Math.Max(0, fileName.Length - nonWildcardChars);
                score += Math.Max(150.0, 700.0 - (extraLen * 8.0));
            }
        }
        else if (fileName.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 800.0;
        }
        else if (IsWordBoundaryMatch(fileName, query))
        {
            score += 650.0;
        }
        else if (fileName.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 450.0;
        }
        else
        {
            score += 100.0;
        }

        // Length bonus: shorter file names matching the query get higher relevance
        int lenDiff = Math.Max(0, fileName.Length - query.Length);
        score += Math.Max(0, 100.0 - (lenDiff * 2.0));

        // Recency bonus: files modified within recent days get up to 50 points bonus
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
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
