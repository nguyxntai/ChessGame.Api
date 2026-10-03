using MongoDB.Bson.Serialization.Attributes;

namespace ChessGame.Api.Online;

public sealed class OnlineEvent
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = "";
    public string? MatchId { get; set; }
    public long Sequence { get; set; }
    public long StateVersion { get; set; }
    public List<string> Recipients { get; set; } = new();
    public string PayloadJson { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool Published { get; set; }
}
