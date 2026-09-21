using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ChessGame.Api.Models;

[BsonIgnoreExtraElements]
public class Item
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("code")]
    public string Code { get; set; } = string.Empty;

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("type")]
    public string Type { get; set; } = string.Empty;

    [BsonElement("rarity")]
    public string Rarity { get; set; } = string.Empty;

    [BsonElement("unityAssetKey")]
    public string UnityAssetKey { get; set; } = string.Empty;

    [BsonElement("isDefault")]
    public bool IsDefault { get; set; }

    [BsonElement("isActive")]
    public bool IsActive { get; set; }
}