using System;
using System.Collections.Generic;
using System.Linq;
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

    // Remaining endpoints added in Tasks 5-10
}
