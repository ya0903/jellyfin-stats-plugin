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
