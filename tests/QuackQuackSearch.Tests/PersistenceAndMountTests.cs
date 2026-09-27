using FluentAssertions;
using QuackQuackSearch.Core.Crawler;
using QuackQuackSearch.Core.Index;
using QuackQuackSearch.Core.Storage;
using QuackQuackSearch.Core.System;
using Xunit;

namespace QuackQuackSearch.Tests;

public class PersistenceAndMountTests
{
    [Fact]
    public async Task IndexSerializer_Roundtrip_ShouldPreserveAllEntries()
    {
        var originalEngine = new SearchIndexEngine();
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var items = new List<DiscoveredItem>
        {
            new("/home/user", "file1.txt", 100, now, false, false),
            new("/home/user/sub", "file2.txt", 200, now, false, false)
        };
        originalEngine.BulkAdd(items);

        string tempPath = Path.Combine(Path.GetTempPath(), $"test_index_{Guid.NewGuid():N}.bin");

        try
        {
            await IndexSerializer.SaveAsync(originalEngine, tempPath);
            File.Exists(tempPath).Should().BeTrue();

            var loadedEngine = new SearchIndexEngine();
            bool loaded = await IndexSerializer.TryLoadAsync(loadedEngine, tempPath);

            loaded.Should().BeTrue();
            loadedEngine.TotalFiles.Should().Be(2);

            var results = loadedEngine.Search("file2");
            results.Should().HaveCount(1);
            results[0].FullPath.Should().Be("/home/user/sub/file2.txt");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void MountScanner_ShouldScanRealSystemMounts()
    {
        var mounts = MountScanner.ScanMounts();
        mounts.Should().NotBeNull();
        mounts.Should().NotBeEmpty();

        // System should have at least root "/" or "/home"
        mounts.Should().Contain(m => m.MountPoint == "/" || m.MountPoint == "/home");
    }
}
