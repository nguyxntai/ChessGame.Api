using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ChessGame.Api.Models;

[BsonIgnoreExtraElements]
public class RefreshToken
{
    [BsonId]
    public ObjectId Id { get; set; }
        = ObjectId.GenerateNewId();

    [BsonElement("userId")]
    public ObjectId UserId { get; set; }

    [BsonElement("tokenHash")]
    public string TokenHash { get; set; }
        = string.Empty;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }
        = DateTime.UtcNow;

    [BsonElement("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    [BsonElement("revokedAt")]
    public DateTime? RevokedAt { get; set; }

    [BsonElement("replacedByTokenHash")]
    public string? ReplacedByTokenHash { get; set; }
}