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

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        // Tùy JWT claim mapping của ASP.NET Core,
        // "sub" có thể tồn tại dưới một trong hai tên này.
        string? userId =
            User.FindFirstValue(
                JwtRegisteredClaimNames.Sub
            )
            ??
            User.FindFirstValue(
                ClaimTypes.NameIdentifier
            );

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new
            {
                message = "Token không chứa User ID."
            });
        }

        if (!ObjectId.TryParse(
                userId,
                out var objectId))
        {
            return Unauthorized(new
            {
                message = "User ID trong token không hợp lệ."
            });
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

        var response =
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

                Stats =
                    new PlayerStatsResponse
                    {
                        Elo =
                            user.Stats.Elo,

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

        return Ok(response);
    }
}