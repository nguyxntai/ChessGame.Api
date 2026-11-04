using System.Diagnostics;
using ChessGame.Api.DTOs.Shop;
using ChessGame.Api.Models;
using ChessGame.Api.Services;
using ChessGame.Api.Services.Shop;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

// Dedicated loopback replica set; never reads application configuration or user secrets.
if (args.Length != 2) throw new ArgumentException("Pass <mongod.exe> <empty-fixture-directory>.");
var directory = Path.GetFullPath(args[1]);
if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new InvalidOperationException("Fixture directory must be empty.");
Directory.CreateDirectory(directory);
const int port = 29763;
var info = new ProcessStartInfo(Path.GetFullPath(args[0])) { UseShellExecute = false, CreateNoWindow = true };
foreach (var arg in new[] { "--bind_ip", "127.0.0.1", "--port", port.ToString(), "--dbpath", directory, "--replSet", "shopTests",
    "--logpath", Path.Combine(directory, "mongod.log"), "--setParameter", "enableTestCommands=1" }) info.ArgumentList.Add(arg);
using var process = Process.Start(info) ?? throw new InvalidOperationException("MongoDB fixture failed to start.");
var checks = new List<string>();
try
{
    var bootstrap = new MongoClient($"mongodb://127.0.0.1:{port}/?directConnection=true&serverSelectionTimeoutMS=1000");
    bool ready = false;
    for (int i = 0; i < 40; i++)
    {
        if (process.HasExited) throw new InvalidOperationException("Fixture exited. Port may already be in use.");
        try { await bootstrap.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1)); ready = true; break; }
        catch (TimeoutException) { await Task.Delay(100); }
    }
    if (!ready) throw new TimeoutException("Fixture did not start.");
    await bootstrap.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument { { "replSetInitiate", new BsonDocument {
        { "_id", "shopTests" }, { "members", new BsonArray { new BsonDocument { { "_id", 0 }, { "host", $"127.0.0.1:{port}" } } } } } } });
    var client = new MongoClient($"mongodb://127.0.0.1:{port}/?replicaSet=shopTests&serverSelectionTimeoutMS=20000");
    var db = client.GetDatabase("shop_verification_" + Guid.NewGuid().ToString("N"));
    var users = db.GetCollection<User>("users"); var items = db.GetCollection<Item>("items");
    var inventory = db.GetCollection<PlayerItem>("player_items");
    var ledger = db.GetCollection<CurrencyTransaction>("currency_transactions");
    var receipts = db.GetCollection<ShopPurchase>("shop_purchases");
    var a = new Item { Code = "SYNTHETIC_A", Id = ObjectId.GenerateNewId(), Name = "Fixture Set", Type = "CHESS_PIECE_SKIN", UnityAssetKey = "Fixture/A", IsActive = true };
    var b = new Item { Code = "SYNTHETIC_B", Id = ObjectId.GenerateNewId(), Name = "Fixture Board", Type = "BOARD_SKIN", UnityAssetKey = "Fixture/B", IsActive = true };
    await items.InsertManyAsync(new[] { a, b });
    var config = Options.Create(new ShopCatalogOptions {
        Products = new() { new() { Id = "a", Type = a.Type, UnityAssetKey = a.UnityAssetKey, Golds = 1800, Diamonds = 90 },
            new() { Id = "b", Type = b.Type, UnityAssetKey = b.UnityAssetKey, Golds = 1600, Diamonds = 80 } },
        Offers = new() { new() { Id = "a", ProductIds = new() { "a" } }, new() { Id = "b", ProductIds = new() { "b" } },
            new() { Id = "bundle", ProductIds = new() { "a", "b" }, DiscountPercent = 15 } } });
    ShopService Service() => new(db, client, config);
    await new InventoryService(db).EnsureIndexesAsync(); await Service().EnsureIndexesAsync();
    await db.CreateCollectionAsync("currency_transactions");
    async Task<User> User(long gold = 10000, long diamonds = 1000)
    { var u = new User { Wallet = new PlayerWallet { Golds = gold, Diamonds = diamonds, Tickets = 17 } }; await users.InsertOneAsync(u); return u; }
    ShopPurchaseRequest Request(string offer, string currency, long price, params Item[] grants) => new() {
        RequestId = Guid.NewGuid(), OfferId = offer, Currency = currency, ExpectedPrice = price, ExpectedItemIds = grants.Select(i => i.Id.ToString()).ToList() };
    void Need(bool result, string name) { if (!result) throw new Exception("FAIL " + name); checks.Add("PASS " + name); Console.WriteLine(checks[^1]); }
    async Task<long> Gold(User u) => (await users.Find(x => x.Id == u.Id).SingleAsync()).Wallet.Golds;
    async Task Expect(string code, Func<Task> action)
    { try { await action(); throw new Exception("Expected " + code); } catch (ShopException ex) { Need(ex.Code == code, code); } }

    var owner = await User(); var purchase = Request("a", "GOLDS", 1800, a);
    var receipt = await Service().PurchaseAsync(owner.Id, purchase);
    Need(await Gold(owner) == 8200 && receipt.GrantedItemIds.SequenceEqual(new[] { a.Id.ToString() }), "Gold debit and ownership committed together");
    var replay = await Service().PurchaseAsync(owner.Id, purchase);
    Need(await Gold(owner) == 8200 && replay.RequestId == receipt.RequestId && await receipts.CountDocumentsAsync(x => x.UserId == owner.Id) == 1,
        "Lost-response retry after service recreation returns one receipt");
    await Expect("ALREADY_OWNED", () => Service().PurchaseAsync(owner.Id, Request("a", "GOLDS", 1800, a)));
    var partial = await Service().PurchaseAsync(owner.Id, Request("bundle", "DIAMONDS", 68, b));
    Need(partial.Wallet.Diamonds == 932 && partial.Wallet.Golds == 8200 && partial.Wallet.Tickets == 17 && partial.GrantedItemIds.Count == 1,
        "Partial bundle debits Diamonds only for missing board; Tickets preserved");
    await new InventoryService(db).EquipItemAsync(owner.Id, b.Id);
    Need((await new InventoryService(db).GetUserInventoryAsync(owner.Id)).Any(i => i.ItemId == b.Id.ToString() && i.IsEquipped), "Shop grants work in existing Inventory and Equip service");
    var poor = await User(0, 0);
    await Expect("INSUFFICIENT_CURRENCY", () => Service().PurchaseAsync(poor.Id, Request("a", "GOLDS", 1800, a)));
    Need(await inventory.CountDocumentsAsync(x => x.UserId == poor.Id) == 0 && await Gold(poor) == 0, "Insufficient funds changes neither wallet nor ownership");
    await Expect("QUOTE_CHANGED", () => Service().PurchaseAsync(poor.Id, Request("a", "GOLDS", 1, a)));
    await Expect("INVALID_CURRENCY", () => Service().PurchaseAsync(poor.Id, Request("a", "TICKETS", 1800, a)));
    var disabled = await User(); await users.UpdateOneAsync(x => x.Id == disabled.Id, Builders<User>.Update.Set(x => x.IsActive, false));
    await Expect("ACCOUNT_DISABLED", () => Service().PurchaseAsync(disabled.Id, Request("a", "GOLDS", 1800, a)));
    var concurrent = await User(); var duplicate = Request("bundle", "GOLDS", 2890, a, b);
    var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Service().PurchaseAsync(concurrent.Id, duplicate)));
    Need(await Gold(concurrent) == 7110 && await inventory.CountDocumentsAsync(x => x.UserId == concurrent.Id) == 2 &&
        await ledger.CountDocumentsAsync(x => x.UserId == concurrent.Id) == 1 && outcomes.All(o => o.RequestId == duplicate.RequestId.ToString("D")),
        "Six concurrent retries produce one charge, one ledger entry and two grants");
    var race = await User();
    async Task<bool> RaceBuy() { try { await Service().PurchaseAsync(race.Id, Request("a", "GOLDS", 1800, a)); return true; } catch (ShopException e) when (e.Code == "ALREADY_OWNED" || e.Code == "PURCHASE_CONFLICT") { return false; } }
    var raceResults = await Task.WhenAll(RaceBuy(), RaceBuy());
    Need(raceResults.Count(x => x) == 1 && await Gold(race) == 8200, "Different requests for the same item cannot double charge");
    var rollback = await User();
    await client.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument { { "configureFailPoint", "failCommand" }, { "mode", new BsonDocument("times", 1) },
        { "data", new BsonDocument { { "failCommands", new BsonArray { "insert" } }, { "errorCode", 121 } } } });
    try { await Service().PurchaseAsync(rollback.Id, Request("a", "GOLDS", 1800, a)); throw new Exception("Expected injected insert failure"); }
    catch (MongoException) { }
    Need(await Gold(rollback) == 10000 && await inventory.CountDocumentsAsync(x => x.UserId == rollback.Id) == 0 &&
        await ledger.CountDocumentsAsync(x => x.UserId == rollback.Id) == 0 && await receipts.CountDocumentsAsync(x => x.UserId == rollback.Id) == 0,
        "Injected grant failure rolls back debit, inventory, ledger and receipt");
    File.WriteAllLines(Path.Combine(directory, "checks.txt"), checks);
    Console.WriteLine("SHOP_INTEGRATION_PASS " + checks.Count);
}
finally
{
    if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
}
