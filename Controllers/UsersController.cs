using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ChessGame.Api.DTOs.User;
using ChessGame.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;

namespace ChessGame.Api.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly UserService _userService;

    public UsersController(UserService userService)
    {
        _userService = userService;
    }

    /// <summary>Read the authenticated player's profile and server-owned balances/statistics.</summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        if (!TryGetUserId(out var objectId, out var errorResult))
        {
            return errorResult;
        }

        var user =
            await _userService.GetByIdAsync(objectId);

        if (user is null)
        {
            return NotFound(new
            {
                message =
                    "Không tìm thấy người chơi."
            });
        }

        if (!user.IsActive)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    message =
                        "Tài khoản đã bị vô hiệu hóa."
                }
            );
        }

        return Ok(CreateResponse(user));
    }

    /// <summary>Update only the authenticated player's display name and/or avatar.</summary>
    [Authorize]
    [HttpPatch("me/profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        if (!TryGetUserId(out var objectId, out var errorResult))
        {
            return errorResult;
        }

        var user = await _userService.GetByIdAsync(objectId);
        if (user is null)
        {
            return NotFound(new { message = "Không tìm thấy người chơi." });
        }

        if (!user.IsActive)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Tài khoản đã bị vô hiệu hóa." });
        }

        var updatedUser = await _userService.UpdateProfileAsync(objectId, request);
        if (updatedUser is null)
        {
            // The account may have been removed or disabled after the initial read.
            user = await _userService.GetByIdAsync(objectId);
            return user is null
                ? NotFound(new { message = "Không tìm thấy người chơi." })
                : StatusCode(StatusCodes.Status403Forbidden,
                    new { message = "Tài khoản đã bị vô hiệu hóa." });
        }

        return Ok(CreateResponse(updatedUser));
    }

    private bool TryGetUserId(out ObjectId userId, out IActionResult errorResult)
    {
        userId = ObjectId.Empty;
        errorResult = Unauthorized();
        string? claim = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(claim))
        {
            errorResult = Unauthorized(new { message = "Token không chứa User ID." });
            return false;
        }

        if (!ObjectId.TryParse(claim, out userId))
        {
            errorResult = Unauthorized(new { message = "User ID trong token không hợp lệ." });
            return false;
        }

        return true;
    }

    private static UserMeResponse CreateResponse(ChessGame.Api.Models.User user) =>
            new UserMeResponse
            {
                UserId =
                    user.Id.ToString(),

                Username =
                    user.Username,

                Email =
                    user.Email,

                Profile =
                    new UserProfileResponse
                    {
                        DisplayName =
                            user.Profile.DisplayName,

                        AvatarId =
                            user.Profile.AvatarId
                    },

                Wallet =
                    new WalletResponse
                    {
                        Golds =
                            user.Wallet.Golds,

                        Diamonds =
                            user.Wallet.Diamonds,

                        Tickets =
                            user.Wallet.Tickets
                    },

                Ratings = user.Ratings,

                Stats =
                    new PlayerStatsResponse
                    {
                        Elo =
                            ChessGame.Api.Services.Ratings.RatingPolicies.Display(user.Ratings.Classic.Rating),

                        Wins =
                            user.Stats.Wins,

                        Losses =
                            user.Stats.Losses,

                        Draws =
                            user.Stats.Draws,

                        GamesPlayed =
                            user.Stats.GamesPlayed
                    },

                Equipped =
                    new EquippedSkinsResponse
                    {
                        ChessSkinId =
                            user.Equipped.ChessSkinId?
                                .ToString(),

                        BoardSkinId =
                            user.Equipped.BoardSkinId?
                                .ToString()
                    },

                CreatedAt =
                    user.CreatedAt
            };

}
