using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ChessGame.Api.Models;

[BsonIgnoreExtraElements]
public sealed class GachaPity
{
    [BsonId]
    public ObjectId Id { get; set; } = ObjectId.GenerateNewId();

    [BsonElement("userId")]
    public ObjectId UserId { get; set; }

    [BsonElement("pityGroup")]
    public string PityGroup { get; set; } = string.Empty;

    [BsonElement("epicCurrent")]
    public int EpicCurrent { get; set; }

    [BsonElement("legendaryCurrent")]
    public int LegendaryCurrent { get; set; }

    [BsonElement("version")]
    public long Version { get; set; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
