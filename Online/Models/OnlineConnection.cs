using MongoDB.Bson.Serialization.Attributes;

namespace ChessGame.Api.Online;

public sealed class OnlineConnection
{
    [BsonId] public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
}
