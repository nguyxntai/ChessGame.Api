using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ChessGame.Api.DTOs.Shop;
using ChessGame.Api.Services.Shop;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ChessGame.Api.Controllers;

[ApiController, Route("api/shop")]
public sealed class ShopController : ControllerBase
{
    private readonly ShopService shop;
    public ShopController(ShopService shop) { this.shop = shop; }
    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog(CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await shop.GetCatalogAsync(token)); }
        catch (ShopException ex) { return Error(ex.Code); }
    }
    [Authorize, HttpPost("purchase")]
    public async Task<IActionResult> Purchase(ShopPurchaseRequest request, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        if (!ObjectId.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized(new { code = "INVALID_TOKEN_USER" });
        try { return Ok(await shop.PurchaseAsync(userId, request, token)); }
        catch (ShopException ex) { return Error(ex.Code); }
        catch (Exception ex) when (ex is MongoException or OperationCanceledException or TimeoutException)
        {
            return StatusCode(503, new { code = "PURCHASE_OUTCOME_UNKNOWN", requestId = request.RequestId.ToString("D"),
                message = "Retry the same purchase request to recover its outcome." });
        }
    }
    private ObjectResult Error(string code) => StatusCode(code switch
    {
        "USER_NOT_FOUND" or "OFFER_UNAVAILABLE" => 404,
        "ACCOUNT_DISABLED" => 403,
        "ALREADY_OWNED" or "QUOTE_CHANGED" or "REQUEST_CONFLICT" or "PURCHASE_CONFLICT" => 409,
        "SHOP_CONFIG_INVALID" => 503,
        _ => 400
    }, new { code });
}
