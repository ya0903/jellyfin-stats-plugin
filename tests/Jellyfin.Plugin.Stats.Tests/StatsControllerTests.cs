using Jellyfin.Plugin.Stats.Api;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public class StatsControllerTests
{
    private static StatsController MakeController() =>
        new(null!, null!, null!, null!);

    [Fact]
    public void GetConfig_WhenNoPluginInstance_ReturnsHardcodedDefaults()
    {
        var controller = MakeController();
        var result = controller.GetConfig();
        Assert.Equal("Stats", result.PluginTitle);
        Assert.True(result.LeaderboardVisibleToAll);
    }
}
