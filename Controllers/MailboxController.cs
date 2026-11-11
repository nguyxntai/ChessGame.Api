using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ChessGame.Api.Services.Mailbox;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ChessGame.Api.Controllers;

[ApiController, Authorize, Route("api/mailbox")]
public sealed class MailboxController(MailboxService mailbox) : ControllerBase
{
    [HttpGet] public Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Run(async id => await mailbox.ListAsync(id, page, pageSize, ct));
    [HttpPost("enroll")] public Task<IActionResult> Enroll(CancellationToken ct) => Run(async id => await mailbox.EnrollAsync(id, ct));
    [HttpPost("claim-all")] public Task<IActionResult> ClaimAll([FromBody] MailClaimRequest request, CancellationToken ct) =>
        Run(async id => await mailbox.ClaimAllAsync(id, ct));
    [HttpGet("{mailId}")] public Task<IActionResult> Get(string mailId, CancellationToken ct) => Run(async id => await mailbox.GetAsync(id, mailId, ct));
    [HttpPost("{mailId}/read")] public Task<IActionResult> Read(string mailId, CancellationToken ct) => Run(async id => await mailbox.ReadAsync(id, mailId, ct));
    [HttpPost("{mailId}/claim")] public Task<IActionResult> Claim(string mailId, [FromBody] MailClaimRequest request, CancellationToken ct) =>
        Run(async id => await mailbox.ClaimAsync(id, mailId, ct));

    private async Task<IActionResult> Run(Func<ObjectId, Task<object>> action)
    {
        if (User.IsInRole("Guest") || string.Equals(User.FindFirstValue("guest"), "true", StringComparison.OrdinalIgnoreCase) ||
            !ObjectId.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            return Unauthorized(new { code = "ACCOUNT_REQUIRED" });
        try { return Ok(await action(id)); }
        catch (MailboxException ex) { return StatusCode(ex.Code switch {
            "USER_NOT_FOUND" or "MAIL_NOT_FOUND" => 404, "ACCOUNT_DISABLED" => 403, "INVALID_PAGE" => 400,
            "MAIL_EXPIRED" or "NO_REWARDS" => 409, _ => 500 }, new { code = ex.Code }); }
        catch (OverflowException) { return Conflict(new { code = "WALLET_LIMIT" }); }
        // An interrupted/ambiguous commit must be recovered using GET mail + /me, never described as a failed grant.
        catch (MongoException) { return StatusCode(503, new { code = "MAILBOX_UNAVAILABLE_RECOVER_STATE" }); }
    }
}
