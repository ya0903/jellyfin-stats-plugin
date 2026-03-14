using Xunit;
using Jellyfin.Plugin.Stats;

namespace Jellyfin.Plugin.Stats.Tests;

public class PluginTests
{
    [Fact]
    public void PluginConfiguration_DefaultTitle_IsStats()
    {
        var config = new PluginConfiguration();
        Assert.Equal("Stats", config.PluginTitle);
    }

    [Fact]
    public void PluginConfiguration_DefaultLeaderboard_IsTrue()
    {
        var config = new PluginConfiguration();
        Assert.True(config.LeaderboardVisibleToAll);
    }
}
