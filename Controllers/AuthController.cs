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

    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request)
    {
        try
        {
            var result =
                await _authService.LoginAsync(request);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message switch
            {
                "INVALID_CREDENTIALS" =>
                    Unauthorized(new
                    {
                        message =
                            "Email hoặc mật khẩu không chính xác."
                    }),

                "ACCOUNT_DISABLED" =>
                    StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            message =
                                "Tài khoản đã bị vô hiệu hóa."
                        }
                    ),

                _ =>
                    StatusCode(
                        StatusCodes
                            .Status500InternalServerError,
                        new
                        {
                            message =
                                "Đã xảy ra lỗi khi đăng nhập."
                        }
                    )
            };
        }
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
                    
                "DEFAULT_ITEMS_NOT_CONFIGURED" =>
                    StatusCode(
                        StatusCodes.Status500InternalServerError,
                    new
                    {
                        message =
                            "Hệ thống chưa cấu hình skin mặc định."
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

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshRequest request)
    {
        try
        {
            var result =
                await _authService
                    .RefreshAsync(request);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message switch
            {
                "INVALID_REFRESH_TOKEN" =>
                    Unauthorized(new
                    {
                        message =
                            "Refresh token không hợp lệ hoặc đã hết hạn."
                    }),

                "ACCOUNT_DISABLED" =>
                    StatusCode(
                        StatusCodes
                            .Status403Forbidden,
                        new
                        {
                            message =
                                "Tài khoản đã bị vô hiệu hóa."
                        }
                    ),

                _ =>
                    StatusCode(
                        StatusCodes
                            .Status500InternalServerError
                    )
            };
        }
    }
    
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequest request)
    {
        await _authService
            .LogoutAsync(request);

        return NoContent();
    }
}