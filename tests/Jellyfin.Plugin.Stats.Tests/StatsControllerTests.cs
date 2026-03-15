using System;
using System.Collections.Generic;
using System.Linq;
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
    public void CalculateWatchTimeTicks_ZeroPercentageCountsFullItem()
    {
        // Items manually marked as watched have PlayedPercentage = 0.0 with no tracking data.
        // They should contribute full runtime, not zero.
        long runTimeTicks = 36_000_000_000L;
        long result = StatsController.CalculateWatchTimeTicks(runTimeTicks, 0.0);
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

        Assert.DoesNotContain(buckets, b => b.Label.StartsWith("Apr") && b.Count > 0);
        Assert.Contains(buckets, b => b.Label.StartsWith("Mar") && b.Count == 1);
        Assert.Contains(buckets, b => b.Label.StartsWith("Dec") && b.Count == 1);
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

    [Fact]
    public void BucketByMonth_NoLabelCollisionAcrossYears()
    {
        var today = new DateTime(2026, 3, 14);
        var dates = new List<DateTime>
        {
            new(2025, 3, 10), // Mar 25 — same month name as current year but different year
            new(2026, 3, 5),  // Mar 26 — current month
        };

        var buckets = StatsController.BucketDates(dates, "month", today, 24);

        // Both Marches must be counted — total should be 2 across the two Mar buckets
        int marTotal = buckets.Where(b => b.Label.StartsWith("Mar")).Sum(b => b.Count);
        Assert.Equal(2, marTotal);
        // Must have 24 distinct buckets
        Assert.Equal(24, buckets.Count);
    }

    [Fact]
    public void AggregateGenres_CountsAndRanksCorrectly()
    {
        var items = new[]
        {
            new { Genres = new[] { "Action", "Drama" } },
            new { Genres = new[] { "Action" } },
            new { Genres = new[] { "Drama", "Comedy" } },
        };

        var genreMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
            foreach (var g in item.Genres)
                genreMap[g] = genreMap.GetValueOrDefault(g) + 1;

        var ranked = genreMap.OrderByDescending(kv => kv.Value).Take(6)
            .Select(kv => new GenreDto(kv.Key, kv.Value)).ToList();

        Assert.Equal("Action", ranked[0].Name);
        Assert.Equal(2, ranked[0].Count);
        Assert.Equal("Drama", ranked[1].Name);
        Assert.Equal(2, ranked[1].Count);
        Assert.Equal(3, ranked.Count);
    }

    [Fact]
    public void AggregateGenres_CaseInsensitiveDeduplicated()
    {
        var genreMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in new[] { "action", "Action", "ACTION" })
            genreMap[g] = genreMap.GetValueOrDefault(g) + 1;

        Assert.Single(genreMap);
        Assert.Equal(3, genreMap.Values.First());
    }

    [Fact]
    public void ComputeCompletionPercent_FullyWatched_Returns100()
    {
        double result = StatsController.ComputeCompletionPercent(watched: 10, total: 10);
        Assert.Equal(100.0, result);
    }

    [Fact]
    public void ComputeCompletionPercent_HalfWatched_Returns50()
    {
        double result = StatsController.ComputeCompletionPercent(watched: 5, total: 10);
        Assert.Equal(50.0, result);
    }

    [Fact]
    public void ComputeCompletionPercent_ZeroTotal_ReturnsZero()
    {
        double result = StatsController.ComputeCompletionPercent(watched: 0, total: 0);
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void ComputeCompletionPercent_WatchedExceedsTotal_ClampsTo100()
    {
        // Can happen if episode count shifts between queries
        double result = StatsController.ComputeCompletionPercent(watched: 12, total: 10);
        Assert.Equal(100.0, result);
    }

    [Fact]
    public void CalculateBinge_LongestStreakInOneDay()
    {
        var today = DateTime.Today;
        // 5 episodes on day 1, 3 on day 2
        var sessions = new List<(DateTime date, double durationHours)>
        {
            (today.AddDays(-2), 0.5),
            (today.AddDays(-2), 0.5),
            (today.AddDays(-2), 0.5),
            (today.AddDays(-2), 0.5),
            (today.AddDays(-2), 0.5),
            (today.AddDays(-1), 0.5),
            (today.AddDays(-1), 0.5),
            (today.AddDays(-1), 0.5),
        };

        var result = StatsController.CalculateBingeStats(sessions);

        Assert.Equal(5, result.LongestBingeEpisodes);
    }

    [Fact]
    public void CalculateBinge_AverageSession_IgnoresZeroDays()
    {
        var today = DateTime.Today;
        var sessions = new List<(DateTime date, double durationHours)>
        {
            (today.AddDays(-1), 2.0),
            (today.AddDays(-1), 1.0), // day 1: 3 hours
            (today.AddDays(-3), 1.5), // day 2: 1.5 hours
        };

        var result = StatsController.CalculateBingeStats(sessions);
        // average of 3.0 and 1.5 = 2.25
        Assert.Equal(2.25, result.AverageSessionHours);
    }

    [Fact]
    public void CalculateBingeStats_EmptyInput_ReturnsZeros()
    {
        var result = StatsController.CalculateBingeStats([]);
        Assert.Equal(0, result.LongestBingeEpisodes);
        Assert.Equal(0.0, result.LongestSessionHours);
        Assert.Equal(0.0, result.AverageSessionHours);
    }

    // ── Heatmap ────────────────────────────────────────────────────────────────

    [Fact]
    public void CalculateHeatmap_CountsByHourAndDay()
    {
        var dates = new List<DateTime>
        {
            new(2026, 3, 13, 21, 0, 0), // Friday  21:00 → index 21 / Fri
            new(2026, 3, 13, 21, 30, 0),// Friday  21:30 → index 21 / Fri
            new(2026, 3, 14, 14, 0, 0), // Saturday 14:00 → index 14 / Sat
        };

        var result = StatsController.CalculateHeatmap(dates);

        Assert.Equal(24, result.HourlyBuckets.Count);
        Assert.Equal(7,  result.DailyBuckets.Count);
        Assert.Equal(2, result.HourlyBuckets[21].Count);
        Assert.Equal(1, result.HourlyBuckets[14].Count);
        Assert.Equal(2, result.DailyBuckets.First(b => b.Label == "Fri").Count);
        Assert.Equal(1, result.DailyBuckets.First(b => b.Label == "Sat").Count);
    }

    [Fact]
    public void CalculateHeatmap_EmptyInput_ReturnsAllZeros()
    {
        var result = StatsController.CalculateHeatmap([]);
        Assert.Equal(24, result.HourlyBuckets.Count);
        Assert.Equal(7,  result.DailyBuckets.Count);
        Assert.All(result.HourlyBuckets, b => Assert.Equal(0, b.Count));
        Assert.All(result.DailyBuckets,  b => Assert.Equal(0, b.Count));
    }

    [Fact]
    public void CalculateHeatmap_HourLabels_Correct()
    {
        var result = StatsController.CalculateHeatmap([]);
        Assert.Equal("12am", result.HourlyBuckets[0].Label);
        Assert.Equal("1am",  result.HourlyBuckets[1].Label);
        Assert.Equal("11am", result.HourlyBuckets[11].Label);
        Assert.Equal("12pm", result.HourlyBuckets[12].Label);
        Assert.Equal("1pm",  result.HourlyBuckets[13].Label);
        Assert.Equal("11pm", result.HourlyBuckets[23].Label);
    }

    [Fact]
    public void CalculateHeatmap_DayLabels_SundayFirst()
    {
        var result = StatsController.CalculateHeatmap([]);
        Assert.Equal("Sun", result.DailyBuckets[0].Label);
        Assert.Equal("Sat", result.DailyBuckets[6].Label);
    }

    // ── Decade bucketing ───────────────────────────────────────────────────────

    [Fact]
    public void BucketByDecade_GroupsCorrectly()
    {
        var years = new[] { 1994, 1995, 1999, 2001, 2010, 2023 };
        var result = StatsController.BucketByDecade(years);

        Assert.Equal(4, result.Count);
        Assert.Equal(3, result.First(d => d.Label == "1990s").Count);
        Assert.Equal(1, result.First(d => d.Label == "2000s").Count);
        Assert.Equal(1, result.First(d => d.Label == "2010s").Count);
        Assert.Equal(1, result.First(d => d.Label == "2020s").Count);
    }

    [Fact]
    public void BucketByDecade_OrderedChronologically()
    {
        var years = new[] { 2010, 1985, 1995 };
        var result = StatsController.BucketByDecade(years);
        Assert.Equal("1980s", result[0].Label);
        Assert.Equal("1990s", result[1].Label);
        Assert.Equal("2010s", result[2].Label);
    }

    [Fact]
    public void BucketByDecade_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(StatsController.BucketByDecade([]));
    }

    [Fact]
    public void BucketByDecade_SkipsEmptyDecades()
    {
        var years = new[] { 1985, 2023 }; // gap: no 1990s–2010s
        var result = StatsController.BucketByDecade(years);
        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, d => d.Label == "1990s");
        Assert.DoesNotContain(result, d => d.Label == "2000s");
    }

    [Fact]
    public void RankLeaderboard_SortsByHoursDescending()
    {
        var raw = new List<(string name, long ticks)>
        {
            ("Alice", 36_000_000_000L * 10),  // 10 hrs
            ("Bob",   36_000_000_000L * 50),  // 50 hrs
            ("Carol", 36_000_000_000L * 25),  // 25 hrs
        };

        var ranked = StatsController.RankLeaderboard(raw);

        Assert.Equal("Bob",   ranked[0].UserName);
        Assert.Equal(50.0,    ranked[0].TotalHours);
        Assert.Equal("Carol", ranked[1].UserName);
        Assert.Equal("Alice", ranked[2].UserName);
    }
}
