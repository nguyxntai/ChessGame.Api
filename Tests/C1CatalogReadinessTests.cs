using System.Reflection;
using ChessGame.Api.Models;
using ChessGame.Api.Services;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace ChessGame.Api.Tests;

public sealed class C1CatalogReadinessTests
{
    [Theory]
    [InlineData("valid", true)][InlineData("unrelated-catalog-row", true)]
    [InlineData("unknown-type", true)][InlineData("empty-type", true)]
    [InlineData("pity", false)][InlineData("pity-order", false)][InlineData("cost-count", false)]
    [InlineData("cost-amount", false)][InlineData("cost-currency", false)][InlineData("cost-duplicate", false)]
    [InlineData("pool-empty", false)][InlineData("pool-duplicate", false)][InlineData("pool-weight", false)]
    [InlineData("pool-id", false)][InlineData("missing-item", false)][InlineData("inactive", false)]
    [InlineData("default", false)][InlineData("rarity", false)][InlineData("no-epic", false)]
    [InlineData("no-legendary", false)][InlineData("weight-overflow", false)]
    [InlineData("duplicate-missing", false)][InlineData("duplicate-negative", false)][InlineData("duplicate-currency", false)]
    public async Task DiagnosticMatchesExistingNativePoolValidatorWithoutWrites(string damage, bool valid)
    {
        var f = new C1ReadStore();
        switch (damage)
        {
            case "unrelated-catalog-row": f.Items.Add(new Item { Id = ObjectId.GenerateNewId(), Type = "LEGACY_UNREGISTERED", Rarity = "INVALID" }); break;
            case "unknown-type": f.Items[0].Type = "UNKNOWN_CHESS_BOARD"; break;
            case "empty-type": f.Items[0].Type = ""; break;
            case "pity": f.Banner.PityGroup = " "; break;
            case "pity-order": f.Banner.Pity.Epic = 2; break;
            case "cost-count": f.Banner.Costs[0].RollCount = 2; break;
            case "cost-amount": f.Banner.Costs[0].Amount = 0; break;
            case "cost-currency": f.Banner.Costs[0].Currency = "GOLD"; break;
            case "cost-duplicate": f.Banner.Costs.Add(f.Banner.Costs[0]); break;
            case "pool-empty": f.Banner.Pool.Clear(); break;
            case "pool-duplicate": f.Banner.Pool.Add(f.Banner.Pool[0]); break;
            case "pool-weight": f.Banner.Pool[0].Weight = 0; break;
            case "pool-id": f.Banner.Pool[0].ItemId = ObjectId.Empty; break;
            case "missing-item": f.Items.RemoveAt(0); break;
            case "inactive": f.Items[0].IsActive = false; break;
            case "default": f.Items[0].IsDefault = true; break;
            case "rarity": f.Items[0].Rarity = "LEGEND"; break;
            case "no-epic": f.Items[0].Rarity = "LEGENDARY"; break;
            case "no-legendary": f.Items[1].Rarity = "EPIC"; break;
            case "weight-overflow": f.Banner.Pool.ForEach(p => p.Weight = int.MaxValue); break;
            case "duplicate-missing": f.Banner.DuplicateRewards.Remove("COMMON"); break;
            case "duplicate-negative": f.Banner.DuplicateRewards["COMMON"].Amount = -1; break;
            case "duplicate-currency": f.Banner.DuplicateRewards["COMMON"].Currency = "GOLD"; break;
        }
        var before = f.State(); var result = ItemCatalogReadiness.Evaluate(f.Banner, f.Items, true);
        var task = (Task)typeof(GachaService).GetMethod("LoadAndValidatePoolAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(f.Service, new object?[] { f.Banner, null, CancellationToken.None })!;
        if (valid) { await task; Assert.Equal(ItemCatalogReadiness.Unsupported, result.Status); }
        else if (damage == "weight-overflow")
        {
            // Existing rates.Total overflows outside the service's CalculateRates catch.
            // Record the native rejection; C1 does not alter that economic/config path.
            await Assert.ThrowsAsync<OverflowException>(async () => await task);
            Assert.Equal(ItemCatalogReadiness.Invalid, result.Status);
        }
        else { var error = await Assert.ThrowsAsync<GachaException>(async () => await task);
            Assert.Equal("BANNER_MISCONFIGURED", error.Code); Assert.Equal(ItemCatalogReadiness.Invalid, result.Status); }
        Assert.Equal(before, f.State()); Assert.DoesNotContain(f.Calls, call => call.Contains("write") || call.Contains("session"));
        C1Fixture.Save("catalog-" + damage + ".json", new { result, rawTypeFrequency = f.Items.GroupBy(i => i.Type).Select(g => new { rawType = g.Key, count = g.Count() }), unchanged = before == f.State(), nativeValidatorAccepted = valid, nativeRejection = valid ? null : damage == "weight-overflow" ? "OverflowException (pre-existing total sum boundary)" : "GachaException:BANNER_MISCONFIGURED", calls = f.Calls });
    }

    [Fact]
    public void AllFourReadinessOutcomesAreExplicitAndNeverRemovePoolEntries()
    {
        var f = new C1ReadStore(); var before = f.State();
        var approved = ItemCatalogReadiness.Evaluate(f.Banner, f.Items, true, C1Fixture.Policy());
        Assert.Equal(ItemCatalogReadiness.Presentable, approved.Status); Assert.Equal(2, approved.Entries.Count);
        Assert.All(approved.Entries, e => Assert.Equal("UNVERIFIED", e.PresentationAuthority!.UnityAssetAvailability));
        Assert.Equal(ItemCatalogReadiness.Unsupported, ItemCatalogReadiness.Evaluate(f.Banner, f.Items, true).Status);
        Assert.Equal(ItemCatalogReadiness.Unknown, ItemCatalogReadiness.Evaluate(f.Banner, f.Items, false).Status);
        Assert.Equal(ItemCatalogReadiness.Unknown, ItemCatalogReadiness.Evaluate(null, f.Items, true).Status);
        Assert.Equal(ItemCatalogReadiness.Unknown, ItemCatalogReadiness.Evaluate(f.Banner, null, true).Status);
        Assert.Equal(before, f.State()); Assert.Empty(f.Calls);
        f.Banner.Pool[0].Weight = -1;
        Assert.Equal(ItemCatalogReadiness.Invalid, ItemCatalogReadiness.Evaluate(f.Banner, f.Items, true).Status);
        Assert.Equal(2, f.Banner.Pool.Count); C1Fixture.Save("catalog-approved-fixture.json", approved);
    }

    [Fact]
    public async Task ExistingCatalogInventoryAndBannerReadersUseSameClosedPolicyAndKeepRawFields()
    {
        var f = new C1ReadStore(); var before = f.State();
        var catalog = await f.Inventory.GetCatalogAsync();
        var inventory = await f.Inventory.GetUserInventoryAsync(C1ReadStore.Owner);
        var banner = await f.Service.GetBannerAsync("fixture");
        Assert.Equal(2, catalog.Count); Assert.Equal(2, inventory.Count); Assert.Equal(2, banner.PoolItems.Count);
        foreach (var item in catalog)
        {
            Assert.Equal("FIXTURE_PIECE_TYPE", item.Type); Assert.Equal("fixture/shared", item.UnityAssetKey);
            Assert.Equal(item.PresentationAuthority, inventory.Single(r => r.ItemId == item.ItemId).PresentationAuthority);
            Assert.Equal(item.PresentationAuthority, banner.PoolItems.Single(r => r.ItemId == item.ItemId).PresentationAuthority);
            Assert.Equal(ItemPresentationStatus.UnknownItemType, item.PresentationAuthority.ResolutionStatus);
            Assert.Null(item.PresentationAuthority.ApprovedUnityAssetKey);
        }
        Assert.Equal(before, f.State());
        C1Fixture.Save("catalog-inventory-banner-json.json", new { catalog, inventory, banner });
    }

    [Fact]
    public void ExistingAuthorizedFakeCatalogInventoryIsNotProductionTaxonomyEvidence()
    {
        var f = new GachaFakeStore(); var rows = f.Items.GroupBy(i => i.Type).Select(g => new { rawType = g.Key, count = g.Count() }).ToArray();
        Assert.Equal(2, f.Items.Count); Assert.Contains(f.Items, i => i.Type == "RAW:unapproved"); Assert.Contains(f.Items, i => i.Type == "");
        Assert.All(f.Items, i => Assert.Null(ItemPresentationPolicy.Current.ResolveLive(i.Id.ToString(), i.Type, i.UnityAssetKey).ApprovedUnityAssetKey));
        C1Fixture.Save("existing-fixture-type-inventory.json", new { scope = "GachaFakeStore constructor; synthetic, not live catalog", rows });
    }
}

// Strict driver fakes for existing read paths; any transaction/write attempt is unconfigured and fails.
internal sealed class C1ReadStore
{
    internal static readonly ObjectId Owner = ObjectId.Parse("222222222222222222222201");
    internal readonly List<Item> Items = new() {
        new Item { Id = C1Fixture.First, Code = "fixture-epic", Name = "Fixture Epic", Type = "FIXTURE_PIECE_TYPE", UnityAssetKey = "fixture/shared", Rarity = "EPIC", IsActive = true },
        new Item { Id = C1Fixture.Second, Code = "fixture-legendary", Name = "Fixture Legendary", Type = "FIXTURE_PIECE_TYPE", UnityAssetKey = "fixture/shared", Rarity = "LEGENDARY", IsActive = true } };
    internal readonly GachaBanner Banner;
    internal readonly List<string> Calls = new();
    internal readonly InventoryService Inventory;
    internal readonly GachaService Service;
    internal C1ReadStore()
    {
        Banner = new GachaBanner { Code = "FIXTURE", IsActive = true, PityGroup = "FIXTURE",
            Pity = new GachaPityLimits { Epic = 1, Legendary = 1 },
            Costs = new() { new GachaCost { RollCount = 1, Amount = 1, Currency = "TICKETS" }, new GachaCost { RollCount = 10, Amount = 10, Currency = " tickets " } },
            Pool = Items.Select(i => new GachaPoolEntry { ItemId = i.Id }).ToList(),
            DuplicateRewards = new[] { "COMMON", "RARE", "EPIC", "LEGENDARY" }.ToDictionary(r => r, _ => new GachaDuplicateReward { Currency = "GOLDS", Amount = 5 }) };
        var db = new Mock<IMongoDatabase>(MockBehavior.Strict); var client = new Mock<IMongoClient>(MockBehavior.Strict);
        var items = Collection<Item>(db, "items"); var banners = Collection<GachaBanner>(db, "gacha_banners");
        var users = Collection<User>(db, "users"); var owned = Collection<PlayerItem>(db, "player_items");
        Collection<GachaRoll>(db, "gacha_rolls"); Collection<GachaPity>(db, "player_gacha_pity"); Collection<CurrencyTransaction>(db, "currency_transactions");
        items.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<Item>>(), It.IsAny<FindOptions<Item, Item>>(), It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<Item> filter, FindOptions<Item, Item> _, CancellationToken _) => {
                Calls.Add("items.read"); var doc = filter.Render(new RenderArgs<Item>(BsonSerializer.LookupSerializer<Item>(), BsonSerializer.SerializerRegistry));
                IEnumerable<Item> rows = Items;
                if (doc.TryGetValue("_id", out var ids)) { var included = ids["$in"].AsBsonArray.Select(v => v.AsObjectId).ToHashSet(); rows = rows.Where(i => included.Contains(i.Id)); }
                if (doc.TryGetValue("isActive", out var active)) rows = rows.Where(i => i.IsActive == active.AsBoolean);
                return Task.FromResult(Cursor(rows.ToArray())); });
        banners.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<GachaBanner>>(), It.IsAny<FindOptions<GachaBanner, GachaBanner>>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Calls.Add("banner.read"); return Task.FromResult(Cursor(new[] { Banner })); });
        users.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<User>>(), It.IsAny<FindOptions<User, User>>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Calls.Add("user.read"); return Task.FromResult(Cursor(new[] { new User { Id = Owner } })); });
        owned.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<PlayerItem>>(), It.IsAny<FindOptions<PlayerItem, PlayerItem>>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Calls.Add("inventory.read"); return Task.FromResult(Cursor(Items.Select(i => new PlayerItem { Id = ObjectId.GenerateNewId(), UserId = Owner, ItemId = i.Id }).ToArray())); });
        Inventory = new InventoryService(db.Object); Service = new GachaService(db.Object, client.Object, Inventory);
    }
    internal string State() => Banner.ToJson() + "\n" + string.Join("\n", Items.Select(i => i.ToJson()));
    private static Mock<IMongoCollection<T>> Collection<T>(Mock<IMongoDatabase> db, string name)
    {
        var result = new Mock<IMongoCollection<T>>(MockBehavior.Strict); db.Setup(d => d.GetCollection<T>(name, null)).Returns(result.Object); return result;
    }
    private static IAsyncCursor<T> Cursor<T>(T[] rows)
    {
        var c = new Mock<IAsyncCursor<T>>(MockBehavior.Strict); c.SetupGet(x => x.Current).Returns(rows);
        c.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false); c.Setup(x => x.Dispose()); return c.Object;
    }
}
