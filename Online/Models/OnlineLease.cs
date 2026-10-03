using MongoDB.Bson.Serialization.Attributes;

namespace ChessGame.Api.Online;

public sealed class OnlineLease
{
    [BsonId] public string Id { get; set; } = "runtime";
    public string Owner { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public long Revision { get; set; }
}
