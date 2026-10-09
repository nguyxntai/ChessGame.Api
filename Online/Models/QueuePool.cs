using MongoDB.Bson.Serialization.Attributes;

namespace ChessGame.Api.Online;

public sealed class QueuePool
{
    [BsonId] public string Id { get; set; } = "";
    public DateTime LastServedAt { get; set; } = DateTime.UnixEpoch;
    public DateTime CursorAt { get; set; } = DateTime.UnixEpoch;
    public string CursorId { get; set; } = "";
}
