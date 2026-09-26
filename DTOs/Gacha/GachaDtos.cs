using System.ComponentModel.DataAnnotations;

namespace ChessGame.Api.DTOs.Gacha;

public sealed class GachaRollRequest
{
    [Required]
    public string BannerCode { get; set; } = string.Empty;

    public int Count { get; set; }

    [Required]
    public Guid RequestId { get; set; }
}

public class GachaBannerSummaryResponse
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PityGroup { get; set; } = string.Empty;
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public List<GachaCostResponse> Costs { get; set; } = new();
    public GachaRatesResponse RarityRates { get; set; } = new();
    public GachaPityLimitResponse PityLimit { get; set; } = new();
}

public sealed class GachaBannerDetailResponse : GachaBannerSummaryResponse
{
    public List<GachaPoolItemResponse> PoolItems { get; set; } = new();
    public Dictionary<string, GachaRewardCurrencyResponse> DuplicateRewards { get; set; } = new();
}

public sealed class GachaCostResponse
{
    public int RollCount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public long Amount { get; set; }
}

public sealed class GachaRewardCurrencyResponse
{
    public string Currency { get; set; } = string.Empty;
    public long Amount { get; set; }
}

public sealed class GachaRatesResponse
{
    // Percentages, e.g. 0.5 means 0.5%.
    public decimal Common { get; set; }
    public decimal Rare { get; set; }
    public decimal Epic { get; set; }
    public decimal Legendary { get; set; }
}

public sealed class GachaPityLimitResponse
{
    public int Epic { get; set; }
    public int Legendary { get; set; }
}

public sealed class GachaPoolItemResponse
{
    public string ItemId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Rarity { get; set; } = string.Empty;
    public string UnityAssetKey { get; set; } = string.Empty;
    public int Weight { get; set; }
}

public sealed class GachaPityResponse
{
    public GachaPityTierResponse Epic { get; set; } = new();
    public GachaPityTierResponse Legendary { get; set; } = new();
}

public sealed class GachaPityTierResponse
{
    public int Current { get; set; }
    public int Limit { get; set; }
}

public sealed class GachaRollResponse
{
    public string RequestId { get; set; } = string.Empty;
    public string BannerCode { get; set; } = string.Empty;
    public int Count { get; set; }
    public string Currency { get; set; } = string.Empty;
    public long TotalCost { get; set; }
    public GachaWalletResponse Wallet { get; set; } = new();
    public List<GachaRewardResponse> Results { get; set; } = new();
    public GachaPityResponse Pity { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public sealed class GachaWalletResponse
{
    public long Golds { get; set; }
    public long Diamonds { get; set; }
    public long Tickets { get; set; }
}

public sealed class GachaRewardResponse
{
    public string ItemId { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Rarity { get; set; } = string.Empty;
    public bool IsDuplicate { get; set; }
    public GachaRewardCurrencyResponse? DuplicateReward { get; set; }
}

public sealed class GachaHistoryResponse
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public long Total { get; set; }
    public List<GachaRollResponse> Items { get; set; } = new();
}
