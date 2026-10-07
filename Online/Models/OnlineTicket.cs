using MongoDB.Bson.Serialization.Attributes;

namespace ChessGame.Api.Online;

public sealed class OnlineTicket
{
    [BsonId] public string Id { get; set; } = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
    public string UserId { get; set; } = "";
    public string RequestId { get; set; } = "";
    public GameSettings Settings { get; set; } = new();
    public int Rating { get; set; }
    public string PoolKey { get; set; } = "";
    public bool Provisional { get; set; } = true;
    public double? RoundTripMilliseconds { get; set; }
    public string Status { get; set; } = "Queued";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public string? MatchId { get; set; }
}
