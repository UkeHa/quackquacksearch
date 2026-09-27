using FluentAssertions;
using QuackQuackSearch.Core.Crawler;
using QuackQuackSearch.Core.Index;
using QuackQuackSearch.Core.Search;
using Xunit;

namespace QuackQuackSearch.Tests;

public class SearchIndexEngineTests
{
    [Fact]
    public void BulkAddAndSearch_ShouldReturnMatchesRanked()
    {
        var engine = new SearchIndexEngine();
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var items = new List<DiscoveredItem>
        {
            new("/home/user/docs", "report.pdf", 1024, now, false, false),
            new("/home/user/docs", "annual_report_final.pdf", 2048, now, false, false),
            new("/home/user/code", "report_generator.cs", 4096, now, false, false),
            new("/home/user/pictures", "holiday.jpg", 8192, now, false, false)
        };

        engine.BulkAdd(items);

        var results = engine.Search("report", new SearchOptions());

        results.Should().HaveCount(3);
        // "report.pdf" starts with "report" and has shortest name, should be first
        results[0].FileName.Should().Be("report.pdf");
        results[0].FullPath.Should().Be("/home/user/docs/report.pdf");
    }

    [Fact]
    public void IncrementalUpdateAndRemove_ShouldUpdateIndexCorrectly()
    {
        var engine = new SearchIndexEngine();
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        engine.AddOrUpdate("/home/user/test.txt", isDirectory: false, size: 100, modifiedUnixSec: now, isHidden: false);
        engine.TotalFiles.Should().Be(1);

        var found = engine.Search("test");
        found.Should().HaveCount(1);
        found[0].FullPath.Should().Be("/home/user/test.txt");

        bool removed = engine.Remove("/home/user/test.txt");
        removed.Should().BeTrue();
        engine.TotalFiles.Should().Be(0);

        var notFound = engine.Search("test");
        notFound.Should().BeEmpty();
    }

    [Fact]
    public void RemoveSubtree_ShouldPurgeAllNestedFilesAndDirectories()
    {
        var engine = new SearchIndexEngine();
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var items = new List<DiscoveredItem>
        {
            new("/home/user/Projects", "root_project.txt", 100, now, false, false),
            new("/home/user/Projects/SubA", "fileA.cs", 200, now, false, false),
            new("/home/user/Projects/SubA/Deep", "deep_file.cs", 300, now, false, false),
            new("/home/user/Other", "keep_me.txt", 400, now, false, false)
        };
        engine.BulkAdd(items);

        engine.TotalFiles.Should().Be(4);

        int removed = engine.RemoveSubtree("/home/user/Projects");
        removed.Should().Be(3);
        engine.TotalFiles.Should().Be(1);

        engine.Search("cs").Should().BeEmpty();
        var remaining = engine.Search("keep_me");
        remaining.Should().HaveCount(1);
        remaining[0].FileName.Should().Be("keep_me.txt");
    }
}
