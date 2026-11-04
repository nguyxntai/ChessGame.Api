using ChessGame.Api.DTOs.Shop;
using ChessGame.Api.Models;
using ChessGame.Api.Services.Shop;
using MongoDB.Bson;
using Xunit;

namespace ChessGame.Api.Tests;

public sealed class ShopTests
{
    private static readonly string A = "111111111111111111111111", B = "222222222222222222222222";
    private static ShopOfferResponse Bundle() => new() { OfferId = "bundle", IsBundle = true, DiscountPercent = 15,
        Items = new() { new() { ItemId = A, Golds = 1800, Diamonds = 90 }, new() { ItemId = B, Golds = 1600, Diamonds = 80 } } };
    [Theory]
    [InlineData("GOLDS", 2890, 1360)]
    [InlineData("DIAMONDS", 145, 68)]
    public void BundlesExcludeOwnedItemsBeforeDiscountAndRoundUp(string currency, long full, long partial)
    {
        Assert.Equal(full, ShopCatalog.Quote(Bundle(), new HashSet<string>(), currency).Price);
        var quote = ShopCatalog.Quote(Bundle(), new HashSet<string> { A }, currency);
        Assert.Equal(partial, quote.Price); Assert.Equal(B, Assert.Single(quote.Missing).ItemId);
        Assert.Equal(0, ShopCatalog.Quote(Bundle(), new HashSet<string> { A, B }, currency).Price);
    }
    [Fact] public void TicketsAndInvalidRequestsCannotBeSpent()
    {
        Assert.Throws<ShopException>(() => ShopCatalog.Quote(Bundle(), new HashSet<string>(), "TICKETS"));
        var request = new ShopPurchaseRequest { RequestId = Guid.NewGuid(), OfferId = "bundle", Currency = "GOLDS", ExpectedPrice = 10, ExpectedItemIds = new() { A } };
        ShopCatalog.ValidateRequest(request);
        request.ExpectedItemIds.Add(A);
        Assert.Throws<ShopException>(() => ShopCatalog.ValidateRequest(request));
        request.ExpectedItemIds = new() { "not-an-id" };
        Assert.Throws<ShopException>(() => ShopCatalog.ValidateRequest(request));
    }
    [Fact] public void ReplayMustMatchOriginalOfferCurrencyPriceAndContents()
    {
        var receipt = new ShopPurchase { OfferId = "bundle", Currency = "GOLDS", TotalCost = 1360, GrantedItemIds = new() { ObjectId.Parse(B) } };
        var request = new ShopPurchaseRequest { OfferId = "bundle", Currency = "GOLDS", ExpectedPrice = 1360, ExpectedItemIds = new() { B } };
        Assert.Equal(1360, ShopService.Replay(receipt, request).TotalCost);
        request.Currency = "DIAMONDS"; Assert.Throws<ShopException>(() => ShopService.Replay(receipt, request));
        request.Currency = "GOLDS"; request.ExpectedItemIds = new() { A }; Assert.Throws<ShopException>(() => ShopService.Replay(receipt, request));
        request.ExpectedItemIds = new() { B }; request.ExpectedPrice = 1; Assert.Throws<ShopException>(() => ShopService.Replay(receipt, request));
    }
    [Fact] public void CatalogRequiresExactActiveUniqueItemMappingsAndNeverSellsDefaults()
    {
        var config = new ShopCatalogOptions { Products = new() { new() { Id = "a", Type = "BOARD_SKIN", UnityAssetKey = "Boards/Exact", Golds = 100, Diamonds = 5 } },
            Offers = new() { new() { Id = "offer", ProductIds = new() { "a" } } } };
        var item = new Item { Id = ObjectId.Parse(A), Type = "BOARD_SKIN", UnityAssetKey = "Boards/Exact", IsActive = true };
        Assert.Single(ShopCatalog.Resolve(config, new[] { item }));
        item.IsDefault = true; Assert.Empty(ShopCatalog.Resolve(config, new[] { item }));
        item.IsDefault = false; item.IsActive = false; Assert.Empty(ShopCatalog.Resolve(config, new[] { item }));
        item.IsActive = true; Assert.Empty(ShopCatalog.Resolve(config, new[] { item, item }));
        item.UnityAssetKey = "boards/exact"; Assert.Empty(ShopCatalog.Resolve(config, new[] { item }));
    }
    [Fact] public void InvalidCatalogCannotPublishMalformedPricesOrDiscounts()
    {
        var config = new ShopCatalogOptions { Products = new() { new() { Id = "a", Type = "BOARD_SKIN", Golds = -1, Diamonds = 1 } } };
        Assert.Throws<ShopException>(() => ShopCatalog.Resolve(config, Array.Empty<Item>()));
        config.Products[0].Golds = 10; config.Offers.Add(new() { Id = "bad", DiscountPercent = 100, ProductIds = new() { "a" } });
        Assert.Throws<ShopException>(() => ShopCatalog.Resolve(config, Array.Empty<Item>()));
    }
}
