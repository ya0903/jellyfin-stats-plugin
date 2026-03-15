using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Stats.Api;

/// <summary>Stats API controller — all /Stats/* endpoints.</summary>
[ApiController]
[Route("Stats")]
public class StatsController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ILogger<StatsController> _logger;

    /// <summary>Initializes a new instance of <see cref="StatsController"/>.</summary>
    public StatsController(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        ILogger<StatsController> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _logger = logger;
    }

    /// <summary>Returns current plugin configuration for the frontend.</summary>
    [HttpGet("config")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public PluginConfigDto GetConfig()
    {
        var config = Plugin.Instance?.Configuration;
        return new PluginConfigDto(
            config?.PluginTitle ?? "Stats",
            config?.LeaderboardVisibleToAll ?? true);
    }

    /// <summary>
    /// Calculates actual watched ticks using PlayedPercentage.
    /// Treats null or zero as 100% — callers always pre-filter to IsPlayed = true,
    /// so a zero percentage means the item was manually marked watched with no tracking data.
    /// </summary>
    public static long CalculateWatchTimeTicks(long runTimeTicks, double? playedPercentage)
        => (long)(runTimeTicks * (playedPercentage is null or <= 0.0 ? 100.0 : playedPercentage.Value) / 100.0);

    /// <summary>Returns total watch stats for a user.</summary>
    [HttpGet("user/{userId}/summary")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<UserSummaryDto> GetSummary(Guid userId)
    {
        if (!IsAuthorized(userId)) return Forbid();

        var user = _userManager.GetUserById(userId);
        if (user is null) return NotFound();

        var items = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IsPlayed = true,
            Recursive = true,
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode],
        });

        long watchTimeTicks = 0;
        int movies = 0, episodes = 0;

        foreach (var item in items)
        {
            var ud = _userDataManager.GetUserDataDto(item, user);
            watchTimeTicks += CalculateWatchTimeTicks(
                item.RunTimeTicks ?? 0,
                ud?.PlayedPercentage);

            if (item is MediaBrowser.Controller.Entities.Movies.Movie) movies++;
            else episodes++;
        }

        var series = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            Recursive = true,
            IncludeItemTypes = [BaseItemKind.Series],
        });

        int started = 0, completed = 0;
        foreach (var s in series)
        {
            var ud = _userDataManager.GetUserDataDto(s, user);
            if ((ud?.PlayedPercentage ?? 0) > 0) started++;
            if (ud?.Played == true) completed++;
        }

        return Ok(new UserSummaryDto(movies, episodes, watchTimeTicks, started, completed));
    }

    private bool IsAuthorized(Guid targetUserId)
    {
        var requestUserId = GetRequestUserId();
        if (requestUserId == Guid.Empty) return false; // unauthenticated request
        if (requestUserId == targetUserId) return true;
        var requestUser = _userManager.GetUserById(requestUserId);
        return requestUser?.HasPermission(PermissionKind.IsAdministrator) == true;
    }

    private Guid GetRequestUserId()
    {
        // Jellyfin writes the authenticated user's ID to the "Jellyfin-UserId" claim.
        var claim = User.FindFirst("Jellyfin-UserId");
        return claim is not null && Guid.TryParse(claim.Value, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// Groups a list of dates into labelled buckets.
    /// Clamps to [today - range, today] — never produces future buckets.
    /// </summary>
    public static List<ActivityBucketDto> BucketDates(
        IEnumerable<DateTime> dates,
        string groupBy,
        DateTime today,
        int bucketCount)
    {
        // Build ordered bucket keys (oldest to newest, all pre-seeded at zero)
        var buckets = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int i = bucketCount - 1; i >= 0; i--)
        {
            string key = groupBy switch
            {
                "day"   => today.AddDays(-i).ToString("MMM d"),
                "week"  => $"W{GetIsoWeek(today.AddDays(-i * 7))} '{today.AddDays(-i * 7):yy}",
                "month" => today.AddMonths(-i).ToString("MMM yy"),
                "year"  => today.AddYears(-i).ToString("yyyy"),
                _       => today.AddMonths(-i).ToString("MMM yy"),
            };
            buckets.TryAdd(key, 0);
        }

        var cutoff = groupBy switch
        {
            "day"   => today.AddDays(-(bucketCount - 1)).Date,
            "week"  => today.AddDays(-(bucketCount - 1) * 7).Date,
            "month" => today.AddMonths(-(bucketCount - 1)).Date,
            "year"  => today.AddYears(-(bucketCount - 1)).Date,
            _       => today.AddMonths(-(bucketCount - 1)).Date,
        };

        foreach (var d in dates)
        {
            if (d.Date > today.Date || d.Date < cutoff) continue; // clamp — no future, no out-of-range

            string key = groupBy switch
            {
                "day"   => d.ToString("MMM d"),
                "week"  => $"W{GetIsoWeek(d)} '{d:yy}",
                "month" => d.ToString("MMM yy"),
                "year"  => d.ToString("yyyy"),
                _       => d.ToString("MMM yy"),
            };

            if (buckets.ContainsKey(key)) buckets[key]++;
        }

        return buckets.Select(kv => new ActivityBucketDto(kv.Key, kv.Value)).ToList();
    }

    private static int GetIsoWeek(DateTime d)
        => System.Globalization.ISOWeek.GetWeekOfYear(d);

    /// <summary>Returns watch activity grouped by day/week/month/year.</summary>
    [HttpGet("user/{userId}/activity")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<List<ActivityBucketDto>> GetActivity(
        Guid userId,
        [FromQuery] string groupBy = "month")
    {
        if (!IsAuthorized(userId)) return Forbid();
        var user = _userManager.GetUserById(userId);
        if (user is null) return NotFound();

        var items = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IsPlayed = true,
            Recursive = true,
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode],
        });

        var playedDates = items
            .Select(i => _userDataManager.GetUserDataDto(i, user)?.LastPlayedDate)
            .Where(d => d.HasValue)
            .Select(d => d!.Value.ToLocalTime())
            .ToList();

        int count = groupBy switch { "day" => 30, "week" => 52, "year" => 10, _ => 24 };

        return Ok(BucketDates(playedDates, groupBy, DateTime.Today, count));
    }

    /// <summary>Returns top genres by watched item count.</summary>
    [HttpGet("user/{userId}/genres")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<List<GenreDto>> GetGenres(Guid userId, [FromQuery] int limit = 6)
    {
        if (!IsAuthorized(userId)) return Forbid();
        var user = _userManager.GetUserById(userId);
        if (user is null) return NotFound();

        var items = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IsPlayed = true,
            Recursive = true,
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode],
        });

        var genreMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
            foreach (var genre in item.Genres ?? [])
                genreMap[genre] = genreMap.GetValueOrDefault(genre) + 1;

        return Ok(genreMap
            .OrderByDescending(kv => kv.Value)
            .Take(limit)
            .Select(kv => new GenreDto(kv.Key, kv.Value))
            .ToList());
    }

    /// <summary>Computes completion percentage, guarding against zero total.</summary>
    public static double ComputeCompletionPercent(int watched, int total)
        => total > 0 ? Math.Min(100.0, Math.Round(watched * 100.0 / total, 1)) : 0.0;

    /// <summary>Returns all started series with completion info.</summary>
    [HttpGet("user/{userId}/shows")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<List<ShowStatsDto>> GetShows(Guid userId)
    {
        if (!IsAuthorized(userId)) return Forbid();
        var user = _userManager.GetUserById(userId);
        if (user is null) return NotFound();

        var allSeries = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            Recursive = true,
            IncludeItemTypes = [BaseItemKind.Series],
        });

        // Fetch all episodes in two flat queries to avoid N+1 per series
        var allEpisodes = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            Recursive = true,
            IncludeItemTypes = [BaseItemKind.Episode],
        });

        var playedEpisodes = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            Recursive = true,
            IsPlayed = true,
            IncludeItemTypes = [BaseItemKind.Episode],
        });

        // Group by series ID in memory (cast to Episode to access SeriesId)
        var totalBySeriesId = allEpisodes
            .OfType<MediaBrowser.Controller.Entities.TV.Episode>()
            .GroupBy(e => e.SeriesId)
            .ToDictionary(g => g.Key, g => g.Count());
        var watchedBySeriesId = playedEpisodes
            .OfType<MediaBrowser.Controller.Entities.TV.Episode>()
            .GroupBy(e => e.SeriesId)
            .ToDictionary(g => g.Key, g => g.Count());

        var result = new List<ShowStatsDto>();
        foreach (var series in allSeries)
        {
            int watched = watchedBySeriesId.GetValueOrDefault(series.Id);
            if (watched == 0) continue; // not started

            int total = totalBySeriesId.GetValueOrDefault(series.Id);
            double pct = ComputeCompletionPercent(watched, total);
            bool completed = watched >= total && total > 0;

            result.Add(new ShowStatsDto(
                series.Name ?? string.Empty,
                watched,
                total,
                pct,
                completed));
        }

        return Ok(result.OrderByDescending(s => s.EpisodesWatched).ToList());
    }

    /// <summary>Returns recently played items.</summary>
    [HttpGet("user/{userId}/recent")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<List<RecentItemDto>> GetRecent(Guid userId, [FromQuery] int limit = 20)
    {
        if (!IsAuthorized(userId)) return Forbid();
        var user = _userManager.GetUserById(userId);
        if (user is null) return NotFound();

        var items = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IsPlayed = true,
            Recursive = true,
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode],
            OrderBy = [(Jellyfin.Data.Enums.ItemSortBy.DatePlayed, Jellyfin.Data.Enums.SortOrder.Descending)],
            Limit = limit,
        });

        return Ok(items
            .Select(i =>
            {
                var ud = _userDataManager.GetUserDataDto(i, user);
                string type = i is MediaBrowser.Controller.Entities.Movies.Movie ? "Movie" : "Episode";
                string? seriesName = i is MediaBrowser.Controller.Entities.TV.Episode ep
                    ? ep.SeriesName : null;
                return new RecentItemDto(
                    i.Name ?? string.Empty,
                    type,
                    seriesName,
                    ud?.LastPlayedDate);
            })
            .ToList());
    }

    /// <summary>Calculates binge statistics from a list of (date, durationHours) play events.</summary>
    public static BingeStatsDto CalculateBingeStats(
        IEnumerable<(DateTime date, double durationHours)> sessions)
    {
        var byDay = sessions
            .GroupBy(s => s.date.Date)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Hours: g.Sum(s => s.durationHours)));

        int longestBinge = byDay.Values.Select(v => v.Count).DefaultIfEmpty(0).Max();
        double longestSession = byDay.Values.Select(v => v.Hours).DefaultIfEmpty(0).Max();
        double avgSession = byDay.Count > 0 ? byDay.Values.Average(v => v.Hours) : 0;

        return new BingeStatsDto(
            longestBinge,
            Math.Round(longestSession, 1),
            Math.Round(avgSession, 2));
    }

    /// <summary>Returns binge statistics for a user.</summary>
    [HttpGet("user/{userId}/binge")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<BingeStatsDto> GetBinge(Guid userId)
    {
        if (!IsAuthorized(userId)) return Forbid();
        var user = _userManager.GetUserById(userId);
        if (user is null) return NotFound();

        var items = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IsPlayed = true,
            Recursive = true,
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode],
        });

        var sessions = items
            .Select(i =>
            {
                var ud = _userDataManager.GetUserDataDto(i, user);
                if (ud?.LastPlayedDate is null) return ((DateTime, double)?)null;
                double hours = CalculateWatchTimeTicks(
                    i.RunTimeTicks ?? 0,
                    ud.PlayedPercentage) / 36_000_000_000.0;
                return (ud.LastPlayedDate.Value.ToLocalTime(), hours);
            })
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToList();

        return Ok(CalculateBingeStats(sessions));
    }

    /// <summary>Returns top actors or directors by watched title count.</summary>
    [HttpGet("user/{userId}/people")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<List<PersonStatsDto>> GetPeople(
        Guid userId,
        [FromQuery] string type = "Actor",
        [FromQuery] int limit = 10)
    {
        if (!IsAuthorized(userId)) return Forbid();
        var user = _userManager.GetUserById(userId);
        if (user is null) return NotFound();

        var items = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IsPlayed = true,
            Recursive = true,
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode],
        });

        if (!Enum.TryParse<Jellyfin.Data.Enums.PersonKind>(type, ignoreCase: true, out var personKind))
            return BadRequest($"Invalid person type '{type}'. Valid values: {string.Join(", ", Enum.GetNames<Jellyfin.Data.Enums.PersonKind>())}");

        var personMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var people = _libraryManager.GetPeople(item);
            foreach (var p in people.Where(p => p.Type == personKind))
            {
                personMap[p.Name ?? string.Empty] = personMap.GetValueOrDefault(p.Name ?? string.Empty) + 1;
            }
        }

        return Ok(personMap
            .OrderByDescending(kv => kv.Value)
            .Take(limit)
            .Select(kv => new PersonStatsDto(kv.Key, kv.Value))
            .ToList());
    }

    /// <summary>Converts raw (name, ticks) pairs into a sorted leaderboard.</summary>
    public static List<LeaderboardEntryDto> RankLeaderboard(
        IEnumerable<(string name, long ticks)> entries)
        => entries
            .Select(e => new LeaderboardEntryDto(e.name, Math.Round(e.ticks / 36_000_000_000.0, 1)))
            .OrderByDescending(e => e.TotalHours)
            .ToList();

    /// <summary>
    /// Buckets a sequence of play-end timestamps into 24 hourly and 7 daily slots.
    /// Uses LastPlayedDate (when playback ended) as the signal — label accordingly in the UI.
    /// </summary>
    public static HeatmapDto CalculateHeatmap(IEnumerable<DateTime> dates)
    {
        static string HourLabel(int h) => h switch
        {
            0  => "12am",
            12 => "12pm",
            _  => h < 12 ? $"{h}am" : $"{h - 12}pm",
        };

        var hourCounts = new int[24];
        var dayCounts  = new int[7];

        foreach (var d in dates)
        {
            hourCounts[d.Hour]++;
            dayCounts[(int)d.DayOfWeek]++;
        }

        var hourly = Enumerable.Range(0, 24)
            .Select(i => new HourlyBucketDto(HourLabel(i), hourCounts[i]))
            .ToList();

        string[] dayLabels = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
        var daily = Enumerable.Range(0, 7)
            .Select(i => new DailyBucketDto(dayLabels[i], dayCounts[i]))
            .ToList();

        return new HeatmapDto(hourly, daily);
    }

    /// <summary>Returns hour-of-day and day-of-week finish-time distribution for a user.</summary>
    [HttpGet("user/{userId}/heatmap")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<HeatmapDto> GetHeatmap(Guid userId)
    {
        if (!IsAuthorized(userId)) return Forbid();
        var user = _userManager.GetUserById(userId);
        if (user is null) return NotFound();

        var items = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IsPlayed = true,
            Recursive = true,
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode],
        });

        var dates = items
            .Select(i => _userDataManager.GetUserDataDto(i, user)?.LastPlayedDate)
            .Where(d => d.HasValue)
            .Select(d => d!.Value.ToLocalTime())
            .ToList();

        return Ok(CalculateHeatmap(dates));
    }

    /// <summary>
    /// Groups a sequence of production years into decade buckets.
    /// Only decades with at least one title are returned, ordered chronologically.
    /// </summary>
    public static List<DecadeBucketDto> BucketByDecade(IEnumerable<int> years)
        => years
            .GroupBy(y => y / 10 * 10)
            .OrderBy(g => g.Key)
            .Select(g => new DecadeBucketDto($"{g.Key}s", g.Count()))
            .ToList();

    /// <summary>Returns movies grouped by production decade.</summary>
    [HttpGet("user/{userId}/decades")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<List<DecadeBucketDto>> GetDecades(Guid userId)
    {
        if (!IsAuthorized(userId)) return Forbid();
        var user = _userManager.GetUserById(userId);
        if (user is null) return NotFound();

        var items = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IsPlayed = true,
            Recursive = true,
            IncludeItemTypes = [BaseItemKind.Movie],
        });

        var years = items
            .Where(i => i.ProductionYear.HasValue)
            .Select(i => i.ProductionYear!.Value)
            .ToList();

        return Ok(BucketByDecade(years));
    }

    /// <summary>Returns all users ranked by total watch time.</summary>
    [HttpGet("leaderboard")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<List<LeaderboardEntryDto>> GetLeaderboard()
    {
        var config = Plugin.Instance?.Configuration;
        if (config?.LeaderboardVisibleToAll == false)
        {
            var requestUser = _userManager.GetUserById(GetRequestUserId());
            if (requestUser?.HasPermission(PermissionKind.IsAdministrator) != true)
                return Forbid();
        }
        var users = _userManager.Users.ToList();
        var raw = new List<(string name, long ticks)>();

        foreach (var user in users)
        {
            var items = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IsPlayed = true,
                Recursive = true,
                IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode],
            });

            long ticks = items.Sum(i =>
            {
                var ud = _userDataManager.GetUserDataDto(i, user);
                return CalculateWatchTimeTicks(i.RunTimeTicks ?? 0, ud?.PlayedPercentage);
            });

            raw.Add((user.Username, ticks));
        }

        return Ok(RankLeaderboard(raw));
    }
}
