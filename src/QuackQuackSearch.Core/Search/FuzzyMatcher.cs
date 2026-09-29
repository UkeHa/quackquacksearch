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
            score = 950.0 + Math.Max(0, 50.0 - (target.Length - query.Length));
            return true;
        }

        // 3. Exact substring match
        int substrIdx = target.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (substrIdx >= 0)
        {
            double baseScore = 800.0;
            if (substrIdx > 0 && IsWordBoundary(target[substrIdx - 1]))
            {
                baseScore += 80.0;
            }
            score = baseScore + Math.Max(0, 50.0 - (target.Length - query.Length));
            return true;
        }

        // 4. Close Typo match on full filename / stem (e.g. "meems" -> "memes", "reprot" -> "report")
        if (query.Length >= 3 && TryStemTypoMatch(target, query, out double stemTypoScore))
        {
            score = stemTypoScore;
            return true;
        }

        // 5. Subsequence fuzzy match (like fzf / fzy)
        if (TrySubsequenceMatch(target, query, out double subseqScore))
        {
            score = subseqScore;
            return true;
        }

        // 6. Typo match on word parts (e.g. "reprot" inside "annual_report_2026.pdf")
        if (query.Length >= 4 && TryWordTypoMatch(target, query, out double wordTypoScore))
        {
            score = wordTypoScore;
            return true;
        }

        return false;
    }

    private static bool TryStemTypoMatch(ReadOnlySpan<char> target, ReadOnlySpan<char> query, out double score)
    {
        score = 0.0;
        int dotIdx = target.LastIndexOf('.');
        ReadOnlySpan<char> nameStem = dotIdx > 0 ? target[..dotIdx] : target;

        int maxAllowedDist = query.Length >= 5 ? 2 : 1;
        if (Math.Abs(nameStem.Length - query.Length) <= maxAllowedDist)
        {
            int dist = DamerauLevenshteinDistance(nameStem, query, maxAllowedDist);
            if (dist <= maxAllowedDist)
            {
                // Direct typo match on the whole stem gets high score (800-850 for dist 1, 600-700 for dist 2)
                score = (dist == 1 ? 850.0 : 700.0) - (Math.Abs(nameStem.Length - query.Length) * 15.0);
                return true;
            }
        }

        return false;
    }

    private static bool TryWordTypoMatch(ReadOnlySpan<char> target, ReadOnlySpan<char> query, out double score)
    {
        score = 0.0;
        int maxAllowedDist = query.Length >= 5 ? 2 : 1;

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
                            score = (dist == 1 ? 650.0 : 500.0) - (Math.Abs(word.Length - query.Length) * 15.0);
                            return true;
                        }
                    }
                }
                start = i + 1;
            }
        }

        return false;
    }

    private static bool TrySubsequenceMatch(ReadOnlySpan<char> target, ReadOnlySpan<char> query, out double score)
    {
        score = 0.0;
        int tIdx = 0;
        int qIdx = 0;

        int consecutive = 0;
        double currentScore = 100.0;
        int firstMatchIndex = -1;
        int boundaryMatches = 0;

        while (tIdx < target.Length && qIdx < query.Length)
        {
            char tc = char.ToLowerInvariant(target[tIdx]);
            char qc = char.ToLowerInvariant(query[qIdx]);

            if (tc == qc)
            {
                if (firstMatchIndex < 0) firstMatchIndex = tIdx;

                currentScore += 20.0;

                // Consecutive match bonus
                consecutive++;
                currentScore += consecutive * 10.0;

                // Word boundary bonus
                if (tIdx == 0)
                {
                    boundaryMatches++;
                    currentScore += 70.0;
                }
                else if (IsWordBoundary(target[tIdx - 1]) || (char.IsUpper(target[tIdx]) && char.IsLower(target[tIdx - 1])))
                {
                    boundaryMatches++;
                    currentScore += 50.0;
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
            int spanLength = tIdx - firstMatchIndex;
            double compactness = (double)query.Length / Math.Max(1, spanLength);

            // Scale score heavily by compactness
            currentScore *= Math.Clamp(compactness, 0.3, 1.0);

            // Penalty for excess filename length
            int extraLen = Math.Max(0, target.Length - query.Length);
            currentScore -= extraLen * 2.0;

            // An acronym matching word boundaries gets a substantial boost
            if (boundaryMatches >= query.Length)
            {
                currentScore += 150.0;
            }

            score = Math.Max(30.0, currentScore);
            return true;
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
