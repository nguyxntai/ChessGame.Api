using ChessGame.Api.DTOs.Shop;
using ChessGame.Api.Models;
using MongoDB.Bson;

namespace ChessGame.Api.Services.Shop;

// Prices live on the existing backend. Unity never grants inventory or edits the server wallet.
public sealed class ShopCatalogOptions
{
    public List<ShopProductDefinition> Products { get; set; } = new();
    public List<ShopOfferDefinition> Offers { get; set; } = new();
}
public sealed class ShopProductDefinition
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string UnityAssetKey { get; set; } = "";
    public long Golds { get; set; }
    public long Diamonds { get; set; }
}
public sealed class ShopOfferDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Featured { get; set; }
    public int DiscountPercent { get; set; }
    public List<string> ProductIds { get; set; } = new();
}

public static class ShopCatalog
{
    public static List<ShopOfferResponse> Resolve(ShopCatalogOptions config, IReadOnlyList<Item> items)
    {
        if (config.Products.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != config.Products.Count ||
            config.Offers.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != config.Offers.Count)
            throw new ShopException("SHOP_CONFIG_INVALID");
        var products = new Dictionary<string, ShopProductResponse>(StringComparer.Ordinal);
        foreach (var product in config.Products)
        {
            if (string.IsNullOrWhiteSpace(product.Id) || product.Golds <= 0 || product.Diamonds <= 0 ||
                product.Golds > 1_000_000_000 || product.Diamonds > 1_000_000_000 ||
                product.Type != CosmeticLoadoutPolicy.SetType && product.Type != CosmeticLoadoutPolicy.BoardType &&
                CosmeticLoadoutPolicy.SlotForType(product.Type) == null)
                throw new ShopException("SHOP_CONFIG_INVALID");
            var matches = items.Where(i => i.IsActive && !i.IsDefault && i.Type == product.Type &&
                i.UnityAssetKey == product.UnityAssetKey).ToList();
            // No invented ItemIds, no alias/rarity guesses, and no sale of ambiguous records.
            if (matches.Count != 1) continue;
            var item = matches[0];
            products.Add(product.Id, new ShopProductResponse { ItemId = item.Id.ToString(), Name = item.Name,
                Type = item.Type, UnityAssetKey = item.UnityAssetKey, Rarity = item.Rarity,
                Golds = product.Golds, Diamonds = product.Diamonds });
        }
        var result = new List<ShopOfferResponse>();
        foreach (var offer in config.Offers)
        {
            if (string.IsNullOrWhiteSpace(offer.Id) || offer.ProductIds.Count is < 1 or > 12 ||
                offer.ProductIds.Distinct(StringComparer.Ordinal).Count() != offer.ProductIds.Count ||
                offer.DiscountPercent is < 0 or > 90 || offer.ProductIds.Any(id => !config.Products.Any(p => p.Id == id)))
                throw new ShopException("SHOP_CONFIG_INVALID");
            if (offer.ProductIds.Any(id => !products.ContainsKey(id))) continue;
            var contents = offer.ProductIds.Select(id => products[id]).ToList();
            if (contents.Select(i => i.ItemId).Distinct().Count() != contents.Count) throw new ShopException("SHOP_CONFIG_INVALID");
            result.Add(new ShopOfferResponse { OfferId = offer.Id, Name = offer.Name, Description = offer.Description,
                Featured = offer.Featured, IsBundle = contents.Count > 1, DiscountPercent = offer.DiscountPercent, Items = contents });
        }
        return result;
    }

    public static (long Price, List<ShopProductResponse> Missing) Quote(ShopOfferResponse offer, ISet<string> owned, string currency)
    {
        if (currency != "GOLDS" && currency != "DIAMONDS") throw new ShopException("INVALID_CURRENCY");
        var missing = offer.Items.Where(i => !owned.Contains(i.ItemId)).ToList();
        long total = missing.Sum(i => currency == "GOLDS" ? i.Golds : i.Diamonds);
        // Round up once, after excluding owned contents. No discount is lost for partial bundles.
        return (checked((total * (100 - offer.DiscountPercent) + 99) / 100), missing);
    }

    public static void ValidateRequest(ShopPurchaseRequest request)
    {
        if (request.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.OfferId) || request.OfferId.Length > 80 ||
            request.ExpectedPrice <= 0 || request.ExpectedItemIds == null || request.ExpectedItemIds.Count is < 1 or > 12 ||
            request.ExpectedItemIds.Any(id => !ObjectId.TryParse(id, out _)) ||
            request.ExpectedItemIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.ExpectedItemIds.Count)
            throw new ShopException("INVALID_PURCHASE");
        if (request.Currency != "GOLDS" && request.Currency != "DIAMONDS") throw new ShopException("INVALID_CURRENCY");
    }
}

public sealed class ShopException : Exception
{
    public string Code { get; }
    public ShopException(string code) : base(code) { Code = code; }
}
