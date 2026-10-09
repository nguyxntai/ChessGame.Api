using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChessGame.Api.Online;

[ApiController, Authorize, ServiceFilter(typeof(OnlineExceptionFilter)), Route("api/matchmaking/tickets")]
public sealed class MatchmakingController(OnlineService service) : ControllerBase
{
    private string UserId => OnlineIdentity.UserId(User);
    [HttpPost] public async Task<IActionResult> Queue(QueueRequest request, CancellationToken ct) => Ok(await service.Queue(UserId, request, ct));
    [HttpGet("current")] public async Task<IActionResult> Current(CancellationToken ct) => Ok(await service.CurrentTicket(UserId, ct));
    [HttpDelete("{ticketId}")] public async Task<IActionResult> Cancel(string ticketId, CancellationToken ct) => Ok(await service.CancelTicket(UserId, ticketId, ct));

    [HttpGet("/api/matchmaking/quality")]
    public async Task<IActionResult> Quality(CancellationToken ct, int days = 7) =>
        Ok(await service.Quality(OnlineIdentity.UserId(User), days, ct));
}
