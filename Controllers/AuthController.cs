using ChessGame.Api.DTOs.Auth;
using ChessGame.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChessGame.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request)
    {
        try
        {
            var result =
                await _authService.RegisterAsync(request);

            return StatusCode(
                StatusCodes.Status201Created,
                result
            );
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message switch
            {
                "USERNAME_EXISTS" =>
                    Conflict(new
                    {
                        message =
                            "Username đã được sử dụng."
                    }),

                "EMAIL_EXISTS" =>
                    Conflict(new
                    {
                        message =
                            "Email đã được sử dụng."
                    }),

                "USER_ALREADY_EXISTS" =>
                    Conflict(new
                    {
                        message =
                            "Username hoặc email đã tồn tại."
                    }),

                _ =>
                    StatusCode(
                        StatusCodes
                            .Status500InternalServerError,
                        new
                        {
                            message =
                                "Đã xảy ra lỗi khi tạo tài khoản."
                        }
                    )
            };
        }
    }
}