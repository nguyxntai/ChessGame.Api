namespace ChessGame.Api.DTOs.Auth;

public class RefreshResponse
{
    public string AccessToken { get; set; }
        = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public string RefreshToken { get; set; }
        = string.Empty;

    public DateTime RefreshTokenExpiresAt { get; set; }
}