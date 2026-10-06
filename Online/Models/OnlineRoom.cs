using MongoDB.Bson.Serialization.Attributes;

namespace ChessGame.Api.Online;

public sealed class OnlineRoom
{
    [BsonId] public string Id { get; set; } = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
    public string Code { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string CreatorId { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string? CreationFingerprint { get; set; }
    public List<string> Members { get; set; } = new();
    public GameSettings Settings { get; set; } = new();
    public string Status { get; set; } = "Open";
    public string? MatchId { get; set; }
    public DateTime ExpiresAt { get; set; }
}
