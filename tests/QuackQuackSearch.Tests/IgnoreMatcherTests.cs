using FluentAssertions;
using QuackQuackSearch.Core.Crawler;
using Xunit;

namespace QuackQuackSearch.Tests;

public class IgnoreMatcherTests
{
    [Theory]
    [InlineData(".git", true)]
    [InlineData("node_modules", true)]
    [InlineData("bin", true)]
    [InlineData("obj", true)]
    [InlineData(".cache", true)]
    [InlineData("src", false)]
    [InlineData("components", false)]
    public void DefaultMatcher_ShouldIdentifyIgnoredDirectoryNames(string dirName, bool shouldIgnore)
    {
        var matcher = IgnoreMatcher.Default;
        matcher.ShouldIgnoreDirectoryName(dirName).Should().Be(shouldIgnore);
    }

    [Theory]
    [InlineData("/home/user/project/.git/HEAD", "HEAD", false, true)]
    [InlineData("/home/user/project/node_modules/pkg/index.js", "index.js", false, true)]
    [InlineData("/home/user/file.tmp", "file.tmp", false, true)]
    [InlineData("/home/user/project/src/index.ts", "index.ts", false, false)]
    public void DefaultMatcher_ShouldIdentifyIgnoredPaths(string fullPath, string fileName, bool isDir, bool shouldIgnore)
    {
        var matcher = IgnoreMatcher.Default;
        matcher.ShouldIgnorePath(fullPath, fileName, isDir).Should().Be(shouldIgnore);
    }
}
