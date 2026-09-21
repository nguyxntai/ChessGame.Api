namespace ChessGame.Api.DTOs.User;

public class UserMeResponse
{
    public string UserId { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public UserProfileResponse Profile { get; set; } = new();

    public WalletResponse Wallet { get; set; } = new();

    public PlayerStatsResponse Stats { get; set; } = new();

    public EquippedSkinsResponse Equipped { get; set; } = new();

    public DateTime CreatedAt { get; set; }
}

public class UserProfileResponse
{
    public string DisplayName { get; set; } = string.Empty;

    public string? AvatarId { get; set; }
}

public class WalletResponse
{
    public long Golds { get; set; }

    public long Diamonds { get; set; }

    public long Tickets { get; set; }
}

public class PlayerStatsResponse
{
    public int Elo { get; set; }

    public int Wins { get; set; }

    public int Losses { get; set; }

    public int Draws { get; set; }

    public int GamesPlayed { get; set; }
}

public class EquippedSkinsResponse
{
    public string? ChessSkinId { get; set; }

    public string? BoardSkinId { get; set; }
}