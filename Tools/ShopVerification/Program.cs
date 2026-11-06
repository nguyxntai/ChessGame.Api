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
    // Actual collab configuration, isolated database and synthetic wallets only.
    await KingdomRushCatalog.EnsureItemsAsync(db);
    await KingdomRushCatalog.EnsureItemsAsync(db);
    var krItems = await items.Find(i => i.UnityAssetKey == KingdomRushCatalog.AssetKey).ToListAsync();
    Need(krItems.Count == 3 && krItems.All(i => i.ShopExclusive && !i.IsDefault), "Collab installer is idempotent: exactly three shop-exclusive products");
    using var configJson = System.Text.Json.JsonDocument.Parse(File.ReadAllText("shop-catalog.json"));
    var krOptions = System.Text.Json.JsonSerializer.Deserialize<ShopCatalogOptions>(configJson.RootElement.GetProperty("ShopCatalog").GetRawText())!;
    var krShop = new ShopService(db, client, Options.Create(krOptions));
    var offers = (await krShop.GetCatalogAsync()).Offers.Where(o => o.OfferId.StartsWith("kingdom-rush-", StringComparison.Ordinal)).ToList();
    var standalone = offers.Where(o => !o.IsBundle).ToList();
    var complete = offers.Single(o => o.OfferId == "kingdom-rush-complete");
    Need(offers.Count == 4 && standalone.Count == 3 && standalone.All(o => o.DiscountPercent == 0), "Three separate full-price collab offers plus complete bundle; no basic-background sale");
    Need(complete.IsBundle && complete.DiscountPercent == 25 && complete.Items.Count == 3 &&
        complete.Items.Select(i => i.ItemId).ToHashSet().SetEquals(krItems.Select(i => i.Id.ToString())), "Complete collab bundle contains exactly the three products with 25 percent discount");
    var unowned = new HashSet<string>();
    Need(complete.Items.Sum(i => i.Golds) == 120000 && complete.Items.Sum(i => i.Diamonds) == 6000 &&
        ShopCatalog.Quote(complete, unowned, "GOLDS").Price == 90000 && ShopCatalog.Quote(complete, unowned, "DIAMONDS").Price == 4500,
        "Combined 120000 Gold / 6000 Diamonds becomes 90000 Gold / 4500 Diamonds");
    Need(offers.SelectMany(o => o.Items).All(i => i.Golds > 6000 && i.Diamonds > 300), "Every collab product costs more than the previous most expensive skin");
    var collector = await User(500000, 25000);
    var invService = new InventoryService(db);
    var bg = krItems.Single(i => i.Type == CosmeticLoadoutPolicy.BackgroundType);
    try { await invService.EquipBackgroundAsync(collector.Id, bg.Id.ToString()); throw new Exception("Unowned background equipped"); }
    catch (InvalidOperationException e) { Need(e.Message == "ITEM_NOT_OWNED", "Unowned paid background cannot be equipped"); }
    foreach (var offer in standalone)
    {
        var product = offer.Items.Single();
        var req = new ShopPurchaseRequest { RequestId = Guid.NewGuid(), OfferId = offer.OfferId, Currency = "GOLDS", ExpectedPrice = product.Golds, ExpectedItemIds = new() { product.ItemId } };
        await krShop.PurchaseAsync(collector.Id, req); await krShop.PurchaseAsync(collector.Id, req);
    }
    Need(await Gold(collector) == 500000 - standalone.Sum(o => o.Items.Single().Golds) &&
        await inventory.CountDocumentsAsync(i => i.UserId == collector.Id) == 3, "Buying all collab items and retrying charges exactly once per item");
    var bundleBuyer = await User(120000, 6000);
    var bundleRequest = new ShopPurchaseRequest { RequestId = Guid.NewGuid(), OfferId = complete.OfferId, Currency = "GOLDS", ExpectedPrice = 90000,
        ExpectedItemIds = complete.Items.Select(i => i.ItemId).ToList() };
    await krShop.PurchaseAsync(bundleBuyer.Id, bundleRequest); await krShop.PurchaseAsync(bundleBuyer.Id, bundleRequest);
    Need(await Gold(bundleBuyer) == 30000 && await inventory.CountDocumentsAsync(i => i.UserId == bundleBuyer.Id) == 3 &&
        await receipts.CountDocumentsAsync(i => i.UserId == bundleBuyer.Id) == 1, "Full collab bundle grants all three products for one discounted Gold debit, including retry");
    var diamondBuyer = await User(120000, 6000);
    var diamondRequest = new ShopPurchaseRequest { RequestId = Guid.NewGuid(), OfferId = complete.OfferId, Currency = "DIAMONDS", ExpectedPrice = 4500,
        ExpectedItemIds = complete.Items.Select(i => i.ItemId).ToList() };
    var diamondReceipt = await krShop.PurchaseAsync(diamondBuyer.Id, diamondRequest);
    Need(diamondReceipt.Wallet.Diamonds == 1500 && diamondReceipt.Wallet.Golds == 120000 && diamondReceipt.GrantedItemIds.Count == 3,
        "Full collab bundle supports the exact 4500-Diamond alternative without spending Gold");
    var ownedSet = complete.Items.Single(i => i.Type == CosmeticLoadoutPolicy.SetType).ItemId;
    Need(ShopCatalog.Quote(complete, new HashSet<string> { ownedSet }, "GOLDS").Price == 45000 &&
        ShopCatalog.Quote(complete, new HashSet<string> { ownedSet }, "DIAMONDS").Price == 2250,
        "Already owned set is excluded before applying 25 percent discount to remaining board and background");
    var equippedBg = await invService.EquipBackgroundAsync(collector.Id, bg.Id.ToString());
    Need(equippedBg.BackgroundSkinId == bg.Id && CosmeticLoadoutPolicy.References(equippedBg).Single().Slot == "BACKGROUND", "Owned background equips and appears in frozen online loadout references");
    var krSet = krItems.Single(i => i.Type == CosmeticLoadoutPolicy.SetType); var krBoard = krItems.Single(i => i.Type == CosmeticLoadoutPolicy.BoardType);
    var equippedAll = await invService.EquipLoadoutAsync(collector.Id, new ChessGame.Api.DTOs.Inventory.EquipLoadoutRequest {
        ChessSkinId = krSet.Id.ToString(), BoardSkinId = krBoard.Id.ToString() });
    Need(equippedAll.BackgroundSkinId == bg.Id && CosmeticLoadoutPolicy.References(equippedAll).Count() == 3,
        "Set/board equipment preserves independently purchased background");
    Need((await invService.GetUserInventoryAsync(collector.Id)).All(i => i.IsEquipped), "Inventory marks all three collab categories equipped");
    var resetBg = await invService.EquipBackgroundAsync(collector.Id, null);
    Need(resetBg.BackgroundSkinId == null && resetBg.ChessSkinId == krSet.Id && resetBg.BoardSkinId == krBoard.Id,
        "Restoring locked default background preserves set, board, wallet and ownership");
    try { await invService.EquipBackgroundAsync(collector.Id, krBoard.Id.ToString()); throw new Exception("Board used as background"); }
    catch (InvalidOperationException e) { Need(e.Message == "ITEM_NOT_EQUIPPABLE", "Board ownership cannot substitute for background ownership"); }
    var banner = new GachaBanner { Code = "KR_EXCLUSION_FIXTURE", IsActive = true, PityGroup = "fixture", Pity = new() { Epic = 10, Legendary = 80 },
        Costs = new() { new() { RollCount = 1, Currency = "TICKETS", Amount = 1 } }, Pool = new() { new() { ItemId = bg.Id, Weight = 1 } } };
    await db.GetCollection<GachaBanner>("gacha_banners").InsertOneAsync(banner);
    try { await new GachaService(db, client, invService).GetBannerAsync(banner.Code); throw new Exception("Shop exclusive accepted by gacha"); }
    catch (GachaException e) { Need(e.Code == "BANNER_MISCONFIGURED", "Gacha rejects a shop-exclusive collab even if accidentally added to a banner"); }
    File.WriteAllLines(Path.Combine(directory, "checks.txt"), checks);
    Console.WriteLine("SHOP_INTEGRATION_PASS " + checks.Count);
}
finally
{
    if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
}
