using FluentAssertions;
using QuackQuackSearch.Core.Index;
using Xunit;

namespace QuackQuackSearch.Tests;

public class DirectoryTableTests
{
    [Fact]
    public void RootDirectory_ShouldHaveValidId()
    {
        var table = new DirectoryTable();
        uint rootId = table.GetOrAddDirectory("/");

        rootId.Should().BeGreaterThan(0u);
        table.ResolveFullPath(rootId).Should().Be("/");
    }

    [Fact]
    public void NestedPath_ShouldResolveCorrectly()
    {
        var table = new DirectoryTable();
        string samplePath = "/home/user/Code/quackquacksearch/src";

        uint dirId = table.GetOrAddDirectory(samplePath);
        string resolved = table.ResolveFullPath(dirId);

        resolved.Should().Be(samplePath);
    }

    [Fact]
    public void RepeatedDirectories_ShouldShareIds()
    {
        var table = new DirectoryTable();
        uint id1 = table.GetOrAddDirectory("/home/user/Projects/AppA");
        uint id2 = table.GetOrAddDirectory("/home/user/Projects/AppB");

        uint parent1 = table.GetOrAddDirectory("/home/user/Projects");

        id1.Should().NotBe(id2);
        table.ResolveFullPath(parent1).Should().Be("/home/user/Projects");
        table.ResolveFullPath(id1).Should().Be("/home/user/Projects/AppA");
        table.ResolveFullPath(id2).Should().Be("/home/user/Projects/AppB");
    }

    [Fact]
    public void DeepHierarchy_Over64Levels_ShouldResolveSubtreeAndScoresWithoutTruncation()
    {
        using var table = new DirectoryTable();
        string currentPath = "/root_deep";
        for (int i = 1; i <= 100; i++)
        {
            currentPath += $"/level_{i}";
        }

        uint leafId = table.GetOrAddDirectory(currentPath);
        table.ResolveFullPath(leafId).Should().Be(currentPath);

        var subtreeIds = table.GetDirectoryIdsInSubtree("/root_deep");
        subtreeIds.Should().Contain(leafId);
        subtreeIds.Count.Should().Be(101); // /root_deep + 100 nested levels

        var matchingScores = table.FindMatchingDirectoryScores("root_deep", hasWildcards: false);
        matchingScores.Should().ContainKey(leafId);
    }
}
