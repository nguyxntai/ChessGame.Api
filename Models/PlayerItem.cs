using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ChessGame.Api.Models;

public class PlayerItem
{
    [BsonId]
    public ObjectId Id { get; set; } = ObjectId.GenerateNewId();

    [BsonElement("userId")]
    public ObjectId UserId { get; set; }

    [BsonElement("itemId")]
    public ObjectId ItemId { get; set; }

    [BsonElement("acquiredSource")]
    public string AcquiredSource { get; set; } = string.Empty;

    [BsonElement("acquiredAt")]
    public DateTime AcquiredAt { get; set; } = DateTime.UtcNow;
}