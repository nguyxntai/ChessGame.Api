using MongoDB.Bson.Serialization.Attributes;

namespace ChessGame.Api.Online;

public sealed class OnlineMove
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string MatchId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string CommandId { get; set; } = "";
    public long Sequence { get; set; }
    public long StateVersion { get; set; }
    public string Kind { get; set; } = "Move";
    public string PayloadJson { get; set; } = "";
    public DateTime PlayedAt { get; set; }
}
