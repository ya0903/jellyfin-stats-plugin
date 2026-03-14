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

    /// <summary>Calculates actual watched ticks using PlayedPercentage.</summary>
    public static long CalculateWatchTimeTicks(long runTimeTicks, double? playedPercentage)
        => (long)(runTimeTicks * (playedPercentage ?? 100.0) / 100.0);

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
        => total > 0 ? Math.Round(watched * 100.0 / total, 1) : 0.0;

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

        var result = new List<ShowStatsDto>();
        foreach (var series in allSeries)
        {
            var ud = _userDataManager.GetUserDataDto(series, user);
            if ((ud?.PlayedPercentage ?? 0) <= 0) continue; // not started

            // Count episodes
            var allEps = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                Recursive = true,
                IncludeItemTypes = [BaseItemKind.Episode],
                AncestorIds = [series.Id],
            });

            int total = allEps.Count;
            int watched = allEps.Count(e =>
                _userDataManager.GetUserDataDto(e, user)?.Played == true);

            double pct = ComputeCompletionPercent(watched, total);
            result.Add(new ShowStatsDto(
                series.Name ?? string.Empty,
                watched,
                total,
                pct,
                ud?.Played == true));
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
            .Where(dto => dto.LastPlayedDate.HasValue)
            .OrderByDescending(dto => dto.LastPlayedDate)
            .Take(limit)
            .ToList());
    }

    // Remaining endpoints added in Tasks 9-10
}
