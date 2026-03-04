using Total.Recall.Infrastructure;

namespace Total.Recall.Tests.Infrastructure;

public class AppVersionTests
{
    [Fact]
    public void Current_ReturnsSemverString()
    {
        var version = AppVersion.Current;

        Assert.NotNull(version);
        Assert.NotEmpty(version);
        // Must be semver-like: digits.digits.digits
        Assert.Matches(@"^\d+\.\d+\.\d+", version);
    }

    [Fact]
    public void Current_MatchesCsprojVersion()
    {
        // The csproj sets Version to 2.3.0
        Assert.Equal("2.3.0", AppVersion.Current);
    }

    [Fact]
    public void Current_IsCachedStaticProperty()
    {
        // Same reference every time (static readonly)
        var v1 = AppVersion.Current;
        var v2 = AppVersion.Current;
        Assert.Same(v1, v2);
    }

    [Fact]
    public void Current_DoesNotContainBuildMetadata()
    {
        // InformationalVersion may have +commitHash appended by SDK — we strip it
        Assert.DoesNotContain("+", AppVersion.Current);
    }
}
