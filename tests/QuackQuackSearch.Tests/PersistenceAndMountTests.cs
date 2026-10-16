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

        // System (or container/sandbox) should have at least root "/", "/home*", or "/etc"
        mounts.Should().Contain(m => m.MountPoint == "/" || m.MountPoint.StartsWith("/home") || m.MountPoint == "/etc");
    }

    [Fact]
    public void MountScanner_ShouldHandleEmptyDeviceAndOctalEscapes()
    {
        string tempMounts = Path.Combine(Path.GetTempPath(), $"test_mounts_{Guid.NewGuid():N}");
        try
        {
            File.WriteAllLines(tempMounts, [
                " / tmpfs ro,relatime 0 0",
                "/dev/nvme0n1p2 /home/user/My\\040Documents btrfs rw,relatime 0 0",
                "//192.168.1.10/share /mnt/nas cifs rw 0 0"
            ]);

            var mounts = MountScanner.ScanMounts(tempMounts);
            // The tmpfs root mount with empty device must be ignored rather than shifting columns
            mounts.Should().NotContain(m => m.MountPoint == "tmpfs");
            mounts.Should().Contain(m => m.MountPoint == "/home/user/My Documents" && m.Type == MountType.Local);
            mounts.Should().Contain(m => m.MountPoint == "/mnt/nas" && m.Type == MountType.Network);
        }
        finally
        {
            if (File.Exists(tempMounts)) File.Delete(tempMounts);
        }
    }

    [Fact]
    public void XdgDirectories_ShouldProvideValidNonEmptyPaths()
    {
        XdgDirectories.ConfigHome.Should().NotBeNullOrWhiteSpace();
        XdgDirectories.DataHome.Should().NotBeNullOrWhiteSpace();
        XdgDirectories.CacheHome.Should().NotBeNullOrWhiteSpace();
        XdgDirectories.RuntimeDir.Should().NotBeNullOrWhiteSpace();

        XdgDirectories.AppConfigDir.Should().EndWith("quackquacksearch");
        XdgDirectories.AppCacheDir.Should().EndWith("quackquacksearch");
        XdgDirectories.AppDataDir.Should().EndWith("quackquacksearch");
        XdgDirectories.KRunnerPluginsDir.Should().EndWith(Path.Combine("krunner", "dbusplugins"));
        XdgDirectories.SystemdUserDir.Should().EndWith(Path.Combine("systemd", "user"));

        string socket = XdgDirectories.GetRuntimeSocketPath("test.sock");
        socket.Should().EndWith("test.sock");
    }
}
