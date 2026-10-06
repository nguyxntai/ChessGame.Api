using MongoDB.Bson.Serialization.Attributes;

namespace ChessGame.Api.Online;

public sealed class OnlineConnection
{
    [BsonId] public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public double? RoundTripMilliseconds { get; set; }
    public double JitterMilliseconds { get; set; }
    public int LatencySamples { get; set; }
    public DateTime? LatencyMeasuredAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
