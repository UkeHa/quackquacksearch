using FluentAssertions;
using QuackQuackSearch.Core.Crawler;
using QuackQuackSearch.Core.Index;
using QuackQuackSearch.Core.Search;
using Xunit;

namespace QuackQuackSearch.Tests;

public class FuzzyMatcherTests
{
    [Theory]
    [InlineData("QuackQuackSearch", "qqs", true)]
    [InlineData("ConfigManager.cs", "cfgman", true)]
    [InlineData("LocalInotifyWatcher.cs", "inoty", true)]
    [InlineData("report_summary_2026.pdf", "repsum", true)]
    [InlineData("holiday_photo.jpg", "xyzabc", false)]
    public void SubsequenceMatching_ShouldMatchAbbreviations(string filename, string query, bool expectedMatch)
    {
        bool matched = FuzzyMatcher.TryMatch(filename.AsSpan(), query.AsSpan(), out double score);
        matched.Should().Be(expectedMatch);
        if (expectedMatch)
        {
            score.Should().BeGreaterThan(0.0);
        }
    }

    [Fact]
    public void TypoTolerance_ShouldMatchCommonMistakes()
    {
        // "reprot" -> "report.pdf" (transposition/edit distance 1)
        bool matched = FuzzyMatcher.TryMatch("report.pdf".AsSpan(), "reprot".AsSpan(), out double score);
        matched.Should().BeTrue();
        score.Should().BeGreaterThan(700.0);

        // "meems" -> "Memes" (transposition of e and m)
        bool matchedMemes = FuzzyMatcher.TryMatch("Memes".AsSpan(), "meems".AsSpan(), out double memesScore);
        matchedMemes.Should().BeTrue();
        memesScore.Should().BeGreaterThan(750.0);

        // Loose subsequence "meems" in "MaterialXGenMsl.dll" must score much lower
        FuzzyMatcher.TryMatch("MaterialXGenMsl.dll".AsSpan(), "meems".AsSpan(), out double looseScore);
        memesScore.Should().BeGreaterThan(looseScore);

        // "serach" -> "search_index.cs"
        bool matched2 = FuzzyMatcher.TryMatch("search_index.cs".AsSpan(), "serach".AsSpan(), out double score2);
        matched2.Should().BeTrue();
        score2.Should().BeGreaterThan(50.0);
    }

    [Fact]
    public void ExactMatch_ShouldScoreHigherThanFuzzySubsequence()
    {
        FuzzyMatcher.TryMatch("qqs.exe".AsSpan(), "qqs".AsSpan(), out double exactPrefixScore);
        FuzzyMatcher.TryMatch("quack_quick_search.cs".AsSpan(), "qqs".AsSpan(), out double fuzzySubseqScore);

        exactPrefixScore.Should().BeGreaterThan(fuzzySubseqScore);
    }

    [Fact]
    public void SearchIndexEngine_WithFuzzyEnabled_ShouldReturnFuzzyMatches()
    {
        var engine = new SearchIndexEngine();
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var items = new List<DiscoveredItem>
        {
            new("/home/user/code", "QuackQuackSearch.cs", 2048, now, false, false),
            new("/home/user/docs", "annual_report.pdf", 4096, now, false, false),
            new("/home/user/photos", "summer_trip.jpg", 8192, now, false, false)
        };
        engine.BulkAdd(items);

        // Exact search for "qqs" with fuzzy=false should find 0 items
        var exactResults = engine.Search("qqs", new SearchOptions { Fuzzy = false });
        exactResults.Should().BeEmpty();

        // Fuzzy search for "qqs" should find "QuackQuackSearch.cs"
        var fuzzyResults = engine.Search("qqs", new SearchOptions { Fuzzy = true });
        fuzzyResults.Should().HaveCount(1);
        fuzzyResults[0].FileName.Should().Be("QuackQuackSearch.cs");

        // Fuzzy search for "reprot" should find "annual_report.pdf"
        var typoResults = engine.Search("reprot", new SearchOptions { Fuzzy = true });
        typoResults.Should().HaveCount(1);
        typoResults[0].FileName.Should().Be("annual_report.pdf");
    }
}
