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

    [Fact]
    public void CustomMatcher_SubfolderAndFileExclusion_ShouldIgnoreSpecificPaths()
    {
        string root = "/home/testuser";
        var excludes = new List<string>
        {
            "/home/testuser/secret",
            "/home/testuser/private/notes.txt"
        };

        var matcher = new IgnoreMatcher(
            globalPatterns: ["**/.git/**"],
            pathExcludes: new[] { (RootPath: root, Excludes: (IEnumerable<string>)excludes) }
        );

        // Subfolder exclusion
        matcher.ShouldIgnorePath("/home/testuser/secret", "secret", isDirectory: true).Should().BeTrue();
        matcher.ShouldIgnorePath("/home/testuser/secret/password.txt", "password.txt", isDirectory: false).Should().BeTrue();
        matcher.ShouldIgnorePath("/home/testuser/secret/sub/keys.pem", "keys.pem", isDirectory: false).Should().BeTrue();

        // Exact file exclusion
        matcher.ShouldIgnorePath("/home/testuser/private/notes.txt", "notes.txt", isDirectory: false).Should().BeTrue();
        matcher.ShouldIgnorePath("/home/testuser/private/other.txt", "other.txt", isDirectory: false).Should().BeFalse();

        // Normal files should not be ignored
        matcher.ShouldIgnorePath("/home/testuser/documents/file.txt", "file.txt", isDirectory: false).Should().BeFalse();
        matcher.ShouldIgnorePath("/home/testuser/secret_notes.txt", "secret_notes.txt", isDirectory: false).Should().BeFalse();
    }
}
