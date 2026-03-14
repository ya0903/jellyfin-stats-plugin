namespace Jellyfin.Plugin.Stats.Api;

/// <summary>Summary stats for a user.</summary>
public record UserSummaryDto(
    int MoviesWatched,
    int EpisodesWatched,
    long TotalWatchTimeTicks,
    int ShowsStarted,
    int ShowsCompleted);

/// <summary>A single activity bucket (day/week/month/year).</summary>
public record ActivityBucketDto(string Label, int Count);

/// <summary>A genre with its item count.</summary>
public record GenreDto(string Name, int Count);

/// <summary>Stats for a single series.</summary>
public record ShowStatsDto(
    string Name,
    int EpisodesWatched,
    int TotalEpisodes,
    double CompletionPercent,
    bool Completed);

/// <summary>A person (actor or director) with title count.</summary>
public record PersonStatsDto(string Name, int TitleCount);

/// <summary>Binge statistics.</summary>
public record BingeStatsDto(
    int LongestBingeEpisodes,
    double LongestSessionHours,
    double AverageSessionHours);

/// <summary>A recently played item.</summary>
public record RecentItemDto(
    string Name,
    string Type,
    string? SeriesName,
    DateTime? LastPlayedDate);

/// <summary>A leaderboard entry.</summary>
public record LeaderboardEntryDto(string UserName, double TotalHours);

/// <summary>Plugin config exposed to the frontend.</summary>
public record PluginConfigDto(string PluginTitle, bool LeaderboardVisibleToAll);
