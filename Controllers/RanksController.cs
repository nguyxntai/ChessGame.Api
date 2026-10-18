using ChessGame.Api.Online;
using ChessGame.Api.Services.Ranks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChessGame.Api.Controllers;

[ApiController, Authorize, ServiceFilter(typeof(OnlineExceptionFilter)), Route("api/ranks")]
public sealed class RanksController(RankService service) : ControllerBase
{
    [HttpGet("config")]
    public IActionResult Config(string mode = "Classic") => Ok(service.Config(mode));
}
