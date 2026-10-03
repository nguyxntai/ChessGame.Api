using MongoDB.Bson.Serialization.Attributes;

namespace ChessGame.Api.Online;

public sealed class OnlineSeat
{
    [BsonId] public string UserId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string ReferenceId { get; set; } = "";
}
