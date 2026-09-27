using FluentAssertions;
using QuackQuackSearch.Core.Index;
using QuackQuackSearch.Core.Monitoring;
using Xunit;

namespace QuackQuackSearch.Tests;

public class MonitoringTests
{
    [Fact]
    public async Task InotifyWatcher_ShouldDetectFileCreationsAndDeletions()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"qqs_inotify_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var engine = new SearchIndexEngine();
        using var watcher = new LocalInotifyWatcher(engine);

        try
        {
            watcher.AddWatchTree(tempDir);
            watcher.TotalActiveWatches.Should().BeGreaterThan(0);

            // 1. Create a file
            string testFile = Path.Combine(tempDir, "sample_doc.txt");
            await File.WriteAllTextAsync(testFile, "Hello World");

            // Allow inotify event to propagate
            await Task.Delay(150);

            var matches = engine.Search("sample_doc");
            matches.Should().HaveCount(1);
            matches[0].FileName.Should().Be("sample_doc.txt");

            // 2. Create a subdirectory dynamically and a file within it
            string subDir = Path.Combine(tempDir, "subfolder");
            Directory.CreateDirectory(subDir);
            await Task.Delay(100);

            string nestedFile = Path.Combine(subDir, "nested_item.pdf");
            await File.WriteAllTextAsync(nestedFile, "Nested Content");
            await Task.Delay(150);

            var nestedMatches = engine.Search("nested_item");
            nestedMatches.Should().HaveCount(1);
            nestedMatches[0].FileName.Should().Be("nested_item.pdf");

            // 3. Delete file
            File.Delete(testFile);
            await Task.Delay(150);

            var deletedMatches = engine.Search("sample_doc");
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
