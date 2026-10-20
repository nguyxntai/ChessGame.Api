using System.Text.Json;
using ChessGame.Api.DTOs.Gacha;
using ChessGame.Api.Models;
using ChessGame.Api.Services;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace ChessGame.Api.Tests;

// Exercises the actual service + inventory code through strict driver fakes, not a real MongoDB.
// Operation counts prove service orchestration only, not server isolation/atomicity/failover.
public sealed class GachaReceiptFlowTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(10, false)]
    [InlineData(1, true)]
    [InlineData(10, true)]
    public async Task FreshRollCapturesSameSessionItemsWithNoExtraEconomicOperations(int count, bool owned)
    {
        var f = new GachaFakeStore(owned);
        var response = await f.Service.RollAsync(f.User.Id, f.Request(count));
        Assert.Equal(count, response.Results.Count);
        var snapshot = response.PresentationSnapshot!;
        Assert.Equal(count, snapshot.Rewards.Count);
        Assert.All(snapshot.Rewards, r =>
        {
            Assert.Equal(f.Legendary.Id.ToString(), r.ItemId);
            Assert.Equal(f.Legendary.Code, r.ItemCode);
            Assert.Equal(f.Legendary.Type, r.RawItemType);
            Assert.Equal(f.Legendary.UnityAssetKey, r.UnityAssetKey);
        });
        Assert.Equal(Enumerable.Range(0, count), snapshot.Rewards.Select(r => r.SlotIndex));
        Assert.Equal(100 - count, response.Wallet.Tickets);
        var duplicates = owned ? count : count - 1;
        Assert.Equal(duplicates * GachaFakeStore.Bonus, response.Wallet.Golds);
        Assert.Equal(duplicates, response.Results.Count(r => r.IsDuplicate));
        Assert.All(response.Results.Where(r => r.IsDuplicate),
            r => Assert.Equal(GachaFakeStore.Bonus, r.DuplicateReward!.Amount));
        Assert.Equal(duplicates + 1, f.Ledger.Count);
        Assert.Equal(new[] { -count * 1L }.Concat(Enumerable.Repeat(GachaFakeStore.Bonus, duplicates)), f.Ledger.Select(l => l.Amount));
        Assert.Equal(0, response.Pity.Epic.Current);
        Assert.Equal(0, response.Pity.Legendary.Current);
        Assert.Equal(1, f.Calls.Count(c => c == "items.find.session"));
        Assert.Equal(1, f.Calls.Count(c => c == "wallet.debit"));
        Assert.Equal(duplicates > 0 ? 1 : 0, f.Calls.Count(c => c == "wallet.credit"));
        Assert.Equal(count, f.Calls.Count(c => c == "inventory.find"));
        Assert.Equal(owned ? 0 : 1, f.Calls.Count(c => c == "inventory.insert"));
        Assert.Equal(1, f.Calls.Count(c => c == "pity.insert"));
        Assert.Equal(1, f.Calls.Count(c => c == "roll.insert"));
        Assert.Equal(1, f.Calls.Count(c => c == "ledger.insert"));
        Assert.Equal(1, f.Calls.Count(c => c == "commit"));
        Assert.DoesNotContain("abort", f.Calls);
        Assert.NotNull(f.Stored!.PresentationSnapshot);
        Assert.Equal(f.User.Id, f.LastRollFilter!["userId"].AsObjectId);
        Assert.Equal(response.RequestId, f.LastRollFilter["requestId"].AsString);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public async Task ReplayAndHistoryReuseStoredSnapshotAfterCatalogMutationOrRemoval(int count)
    {
        var f = new GachaFakeStore();
        var request = f.Request(count);
        var initial = await f.Service.RollAsync(f.User.Id, request);
        // Existing BSON dates have millisecond precision; do not change production timing
        // or mistake its pre-existing sub-millisecond response difference for metadata drift.
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(new DateTimeOffset(initial.CreatedAt).ToUnixTimeMilliseconds()).UtcDateTime,
            f.Stored!.CreatedAt);
        initial.CreatedAt = f.Stored.CreatedAt;
        var json = JsonSerializer.Serialize(initial, GachaSnapshotTests.WebJson);
        f.Legendary.Type = "changed-category"; f.Legendary.UnityAssetKey = "changed-key";
        f.Legendary.Code = "changed-code"; f.Items.Clear();
        f.Calls.Clear();
        var replay = await f.Service.RollAsync(f.User.Id, request);
        Assert.Equal(json, JsonSerializer.Serialize(replay, GachaSnapshotTests.WebJson));
        Assert.Equal(new[] { "roll.find" }, f.Calls);
        f.Calls.Clear();
        var history = await f.Service.GetHistoryAsync(f.User.Id, 1, 20);
        Assert.Equal(json, JsonSerializer.Serialize(Assert.Single(history.Items), GachaSnapshotTests.WebJson));
        Assert.Equal(f.User.Id, f.LastRollFilter!["userId"].AsObjectId);
        Assert.Equal(new[] { "user.find", "roll.count", "roll.find" }, f.Calls);
        var read = JsonSerializer.Deserialize<GachaHistoryResponse>(
            JsonSerializer.Serialize(history, GachaSnapshotTests.WebJson), GachaSnapshotTests.WebJson)!;
        Assert.Equal(json, JsonSerializer.Serialize(read.Items[0], GachaSnapshotTests.WebJson));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData(" UNKNOWN_BOARD_CHESS ", " ")]
    public async Task MissingOrUnapprovedMetadataDoesNotChangeGrantOutcome(string? type, string? key)
    {
        var f = new GachaFakeStore();
        f.Legendary.Type = type!; f.Legendary.UnityAssetKey = key!;
        var response = await f.Service.RollAsync(f.User.Id, f.Request(1));
        Assert.Single(response.Results);
        Assert.False(response.Results[0].IsDuplicate);
        Assert.Equal(type, response.PresentationSnapshot!.Rewards[0].RawItemType);
        Assert.Equal(key, response.PresentationSnapshot.Rewards[0].UnityAssetKey);
        Assert.Null(response.PresentationSnapshot.CatalogVersion);
        Assert.Equal(1, f.Calls.Count(c => c == "commit"));
    }

    [Fact]
    public async Task MissingPoolItemRetainsExistingPreCommitFailure_NoMetadataFallback()
    {
        var f = new GachaFakeStore(); f.Items.Remove(f.Legendary);
        var ex = await Assert.ThrowsAsync<GachaException>(() => f.Service.RollAsync(f.User.Id, f.Request(1)));
        Assert.Equal("BANNER_MISCONFIGURED", ex.Code);
        Assert.Null(f.Stored);
        Assert.DoesNotContain("wallet.debit", f.Calls);
        Assert.DoesNotContain("inventory.insert", f.Calls);
        Assert.DoesNotContain("commit", f.Calls);
        Assert.Contains("abort", f.Calls);
    }

    [Fact]
    public async Task ConflictingRequestCannotGrantOrRewriteSnapshot()
    {
        var f = new GachaFakeStore(); var request = f.Request(1);
        await f.Service.RollAsync(f.User.Id, request); f.Calls.Clear();
        request.Count = 10;
        var ex = await Assert.ThrowsAsync<GachaException>(() => f.Service.RollAsync(f.User.Id, request));
        Assert.Equal("REQUEST_ID_CONFLICT", ex.Code);
        Assert.Equal(new[] { "roll.find" }, f.Calls);
        Assert.Single(f.Stored!.PresentationSnapshot!.Rewards);
    }

    [Fact]
    public async Task HistoricalAndMalformedSnapshotReplayNeverStartsNewTransaction()
    {
        var f = new GachaFakeStore(); var request = f.Request(1);
        await f.Service.RollAsync(f.User.Id, request);
        foreach (var value in new BsonValue?[] { null, new BsonDocument("schemaVersion", 999), new BsonString("bad") })
        {
            var doc = f.Stored!.ToBsonDocument();
            if (value is null) doc.Remove("presentationSnapshot"); else doc["presentationSnapshot"] = value;
            f.Stored = BsonSerializer.Deserialize<GachaRoll>(doc); f.Calls.Clear();
            var response = await f.Service.RollAsync(f.User.Id, request);
            Assert.Null(response.PresentationSnapshot);
            Assert.Equal(new[] { "roll.find" }, f.Calls);
        }
    }

    [Fact]
    public async Task AnotherSubjectCannotReplayOrReadHistoryOfStoredReceipt()
    {
        var f = new GachaFakeStore(); var request = f.Request(1);
        await f.Service.RollAsync(f.User.Id, request);
        var ownerReceipt = f.Stored!.ToBson();
        var other = ObjectId.GenerateNewId(); f.Calls.Clear();
        var error = await Assert.ThrowsAsync<GachaException>(() => f.Service.RollAsync(other, request));
        Assert.Equal("USER_NOT_FOUND", error.Code);
        Assert.Equal(other, f.LastRollFilter!["userId"].AsObjectId);
        Assert.DoesNotContain("wallet.debit", f.Calls);
        Assert.DoesNotContain("inventory.insert", f.Calls);
        Assert.DoesNotContain("commit", f.Calls);
        Assert.Equal(ownerReceipt, f.Stored.ToBson());
        error = await Assert.ThrowsAsync<GachaException>(() => f.Service.GetHistoryAsync(other, 1, 20));
        Assert.Equal("USER_NOT_FOUND", error.Code);
    }
}

