using ChessGame.Api.DTOs.User;

namespace ChessGame.Api.DTOs.Shop;

public sealed class ShopCatalogResponse
{
    public List<ShopOfferResponse> Offers { get; set; } = new();
}

public sealed class ShopOfferResponse
{
    public string OfferId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Featured { get; set; }
    public bool IsBundle { get; set; }
    public int DiscountPercent { get; set; }
    public List<ShopProductResponse> Items { get; set; } = new();
}

public sealed class ShopProductResponse
{
    public string ItemId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string UnityAssetKey { get; set; } = "";
    public string Rarity { get; set; } = "";
    public long Golds { get; set; }
    public long Diamonds { get; set; }
}

public sealed class ShopPurchaseRequest
{
    public Guid RequestId { get; set; }
    public string OfferId { get; set; } = "";
    public string Currency { get; set; } = "";
    // Confirmation must describe the current price and the exact missing contents.
    public long ExpectedPrice { get; set; }
    public List<string> ExpectedItemIds { get; set; } = new();
}

public sealed class ShopPurchaseResponse
{
    public string RequestId { get; set; } = "";
    public string OfferId { get; set; } = "";
    public string Currency { get; set; } = "";
    public long TotalCost { get; set; }
    public List<string> GrantedItemIds { get; set; } = new();
    public WalletResponse Wallet { get; set; } = new();
}
