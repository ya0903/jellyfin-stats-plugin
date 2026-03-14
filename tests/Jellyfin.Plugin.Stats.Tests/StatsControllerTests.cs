using System;
using System.Collections.Generic;
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

    [Fact]
    public void BucketByMonth_NoFutureBuckets()
    {
        var today = new DateTime(2026, 3, 14);
        var dates = new List<DateTime>
        {
            new(2026, 3, 10),  // this month — should appear
            new(2026, 4, 1),   // future — must NOT appear
            new(2025, 12, 25), // past — should appear
        };

        var buckets = StatsController.BucketDates(dates, "month", today, 12);

        Assert.DoesNotContain(buckets, b => b.Label == "Apr" && b.Count > 0);
        Assert.Contains(buckets, b => b.Label == "Mar" && b.Count == 1);
        Assert.Contains(buckets, b => b.Label == "Dec" && b.Count == 1);
    }

    [Fact]
    public void BucketByDay_Returns30Buckets()
    {
        var today = new DateTime(2026, 3, 14);
        var buckets = StatsController.BucketDates([], "day", today, 30);
        Assert.Equal(30, buckets.Count);
    }

    [Fact]
    public void BucketByYear_IncludesCurrentYear()
    {
        var today = new DateTime(2026, 3, 14);
        var dates = new List<DateTime> { new(2026, 1, 5), new(2024, 6, 10) };
        var buckets = StatsController.BucketDates(dates, "year", today, 5);
        Assert.Contains(buckets, b => b.Label == "2026" && b.Count == 1);
        Assert.Contains(buckets, b => b.Label == "2024" && b.Count == 1);
    }
}
