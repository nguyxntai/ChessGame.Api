namespace ChessGame.Api.DTOs.Auth;

public class RegisterResponse
{
    public string UserId { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public long Golds { get; set; }

    public long Diamonds { get; set; }

    public long Tickets { get; set; }

    public int Elo { get; set; }

    public DateTime CreatedAt { get; set; }
}