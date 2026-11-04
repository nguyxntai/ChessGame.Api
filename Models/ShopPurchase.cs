using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ChessGame.Api.Models;

public sealed class ShopPurchase
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.GenerateNewId();
    [BsonElement("userId")] public ObjectId UserId { get; set; }
    [BsonElement("requestId")] public string RequestId { get; set; } = "";
    [BsonElement("offerId")] public string OfferId { get; set; } = "";
    [BsonElement("currency")] public string Currency { get; set; } = "";
    [BsonElement("totalCost")] public long TotalCost { get; set; }
    [BsonElement("grantedItemIds")] public List<ObjectId> GrantedItemIds { get; set; } = new();
    [BsonElement("walletAfter")] public PlayerWallet WalletAfter { get; set; } = new();
    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
