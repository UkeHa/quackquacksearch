namespace QuackQuackSearch.Core.Search;

public static class FuzzyMatcher
{
    /// <summary>
    /// Evaluates whether the target filename matches the search query fuzzily,
    /// returning a relevance score if matched.
    /// </summary>
    public static bool TryMatch(ReadOnlySpan<char> target, ReadOnlySpan<char> query, out double score)
    {
        score = 0.0;
        if (query.IsEmpty)
        {
            score = 100.0;
            return true;
        }

        if (target.IsEmpty) return false;

        // 1. Exact match
        if (target.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            score = 1200.0;
            return true;
        }

        // 2. Exact prefix match
        if (target.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            score = 900.0 + Math.Max(0, 100.0 - (target.Length - query.Length));
            return true;
        }

        // 3. Exact substring match
        int substrIdx = target.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (substrIdx >= 0)
        {
            double baseScore = 750.0;
            if (substrIdx > 0 && IsWordBoundary(target[substrIdx - 1]))
            {
                baseScore += 100.0;
            }
            score = baseScore + Math.Max(0, 80.0 - (target.Length - query.Length));
            return true;
        }

        // 4. Subsequence fuzzy match (like fzf / fzy)
        if (TrySubsequenceMatch(target, query, out double subseqScore))
        {
            score = subseqScore;
            return true;
        }

        // 5. Typo tolerance (Levenshtein / Edit distance) for queries of reasonable length
        if (query.Length >= 4 && TryTypoMatch(target, query, out double typoScore))
        {
            score = typoScore;
            return true;
        }

        return false;
    }

    private static bool TrySubsequenceMatch(ReadOnlySpan<char> target, ReadOnlySpan<char> query, out double score)
    {
        score = 0.0;
        int tIdx = 0;
        int qIdx = 0;

        int consecutive = 0;
        double currentScore = 150.0;
        int firstMatchIndex = -1;

        while (tIdx < target.Length && qIdx < query.Length)
        {
            char tc = char.ToLowerInvariant(target[tIdx]);
            char qc = char.ToLowerInvariant(query[qIdx]);

            if (tc == qc)
            {
                if (firstMatchIndex < 0) firstMatchIndex = tIdx;

                currentScore += 30.0;

                // Consecutive match bonus
                consecutive++;
                currentScore += consecutive * 15.0;

                // Word boundary bonus
                if (tIdx == 0)
                {
                    currentScore += 80.0;
                }
                else if (IsWordBoundary(target[tIdx - 1]) || (char.IsUpper(target[tIdx]) && char.IsLower(target[tIdx - 1])))
                {
                    currentScore += 60.0;
                }

                qIdx++;
            }
            else
            {
                consecutive = 0;
            }

            tIdx++;
        }

        if (qIdx == query.Length)
        {
            // Matched all query characters in sequence
            int spanLength = tIdx - firstMatchIndex;
            // Compactness penalty: matches spread over a wide distance get penalized
            int spread = Math.Max(0, spanLength - query.Length);
            currentScore -= spread * 4.0;

            // Target length penalty
            int extraLen = Math.Max(0, target.Length - query.Length);
            currentScore -= extraLen * 1.5;

            score = Math.Max(50.0, currentScore);
            return true;
        }

        return false;
    }

    private static bool TryTypoMatch(ReadOnlySpan<char> target, ReadOnlySpan<char> query, out double score)
    {
        score = 0.0;

        // Check distance against filename without extension or whole name
        int dotIdx = target.LastIndexOf('.');
        ReadOnlySpan<char> nameStem = dotIdx > 0 ? target[..dotIdx] : target;

        int maxAllowedDist = query.Length >= 5 ? 2 : 1;

        // Compare against stem or words separated by delimiters
        if (Math.Abs(nameStem.Length - query.Length) <= maxAllowedDist)
        {
            int dist = DamerauLevenshteinDistance(nameStem, query, maxAllowedDist);
            if (dist <= maxAllowedDist)
            {
                score = 300.0 - (dist * 75.0);
                return true;
            }
        }

        // Check word parts (e.g. "quack_search" -> check "search" against "serach")
        int start = 0;
        for (int i = 0; i <= target.Length; i++)
        {
            if (i == target.Length || IsWordBoundary(target[i]))
            {
                if (i > start)
                {
                    var word = target[start..i];
                    if (Math.Abs(word.Length - query.Length) <= maxAllowedDist)
                    {
                        int dist = DamerauLevenshteinDistance(word, query, maxAllowedDist);
                        if (dist <= maxAllowedDist)
                        {
                            score = 250.0 - (dist * 70.0);
                            return true;
                        }
                    }
                }
                start = i + 1;
            }
        }

        return false;
    }

    private static bool IsWordBoundary(char c) =>
        c is '_' or '-' or '.' or ' ' or '/' or '\\' or '(' or ')' or '[' or ']';

    private static int DamerauLevenshteinDistance(ReadOnlySpan<char> s1, ReadOnlySpan<char> s2, int maxAllowed)
    {
        int len1 = s1.Length;
        int len2 = s2.Length;

        if (len1 == 0) return len2;
        if (len2 == 0) return len1;
        if (Math.Abs(len1 - len2) > maxAllowed) return maxAllowed + 1;

        Span<int> prevPrevRow = stackalloc int[len2 + 1];
        Span<int> prevRow = stackalloc int[len2 + 1];
        Span<int> currRow = stackalloc int[len2 + 1];

        for (int j = 0; j <= len2; j++) prevRow[j] = j;

        for (int i = 1; i <= len1; i++)
        {
            currRow[0] = i;
            int minInRow = currRow[0];
            char c1 = char.ToLowerInvariant(s1[i - 1]);

            for (int j = 1; j <= len2; j++)
            {
                char c2 = char.ToLowerInvariant(s2[j - 1]);
                int cost = c1 == c2 ? 0 : 1;

                int val = Math.Min(
                    Math.Min(currRow[j - 1] + 1, prevRow[j] + 1),
                    prevRow[j - 1] + cost
                );

                if (i > 1 && j > 1 &&
                    c1 == char.ToLowerInvariant(s2[j - 2]) &&
                    char.ToLowerInvariant(s1[i - 2]) == c2)
                {
                    val = Math.Min(val, prevPrevRow[j - 2] + cost);
                }

                currRow[j] = val;
                if (val < minInRow) minInRow = val;
            }

            if (minInRow > maxAllowed) return maxAllowed + 1;

            prevRow.CopyTo(prevPrevRow);
            currRow.CopyTo(prevRow);
        }

        return prevRow[len2];
    }
}