internal sealed class GachaFakeStore
{
    internal const long Bonus = 5_000_000_000;
    internal readonly List<string> Calls = new();
    internal readonly User User = new() { Wallet = new PlayerWallet { Golds = 0, Tickets = 100 } };
    internal readonly Item Legendary = new() { Id = ObjectId.GenerateNewId(), Code = "mock-legend", Name = "Mock", Rarity = "LEGENDARY", Type = "RAW:unapproved", UnityAssetKey = "same/key", IsActive = true };
    internal readonly List<Item> Items;
    internal GachaRoll? Stored;
    internal List<CurrencyTransaction> Ledger = new();
    internal BsonDocument? LastRollFilter;
    internal readonly GachaService Service;
    private GachaRoll? pending;

    internal GachaRollRequest Request(int count) => new() { BannerCode = " test ", Count = count, RequestId = Guid.NewGuid() };

    internal GachaFakeStore(bool alreadyOwned = false)
    {
        Items = new() { Legendary, new Item { Id = ObjectId.GenerateNewId(), Code = "mock-epic", Rarity = "EPIC", IsActive = true } };
        var banner = new GachaBanner
        {
            Code = "TEST", IsActive = true, PityGroup = "TEST", Pity = new GachaPityLimits { Epic = 1, Legendary = 1 },
            Pool = Items.Select(i => new GachaPoolEntry { ItemId = i.Id, Weight = 1 }).ToList(),
            Costs = new() { new GachaCost { RollCount = 1, Currency = "TICKETS", Amount = 1 }, new GachaCost { RollCount = 10, Currency = "TICKETS", Amount = 10 } },
            DuplicateRewards = new[] { "COMMON", "RARE", "EPIC", "LEGENDARY" }.ToDictionary(r => r, r => new GachaDuplicateReward { Currency = "GOLDS", Amount = Bonus })
        };
        var db = new Mock<IMongoDatabase>(MockBehavior.Strict);
        var client = new Mock<IMongoClient>(MockBehavior.Strict);
        var session = new Mock<IClientSessionHandle>(MockBehavior.Strict);
        bool active = false, owned = alreadyOwned;
        session.Setup(s => s.StartTransaction(It.IsAny<TransactionOptions>())).Callback(() => { active = true; Calls.Add("begin"); });
        session.SetupGet(s => s.IsInTransaction).Returns(() => active);
        session.Setup(s => s.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns(() => { Calls.Add("commit"); active = false; Stored = pending; return Task.CompletedTask; });
        session.Setup(s => s.AbortTransactionAsync(It.IsAny<CancellationToken>())).Returns(() => { Calls.Add("abort"); active = false; return Task.CompletedTask; });
        session.Setup(s => s.Dispose()).Callback(() => Calls.Add("dispose"));
        client.Setup(c => c.StartSessionAsync(It.IsAny<ClientSessionOptions>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Calls.Add("session"); return Task.FromResult(session.Object); });
        var rolls = Collection<GachaRoll>(db, "gacha_rolls");
        var users = Collection<User>(db, "users");
        var items = Collection<Item>(db, "items");
        var banners = Collection<GachaBanner>(db, "gacha_banners");
        var pity = Collection<GachaPity>(db, "player_gacha_pity");
        var inventory = Collection<PlayerItem>(db, "player_items");
        var ledger = Collection<CurrencyTransaction>(db, "currency_transactions");
        rolls.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<GachaRoll>>(), It.IsAny<FindOptions<GachaRoll, GachaRoll>>(), It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<GachaRoll> filter, FindOptions<GachaRoll, GachaRoll> _, CancellationToken _) =>
            { Calls.Add("roll.find"); LastRollFilter = Render(filter);
                var matches = Stored is not null && LastRollFilter["userId"].AsObjectId == Stored.UserId
                    && (!LastRollFilter.TryGetValue("requestId", out var requestId) || requestId.AsString == Stored.RequestId);
                return Task.FromResult(Cursor(matches ? new[] { Stored! } : Array.Empty<GachaRoll>())); });
        rolls.Setup(c => c.FindAsync(session.Object, It.IsAny<FilterDefinition<GachaRoll>>(), It.IsAny<FindOptions<GachaRoll, GachaRoll>>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Calls.Add("roll.find.session"); return Task.FromResult(Cursor(Array.Empty<GachaRoll>())); });
        rolls.Setup(c => c.CountDocumentsAsync(It.IsAny<FilterDefinition<GachaRoll>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Calls.Add("roll.count"); return Task.FromResult(Stored is null ? 0L : 1L); });
        rolls.Setup(c => c.InsertOneAsync(session.Object, It.IsAny<GachaRoll>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Returns((IClientSessionHandle _, GachaRoll roll, InsertOneOptions _, CancellationToken _) =>
            { Calls.Add("roll.insert"); pending = BsonSerializer.Deserialize<GachaRoll>(roll.ToBson()); return Task.CompletedTask; });
        users.Setup(c => c.FindAsync(session.Object, It.IsAny<FilterDefinition<User>>(), It.IsAny<FindOptions<User, User>>(), It.IsAny<CancellationToken>()))
            .Returns((IClientSessionHandle _, FilterDefinition<User> filter, FindOptions<User, User> _, CancellationToken _) =>
            { Calls.Add("user.find.session"); return Task.FromResult(Cursor(Render(filter)["_id"].AsObjectId == User.Id ? new[] { User } : Array.Empty<User>())); });
        users.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<User>>(), It.IsAny<FindOptions<User, User>>(), It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<User> filter, FindOptions<User, User> _, CancellationToken _) =>
            { Calls.Add("user.find"); return Task.FromResult(Cursor(Render(filter)["_id"].AsObjectId == User.Id ? new[] { User } : Array.Empty<User>())); });
        users.Setup(c => c.FindOneAndUpdateAsync(session.Object, It.IsAny<FilterDefinition<User>>(), It.IsAny<UpdateDefinition<User>>(), It.IsAny<FindOneAndUpdateOptions<User, User>>(), It.IsAny<CancellationToken>()))
            .Returns((IClientSessionHandle _, FilterDefinition<User> _, UpdateDefinition<User> update, FindOneAndUpdateOptions<User, User> _, CancellationToken _) =>
            { Calls.Add("wallet.debit"); var inc = update.Render(new RenderArgs<User>(BsonSerializer.LookupSerializer<User>(), BsonSerializer.SerializerRegistry)); User.Wallet.Tickets += inc["$inc"]["wallet.tickets"].ToInt64(); return Task.FromResult(User); });
        users.Setup(c => c.UpdateOneAsync(session.Object, It.IsAny<FilterDefinition<User>>(), It.IsAny<UpdateDefinition<User>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Calls.Add("wallet.credit"); return Task.FromResult<UpdateResult>(new UpdateResult.Acknowledged(1, 1, null)); });
        items.Setup(c => c.FindAsync(session.Object, It.IsAny<FilterDefinition<Item>>(), It.IsAny<FindOptions<Item, Item>>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Calls.Add("items.find.session"); return Task.FromResult(Cursor(Items)); });
        banners.Setup(c => c.FindAsync(session.Object, It.IsAny<FilterDefinition<GachaBanner>>(), It.IsAny<FindOptions<GachaBanner, GachaBanner>>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Calls.Add("banner.find.session"); return Task.FromResult(Cursor(new[] { banner })); });
        pity.Setup(c => c.FindAsync(session.Object, It.IsAny<FilterDefinition<GachaPity>>(), It.IsAny<FindOptions<GachaPity, GachaPity>>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Calls.Add("pity.find.session"); return Task.FromResult(Cursor(Array.Empty<GachaPity>())); });
        pity.Setup(c => c.InsertOneAsync(session.Object, It.IsAny<GachaPity>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Calls.Add("pity.insert"); return Task.CompletedTask; });
        inventory.Setup(c => c.FindAsync(session.Object, It.IsAny<FilterDefinition<PlayerItem>>(), It.IsAny<FindOptions<PlayerItem, BsonDocument>>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Calls.Add("inventory.find"); return Task.FromResult(Cursor(owned ? new[] { new BsonDocument("_id", ObjectId.GenerateNewId()) } : Array.Empty<BsonDocument>())); });
        inventory.Setup(c => c.InsertOneAsync(session.Object, It.IsAny<PlayerItem>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Calls.Add("inventory.insert"); owned = true; return Task.CompletedTask; });
        ledger.Setup(c => c.InsertManyAsync(session.Object, It.IsAny<IEnumerable<CurrencyTransaction>>(), It.IsAny<InsertManyOptions>(), It.IsAny<CancellationToken>()))
            .Returns((IClientSessionHandle _, IEnumerable<CurrencyTransaction> rows, InsertManyOptions _, CancellationToken _) =>
            { Calls.Add("ledger.insert"); Ledger = rows.ToList(); return Task.CompletedTask; });
        Service = new GachaService(db.Object, client.Object, new InventoryService(db.Object));
    }

    private static Mock<IMongoCollection<T>> Collection<T>(Mock<IMongoDatabase> db, string name)
    {
        var collection = new Mock<IMongoCollection<T>>(MockBehavior.Strict);
        db.Setup(d => d.GetCollection<T>(name, It.IsAny<MongoCollectionSettings>())).Returns(collection.Object);
        return collection;
    }
    private static BsonDocument Render<T>(FilterDefinition<T> filter) =>
        filter.Render(new RenderArgs<T>(BsonSerializer.LookupSerializer<T>(), BsonSerializer.SerializerRegistry));
    private static IAsyncCursor<T> Cursor<T>(IEnumerable<T> rows)
    {
        var cursor = new Mock<IAsyncCursor<T>>(); cursor.SetupGet(c => c.Current).Returns(rows);
        cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
        cursor.Setup(c => c.Dispose()); return cursor.Object;
    }
}
