using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ChessGame.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;

namespace ChessGame.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/wallet")]
public sealed class WalletController : ControllerBase
{
    private readonly WalletService _wallet;

    public WalletController(WalletService wallet) => _wallet = wallet;

    [HttpGet]
    public Task<IActionResult> GetWallet(CancellationToken cancellationToken) =>
        ReadAsync(async id => await _wallet.GetWalletAsync(id, cancellationToken));

    [HttpGet("transactions")]
    public Task<IActionResult> GetTransactions(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        ReadAsync(async id => await _wallet.GetTransactionsAsync(id, page, pageSize, cancellationToken));

    private async Task<IActionResult> ReadAsync(Func<ObjectId, Task<object>> read)
    {
        var claim = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!ObjectId.TryParse(claim, out var userId))
            return Unauthorized(new { code = "INVALID_TOKEN_USER" });

        try
        {
            return Ok(await read(userId));
        }
        catch (WalletException ex)
        {
            var status = ex.Code switch
            {
                "INVALID_PAGE" => StatusCodes.Status400BadRequest,
                "USER_NOT_FOUND" => StatusCodes.Status404NotFound,
                "ACCOUNT_DISABLED" => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status500InternalServerError
            };
            return StatusCode(status, new { code = ex.Code });
        }
    }
}
