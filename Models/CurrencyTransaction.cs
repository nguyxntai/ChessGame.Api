using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ChessGame.Api.Models;

[BsonIgnoreExtraElements]
public sealed class CurrencyTransaction
{
    [BsonId]
    public ObjectId Id { get; set; } = ObjectId.GenerateNewId();

    [BsonElement("userId")]
    public ObjectId UserId { get; set; }

    [BsonElement("currency")]
    public string Currency { get; set; } = string.Empty;

    [BsonElement("amount")]
    public long Amount { get; set; }

    [BsonElement("balanceAfter")]
    public long BalanceAfter { get; set; }

    [BsonElement("source")]
    public string Source { get; set; } = string.Empty;

    [BsonElement("requestId")]
    public string RequestId { get; set; } = string.Empty;

    [BsonElement("entryIndex")]
    public int EntryIndex { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
