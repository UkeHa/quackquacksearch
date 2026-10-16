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

    [Fact]
    public void BulkAdd_RepeatedItems_ShouldUpdateInPlaceWithoutDuplicates()
    {
        using var engine = new SearchIndexEngine();
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var batch1 = new List<DiscoveredItem>
        {
            new("/home/user/docs", "notes.txt", 100, now - 60, false, false),
            new("/home/user/docs", "todo.txt", 200, now - 60, false, false)
        };
        engine.BulkAdd(batch1);
        engine.TotalFiles.Should().Be(2);

        // Re-adding with updated size and timestamp should not duplicate entries
        var batch2 = new List<DiscoveredItem>
        {
            new("/home/user/docs", "notes.txt", 555, now, false, false),
            new("/home/user/docs", "todo.txt", 200, now, false, false)
        };
        engine.BulkAdd(batch2);
        engine.TotalFiles.Should().Be(2);

        var results = engine.Search("notes", new SearchOptions { Fuzzy = false });
        results.Should().HaveCount(1);
        results[0].Size.Should().Be(555);
    }

    [Fact]
    public void ReplaceSubtree_ShouldRemoveDeletedFilesAndKeepOtherSubtrees()
    {
        using var engine = new SearchIndexEngine();
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        engine.BulkAdd(new List<DiscoveredItem>
        {
            new("/mnt/nas/share", "old_file.txt", 100, now, false, false),
            new("/mnt/nas/share/sub", "keep_updated.txt", 200, now, false, false),
            new("/home/user", "local.txt", 300, now, false, false)
        });

        engine.ReplaceSubtree("/mnt/nas/share", new List<DiscoveredItem>
        {
            new("/mnt/nas/share/sub", "keep_updated.txt", 999, now, false, false),
            new("/mnt/nas/share", "brand_new.txt", 400, now, false, false)
        });

        engine.TotalFiles.Should().Be(3);
        engine.Search("old_file", new SearchOptions { Fuzzy = false }).Should().BeEmpty();
        engine.Search("brand_new", new SearchOptions { Fuzzy = false }).Should().HaveCount(1);
        engine.Search("local", new SearchOptions { Fuzzy = false }).Should().HaveCount(1);
        engine.Search("keep_updated", new SearchOptions { Fuzzy = false })[0].Size.Should().Be(999);
    }

    [Fact]
    public void GetRecentEntries_ShouldReturnMostRecentItemsAndSearchInPath_ShouldMatchDirectory()
    {
        using var engine = new SearchIndexEngine();
        uint baseTime = 1_700_000_000;

        engine.BulkAdd(new List<DiscoveredItem>
        {
            new("/home/user/alpha_folder", "first.txt", 10, baseTime + 10, false, false),
            new("/home/user/beta_folder", "second.txt", 20, baseTime + 50, false, false),
            new("/home/user/gamma_folder", "third.txt", 30, baseTime + 30, false, false)
        });

        var recent = engine.GetRecentEntries(2);
        recent.Should().HaveCount(2);
        recent[0].FileName.Should().Be("second.txt");
        recent[1].FileName.Should().Be("third.txt");

        // SearchInPath test
        engine.Search("beta_folder", new SearchOptions { Fuzzy = false, SearchInPath = false }).Should().BeEmpty();
        var pathMatches = engine.Search("beta_folder", new SearchOptions { Fuzzy = false, SearchInPath = true });
        pathMatches.Should().HaveCount(1);
        pathMatches[0].FileName.Should().Be("second.txt");

        // Removing non-existent path should not add directories to DirectoryTable
        int dirsBefore = engine.TotalDirectories;
        engine.Remove("/non/existent/deep/path/file.txt").Should().BeFalse();
        engine.TotalDirectories.Should().Be(dirsBefore);
    }
}
