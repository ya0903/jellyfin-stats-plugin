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
        // Plugin.Instance is always null in unit tests (no Jellyfin host running).
        // This test verifies the fallback path: ?? "Stats" and ?? true.
        // The happy path (real config values) is covered by the smoke test in Task 15.
        var controller = MakeController();
        var result = controller.GetConfig();
        Assert.Equal("Stats", result.PluginTitle);
        Assert.True(result.LeaderboardVisibleToAll);
    }
}
