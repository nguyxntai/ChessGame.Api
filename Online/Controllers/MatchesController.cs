using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChessGame.Api.Online;

[ApiController, Authorize, ServiceFilter(typeof(OnlineExceptionFilter)), Route("api/matches")]
public sealed class MatchesController(OnlineService service) : ControllerBase
{
    private string UserId => OnlineIdentity.UserId(User);
    [HttpGet("current")] public async Task<IActionResult> Current(CancellationToken ct) => Ok(await service.CurrentMatch(UserId, ct));
    [HttpGet("{matchId}")] public async Task<IActionResult> Get(string matchId, CancellationToken ct) => Ok(await service.GetMatch(UserId, matchId, ct));
    [HttpGet("{matchId}/state")] public async Task<IActionResult> State(string matchId, CancellationToken ct) => Ok(await service.State(UserId, matchId, ct));
    [HttpGet("{matchId}/moves")] public async Task<IActionResult> Moves(string matchId, CancellationToken ct, int page = 1, int pageSize = 20, long? afterSequence = null) =>
        Ok(await service.Moves(UserId, matchId, page, pageSize, afterSequence, ct));
    [HttpGet("{matchId}/result")] public async Task<IActionResult> Result(string matchId, CancellationToken ct) => Ok(await service.Result(UserId, matchId, ct));
    [HttpGet("/api/users/me/matches")] public async Task<IActionResult> History(CancellationToken ct, int page = 1, int pageSize = 20) => Ok(await service.History(UserId, page, pageSize, ct));
}
