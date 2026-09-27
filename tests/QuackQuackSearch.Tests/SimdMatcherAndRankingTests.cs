using FluentAssertions;
using QuackQuackSearch.Core.Search;
using Xunit;

namespace QuackQuackSearch.Tests;

public class SimdMatcherAndRankingTests
{
    [Theory]
    [InlineData("Program.cs", "pro", true)]
    [InlineData("Program.cs", "GRAM", true)]
    [InlineData("Program.cs", ".CS", true)]
    [InlineData("Program.cs", "xyz", false)]
    public void SubstringMatching_ShouldBeCaseInsensitive(string candidate, string query, bool expected)
    {
        bool result = SimdMatcher.Matches(candidate.AsSpan(), query.AsSpan(), hasWildcards: false);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Program.cs", "*.cs", true)]
    [InlineData("Program.cs", "Prog*.cs", true)]
    [InlineData("Program.cs", "P?ogram.cs", true)]
    [InlineData("Program.cs", "*.txt", false)]
    public void WildcardMatching_ShouldWorkAsExpected(string candidate, string query, bool expected)
    {
        bool result = SimdMatcher.Matches(candidate.AsSpan(), query.AsSpan(), hasWildcards: true);
        result.Should().Be(expected);
    }

    [Fact]
    public void Ranking_ExactMatch_ShouldOutrankPrefixAndSubstring()
    {
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        double exactScore = ResultRanker.CalculateScore("search", "search", now);
        double prefixScore = ResultRanker.CalculateScore("searching", "search", now);
        double boundaryScore = ResultRanker.CalculateScore("my_search_tool", "search", now);
        double substringScore = ResultRanker.CalculateScore("researching", "search", now);

        exactScore.Should().BeGreaterThan(prefixScore);
        prefixScore.Should().BeGreaterThan(boundaryScore);
        boundaryScore.Should().BeGreaterThan(substringScore);
    }
}
