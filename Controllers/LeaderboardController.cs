using ChessGame.Api.Online;
using ChessGame.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChessGame.Api.Controllers;

[ApiController, Authorize, ServiceFilter(typeof(OnlineExceptionFilter)), Route("api/leaderboard")]
public sealed class LeaderboardController(LeaderboardService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Top(CancellationToken ct, int limit = 100, string mode = "Classic", bool includeProvisional = false) =>
        Ok(await service.Top(OnlineIdentity.UserId(User), limit, ct, mode, includeProvisional));

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct, string mode = "Classic", bool includeProvisional = false) =>
        Ok(await service.Me(OnlineIdentity.UserId(User), ct, mode, includeProvisional));
}
