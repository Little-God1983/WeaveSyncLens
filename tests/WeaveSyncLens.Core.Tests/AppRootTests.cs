using Xunit;

namespace WeaveSyncLens.Core.Tests;

public class AppRootTests
{
    [Fact]
    public void Resolve_FromTestBinDirectory_FindsDirectoryContainingSolutionFile()
    {
        var root = AppRoot.Resolve();
        Assert.True(File.Exists(Path.Combine(root, "WeaveSyncLens.sln")));
    }
}
