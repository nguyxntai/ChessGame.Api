using System.ComponentModel.DataAnnotations;

namespace ChessGame.Api.DTOs.Auth;

public class RegisterRequest
{
    [Required]
    [StringLength(
        20,
        MinimumLength = 3,
        ErrorMessage = "Username phải từ 3 đến 20 ký tự."
    )]
    public string Username { get; set; } = string.Empty;

    [Required]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(
        6,
        ErrorMessage = "Password phải có ít nhất 6 ký tự."
    )]
    public string Password { get; set; } = string.Empty;
}