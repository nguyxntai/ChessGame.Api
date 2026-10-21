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
    private readonly ILogger<GachaController> _logger;

    public GachaController(GachaService gacha, ILogger<GachaController> logger)
    {
        _gacha = gacha;
        _logger = logger;
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

        var originalRequestId = request.RequestId;
        var originalBannerCode = request.BannerCode?.Trim().ToUpperInvariant();
        var originalCount = request.Count;

        try
        {
            return Ok(await _gacha.RollAsync(userId.Value, request));
        }
        catch (GachaService.CommitOutcomeUnknownException ex)
        {
            Response.Headers.CacheControl = "no-store, no-cache";
            Response.Headers.Pragma = "no-cache";
            Response.Headers.Remove("Retry-After");

            if (originalRequestId == Guid.Empty || ex.UserId != userId.Value
                || !string.Equals(ex.RequestId, originalRequestId.ToString("D"), StringComparison.Ordinal)
                || !string.Equals(ex.BannerCode, originalBannerCode, StringComparison.Ordinal)
                || ex.Count != originalCount)
            {
                // Do not log the exception, account, receipt or transaction identifiers.
                _logger.LogError(new EventId(3201, "GachaUnknownCorrelationMismatch"),
                    "Gacha UNKNOWN outcome correlation validation failed.");
                return StatusCode(500, new { code = "GACHA_OUTCOME_UNAVAILABLE" });
            }

            return StatusCode(503, new
            {
                code = "COMMIT_OUTCOME_UNKNOWN",
                outcome = "UNKNOWN",
                requestId = originalRequestId.ToString("D"),
                recovery = "RECOVER_ORIGINAL_REQUEST_ID"
            });
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

    [Authorize]
    [HttpGet("rolls/by-request/{requestId}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetRollByRequest(
        string requestId,
        [FromQuery] string? bannerCode = null,
        [FromQuery] int? count = null,
        CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId();
        if (userId is null)
            return Unauthorized(new { code = "INVALID_TOKEN_USER" });
        if (!Guid.TryParse(requestId, out var id) || id == Guid.Empty)
            return BadRequest(new { code = "INVALID_RECOVERY_REQUEST" });

        var query = Request.Query;
        var hasBanner = query.ContainsKey("bannerCode");
        var hasCount = query.ContainsKey("count");
        if (hasBanner != hasCount || (hasBanner
            && (query["bannerCode"].Count != 1 || query["count"].Count != 1
                || string.IsNullOrWhiteSpace(bannerCode) || count is null)))
            return BadRequest(new { code = "INVALID_RECOVERY_REQUEST" });

        try
        {
            return Ok(await _gacha.GetRollByRequestAsync(
                userId.Value, id, bannerCode, count, cancellationToken));
        }
        catch (GachaException ex)
        {
            return Error(ex);
        }
        catch (Exception ex)
        {
            // No exception message/connection details or claim of noncommit in the response.
            _logger.LogWarning("Gacha recovery read unavailable ({FailureType})", ex.GetType().Name);
            return StatusCode(503, new { code = "RECOVERY_UNAVAILABLE" });
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
            "INVALID_BANNER_CODE" or "INVALID_ROLL_REQUEST" or "INVALID_PAGE" or "INVALID_RECOVERY_REQUEST" => 400,
            "BANNER_NOT_FOUND" or "USER_NOT_FOUND" or "NOT_OBSERVED" => 404,
            "ACCOUNT_DISABLED" => 403,
            "INSUFFICIENT_CURRENCY" or "REQUEST_ID_CONFLICT" => 409,
            "BANNER_MISCONFIGURED" or "PITY_STATE_INVALID" or "WALLET_OVERFLOW" or "ROLL_BUSY" => 503,
            _ => 500
        };
        return StatusCode(status, new { code = ex.Code });
    }
}
