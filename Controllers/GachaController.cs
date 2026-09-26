using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ChessGame.Api.DTOs.Gacha;
using ChessGame.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;

namespace ChessGame.Api.Controllers;

[ApiController]
[Route("api/gacha")]
public sealed class GachaController : ControllerBase
{
    private readonly GachaService _gacha;

    public GachaController(GachaService gacha)
    {
        _gacha = gacha;
    }

    [HttpGet("banners")]
    public async Task<IActionResult> GetBanners()
    {
        return Ok(await _gacha.GetActiveBannersAsync());
    }

    [HttpGet("banners/{bannerCode}")]
    public async Task<IActionResult> GetBanner(string bannerCode)
    {
        try
        {
            return Ok(await _gacha.GetBannerAsync(bannerCode));
        }
        catch (GachaException ex)
        {
            return Error(ex);
        }
    }

    [Authorize]
    [HttpPost("roll")]
    public async Task<IActionResult> Roll([FromBody] GachaRollRequest request)
    {
        var userId = CurrentUserId();
        if (userId is null)
            return Unauthorized(new { code = "INVALID_TOKEN_USER" });

        try
        {
            return Ok(await _gacha.RollAsync(userId.Value, request));
        }
        catch (GachaException ex)
        {
            return Error(ex);
        }
    }

    [Authorize]
    [HttpGet("banners/{bannerCode}/pity")]
    public async Task<IActionResult> GetPity(string bannerCode)
    {
        var userId = CurrentUserId();
        if (userId is null)
            return Unauthorized(new { code = "INVALID_TOKEN_USER" });

        try
        {
            return Ok(await _gacha.GetPityAsync(userId.Value, bannerCode));
        }
        catch (GachaException ex)
        {
            return Error(ex);
        }
    }

    [Authorize]
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var userId = CurrentUserId();
        if (userId is null)
            return Unauthorized(new { code = "INVALID_TOKEN_USER" });

        try
        {
            return Ok(await _gacha.GetHistoryAsync(userId.Value, page, pageSize));
        }
        catch (GachaException ex)
        {
            return Error(ex);
        }
    }

    private ObjectId? CurrentUserId()
    {
        var raw = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return ObjectId.TryParse(raw, out var id) ? id : null;
    }

    private IActionResult Error(GachaException ex)
    {
        var status = ex.Code switch
        {
            "INVALID_BANNER_CODE" or "INVALID_ROLL_REQUEST" or "INVALID_PAGE" => 400,
            "BANNER_NOT_FOUND" or "USER_NOT_FOUND" => 404,
            "ACCOUNT_DISABLED" => 403,
            "INSUFFICIENT_CURRENCY" or "REQUEST_ID_CONFLICT" => 409,
            "BANNER_MISCONFIGURED" or "PITY_STATE_INVALID" or "WALLET_OVERFLOW" or "ROLL_BUSY" => 503,
            _ => 500
        };
        return StatusCode(status, new { code = ex.Code });
    }
}
