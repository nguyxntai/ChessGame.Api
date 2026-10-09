using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChessGame.Api.Online;

[ApiController, Authorize, ServiceFilter(typeof(OnlineExceptionFilter)), Route("api/rooms")]
public sealed class RoomsController(OnlineService service) : ControllerBase
{
    private string UserId => OnlineIdentity.UserId(User);
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct, int page = 1, int pageSize = 20, string? mode = null, string? region = null) =>
        Ok(await service.ListRooms(UserId, page, pageSize, mode, region, ct));
    [HttpPost] public async Task<IActionResult> Create(CreateRoomRequest request, CancellationToken ct) => Ok(await service.CreateRoom(UserId, request, ct));
    [HttpPost("join")] public async Task<IActionResult> Join(JoinRoomRequest request, CancellationToken ct) => Ok(await service.JoinRoom(UserId, request.Code, ct));
    [HttpGet("{roomId}")] public async Task<IActionResult> Get(string roomId, CancellationToken ct) => Ok(await service.GetRoom(UserId, roomId, ct));
    [HttpPatch("{roomId}/settings")] public async Task<IActionResult> Settings(string roomId, GameSettings settings, CancellationToken ct) => Ok(await service.RoomSettings(UserId, roomId, settings, ct));
    [HttpPost("{roomId}/leave")] public async Task<IActionResult> Leave(string roomId, CancellationToken ct) => Ok(await service.LeaveRoom(UserId, roomId, ct));
    [HttpPost("{roomId}/start")] public async Task<IActionResult> Start(string roomId, CancellationToken ct) => Ok(await service.StartRoom(UserId, roomId, ct));
    [HttpDelete("{roomId}")] public async Task<IActionResult> Close(string roomId, CancellationToken ct) => Ok(await service.DeleteRoom(UserId, roomId, ct));
}
