using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ChessGame.Api.Models;

public class User
{
    [BsonId]
    public ObjectId Id { get; set; } = ObjectId.GenerateNewId();

    [BsonElement("username")]
    public string Username { get; set; } = string.Empty;

    [BsonElement("normalizedUsername")]
    public string NormalizedUsername { get; set; } = string.Empty;

    [BsonElement("email")]
    public string Email { get; set; } = string.Empty;

    [BsonElement("normalizedEmail")]
    public string NormalizedEmail { get; set; } = string.Empty;

    [BsonElement("passwordHash")]
    public string PasswordHash { get; set; } = string.Empty;

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("profile")]
    public UserProfile Profile { get; set; } = new();

    [BsonElement("wallet")]
    public PlayerWallet Wallet { get; set; } = new();

    [BsonElement("stats")]
    public PlayerStats Stats { get; set; } = new();

    [BsonElement("equipped")]
    public EquippedSkins Equipped { get; set; } = new();

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class UserProfile
{
    [BsonElement("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [BsonElement("avatarId")]
    public string? AvatarId { get; set; }
}

public class PlayerWallet
{
    [BsonElement("golds")]
    public long Golds { get; set; } = 1000;

    [BsonElement("diamonds")]
    public long Diamonds { get; set; } = 0;

    [BsonElement("tickets")]
    public long Tickets { get; set; } = 10;
}

public class PlayerStats
{
    [BsonElement("elo")]
    public int Elo { get; set; } = 1000;

    [BsonElement("wins")]
    public int Wins { get; set; } = 0;

    [BsonElement("losses")]
    public int Losses { get; set; } = 0;

    [BsonElement("draws")]
    public int Draws { get; set; } = 0;

    [BsonElement("gamesPlayed")]
    public int GamesPlayed { get; set; } = 0;
}

public class EquippedSkins
{
    [BsonElement("chessSkinId")]
    public ObjectId? ChessSkinId { get; set; }

    [BsonElement("boardSkinId")]
    public ObjectId? BoardSkinId { get; set; }
}