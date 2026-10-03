using MongoDB.Bson.Serialization.Attributes;

namespace ChessGame.Api.Online;

public sealed class OnlineCommand
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string MatchId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string CommandId { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public CommandAck Ack { get; set; } = new("", false, null, 0, 0, DateTime.UtcNow);
}
