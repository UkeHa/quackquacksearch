using FluentAssertions;
using QuackQuackSearch.Core.Index;
using QuackQuackSearch.Core.Monitoring;
using QuackQuackSearch.Core.Search;
using Xunit;

namespace QuackQuackSearch.Tests;

public class MonitoringTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        int elapsed = 0;
        while (!condition() && elapsed < timeoutMs)
        {
            await Task.Delay(15);
            elapsed += 15;
        }
    }

    [Fact]
    public async Task InotifyWatcher_ShouldDetectFileCreationsMovesAndRecursiveDeletions()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"qqs_inotify_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        using var engine = new SearchIndexEngine();
        using var watcher = new LocalInotifyWatcher(engine);

        try
        {
            watcher.AddWatchTree(tempDir);
            watcher.TotalActiveWatches.Should().BeGreaterThan(0);

            // 1. Create a file
            string testFile = Path.Combine(tempDir, "sample_doc.txt");
            await File.WriteAllTextAsync(testFile, "Hello World");

            await WaitUntilAsync(() => engine.Search("sample_doc", new SearchOptions { Fuzzy = false }).Count == 1);

            var matches = engine.Search("sample_doc", new SearchOptions { Fuzzy = false });
            matches.Should().HaveCount(1);
            matches[0].FileName.Should().Be("sample_doc.txt");

            // 2. Create a subdirectory dynamically and a file within it
            string subDir = Path.Combine(tempDir, "subfolder");
            Directory.CreateDirectory(subDir);
            await WaitUntilAsync(() => watcher.TotalActiveWatches >= 2);

            string nestedFile = Path.Combine(subDir, "nested_item.pdf");
            await File.WriteAllTextAsync(nestedFile, "Nested Content");
            await WaitUntilAsync(() => engine.Search("nested_item", new SearchOptions { Fuzzy = false }).Count == 1);

            var nestedMatches = engine.Search("nested_item", new SearchOptions { Fuzzy = false });
            nestedMatches.Should().HaveCount(1);
            nestedMatches[0].FileName.Should().Be("nested_item.pdf");

            // 3. Rename/Move the subdirectory and verify nested file path updates
            string movedSubDir = Path.Combine(tempDir, "renamed_folder");
            Directory.Move(subDir, movedSubDir);
            await WaitUntilAsync(() =>
            {
                var res = engine.Search("nested_item", new SearchOptions { Fuzzy = false });
                return res.Count == 1 && res[0].FullPath == Path.Combine(movedSubDir, "nested_item.pdf");
            });

            var movedMatches = engine.Search("nested_item", new SearchOptions { Fuzzy = false });
            movedMatches.Should().HaveCount(1);
            movedMatches[0].FullPath.Should().Be(Path.Combine(movedSubDir, "nested_item.pdf"));

            // 4. Delete the subdirectory recursively and verify nested files are purged from index
            Directory.Delete(movedSubDir, recursive: true);
            await WaitUntilAsync(() => engine.Search("nested_item", new SearchOptions { Fuzzy = false }).Count == 0);
            engine.Search("nested_item", new SearchOptions { Fuzzy = false }).Should().BeEmpty();

            // 5. Delete root test file
            File.Delete(testFile);
            await WaitUntilAsync(() => engine.Search("sample_doc", new SearchOptions { Fuzzy = false }).Count == 0);

            var deletedMatches = engine.Search("sample_doc", new SearchOptions { Fuzzy = false });
            deletedMatches.Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
