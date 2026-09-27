namespace QuackQuackSearch.Core.Search;

public static class ResultRanker
{
    public static double CalculateScore(string fileName, string query, uint modifiedUnixSeconds)
    {
        if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(query))
            return 0.0;

        double score = 0.0;

        if (fileName.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 1000.0;
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
