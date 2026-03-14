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

    [Fact]
    public void CalculateWatchTimeTicks_UsesPlayedPercentage()
    {
        // 1 hour item, 50% played = 30 minutes in ticks
        long runTimeTicks = 36_000_000_000L; // 1 hour
        double playedPct = 50.0;
        long expected = 18_000_000_000L; // 30 minutes

        long result = StatsController.CalculateWatchTimeTicks(runTimeTicks, playedPct);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void CalculateWatchTimeTicks_NullPercentageCountsFullItem()
    {
        long runTimeTicks = 36_000_000_000L;
        long result = StatsController.CalculateWatchTimeTicks(runTimeTicks, null);
        Assert.Equal(runTimeTicks, result);
    }
}
