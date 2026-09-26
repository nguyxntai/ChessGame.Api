using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ChessGame.Api.Models;

[BsonIgnoreExtraElements]
public sealed class GachaRoll
{
    [BsonId]
    public ObjectId Id { get; set; } = ObjectId.GenerateNewId();

    [BsonElement("userId")]
    public ObjectId UserId { get; set; }

    [BsonElement("bannerCode")]
    public string BannerCode { get; set; } = string.Empty;

    [BsonElement("pityGroup")]
    public string PityGroup { get; set; } = string.Empty;

    [BsonElement("requestId")]
    public string RequestId { get; set; } = string.Empty;

    [BsonElement("count")]
    public int Count { get; set; }

    [BsonElement("currency")]
    public string Currency { get; set; } = string.Empty;

    [BsonElement("totalCost")]
    public long TotalCost { get; set; }

    [BsonElement("walletAfter")]
    public GachaWalletSnapshot WalletAfter { get; set; } = new();

    [BsonElement("results")]
    public List<GachaRollReward> Results { get; set; } = new();

    [BsonElement("pityAfter")]
    public GachaPitySnapshot PityAfter { get; set; } = new();

    [BsonElement("pityLimit")]
    public GachaPityLimits PityLimit { get; set; } = new();

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class GachaRollReward
{
    [BsonElement("itemId")]
    public ObjectId ItemId { get; set; }

    [BsonElement("itemCode")]
    public string ItemCode { get; set; } = string.Empty;

    [BsonElement("itemName")]
    public string ItemName { get; set; } = string.Empty;

    [BsonElement("rarity")]
    public string Rarity { get; set; } = string.Empty;

    [BsonElement("isDuplicate")]
    public bool IsDuplicate { get; set; }

    [BsonElement("duplicateReward")]
    public GachaDuplicateReward? DuplicateReward { get; set; }
}

public sealed class GachaPitySnapshot
{
    [BsonElement("epic")]
    public int Epic { get; set; }

    [BsonElement("legendary")]
    public int Legendary { get; set; }
}

public sealed class GachaWalletSnapshot
{
    [BsonElement("golds")]
    public long Golds { get; set; }

    [BsonElement("diamonds")]
    public long Diamonds { get; set; }

    [BsonElement("tickets")]
    public long Tickets { get; set; }
}
