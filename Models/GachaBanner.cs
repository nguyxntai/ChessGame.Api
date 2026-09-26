using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ChessGame.Api.Models;

[BsonIgnoreExtraElements]
public sealed class GachaBanner
{
    [BsonId]
    public ObjectId Id { get; set; } = ObjectId.GenerateNewId();

    [BsonElement("code")]
    public string Code { get; set; } = string.Empty;

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    [BsonElement("pityGroup")]
    public string PityGroup { get; set; } = string.Empty;

    [BsonElement("isActive")]
    public bool IsActive { get; set; }

    [BsonElement("startsAt")]
    public DateTime? StartsAt { get; set; }

    [BsonElement("endsAt")]
    public DateTime? EndsAt { get; set; }

    [BsonElement("costs")]
    public List<GachaCost> Costs { get; set; } = new();

    [BsonElement("pity")]
    public GachaPityLimits Pity { get; set; } = new();

    [BsonElement("pool")]
    public List<GachaPoolEntry> Pool { get; set; } = new();

    [BsonElement("duplicateRewards")]
    public Dictionary<string, GachaDuplicateReward> DuplicateRewards { get; set; } = new();
}

public sealed class GachaCost
{
    [BsonElement("rollCount")]
    public int RollCount { get; set; }

    [BsonElement("currency")]
    public string Currency { get; set; } = string.Empty;

    [BsonElement("amount")]
    public long Amount { get; set; }
}

// Calculated from the pool item weights, not stored on a banner.
public sealed class GachaRates
{
    public int Common { get; set; }
    public int Rare { get; set; }
    public int Epic { get; set; }
    public int Legendary { get; set; }

    public int Total => checked(Common + Rare + Epic + Legendary);
}

public sealed class GachaPityLimits
{
    [BsonElement("epic")]
    public int Epic { get; set; }

    [BsonElement("legendary")]
    public int Legendary { get; set; }
}

public sealed class GachaPoolEntry
{
    [BsonElement("itemId")]
    public ObjectId ItemId { get; set; }

    [BsonElement("weight")]
    public int Weight { get; set; } = 1;
}

public sealed class GachaDuplicateReward
{
    [BsonElement("currency")]
    public string Currency { get; set; } = string.Empty;

    [BsonElement("amount")]
    public long Amount { get; set; }
}
