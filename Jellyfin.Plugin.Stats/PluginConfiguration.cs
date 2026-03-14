namespace Jellyfin.Plugin.Stats;

/// <summary>Plugin configuration model.</summary>
public class PluginConfiguration : MediaBrowser.Model.Plugins.BasePluginConfiguration
{
    /// <summary>Gets or sets the display title shown in the dashboard header and sidebar button.</summary>
    public string PluginTitle { get; set; } = "Stats";

    /// <summary>Gets or sets a value indicating whether the leaderboard is visible to non-admin users.</summary>
    public bool LeaderboardVisibleToAll { get; set; } = true;
}
