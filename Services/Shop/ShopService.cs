using ChessGame.Api.DTOs.Shop;
using ChessGame.Api.DTOs.User;
using ChessGame.Api.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ChessGame.Api.Services.Shop;

public sealed class ShopService
{
    private readonly IMongoClient client;
    private readonly IMongoCollection<Item> items;
    private readonly IMongoCollection<PlayerItem> inventory;
    private readonly IMongoCollection<User> users;
    private readonly IMongoCollection<ShopPurchase> purchases;
    private readonly IMongoCollection<CurrencyTransaction> ledger;
    private readonly ShopCatalogOptions catalog;
    public ShopService(IMongoDatabase db, IMongoClient client, IOptions<ShopCatalogOptions> options)
    {
        this.client = client; catalog = options.Value;
        items = db.GetCollection<Item>("items"); inventory = db.GetCollection<PlayerItem>("player_items");
        users = db.GetCollection<User>("users"); purchases = db.GetCollection<ShopPurchase>("shop_purchases");
        ledger = db.GetCollection<CurrencyTransaction>("currency_transactions");
    }
    public async Task<ShopCatalogResponse> GetCatalogAsync(CancellationToken token = default) => new()
    { Offers = ShopCatalog.Resolve(catalog, await items.Find(i => i.IsActive).ToListAsync(token)) };

    public async Task<ShopPurchaseResponse> PurchaseAsync(ObjectId userId, ShopPurchaseRequest request, CancellationToken token = default)
    {
        ShopCatalog.ValidateRequest(request);
        // The driver retries transient transactions and ambiguous commits on the original session.
        // A unique (user, request) receipt makes client retries safe across restarts as well.
        using var session = await client.StartSessionAsync(cancellationToken: token);
        try
        {
            return await session.WithTransactionAsync(async (s, ct) =>
            {
                var user = await users.Find(s, u => u.Id == userId).FirstOrDefaultAsync(ct);
                if (user == null) throw new ShopException("USER_NOT_FOUND");
                if (!user.IsActive) throw new ShopException("ACCOUNT_DISABLED");
                var previous = await purchases.Find(s, p => p.UserId == userId && p.RequestId == request.RequestId.ToString("D")).FirstOrDefaultAsync(ct);
                if (previous != null) return Replay(previous, request);
                var offers = ShopCatalog.Resolve(catalog, await items.Find(s, i => i.IsActive).ToListAsync(ct));
                var offer = offers.SingleOrDefault(o => o.OfferId == request.OfferId) ?? throw new ShopException("OFFER_UNAVAILABLE");
                var owned = (await inventory.Find(s, i => i.UserId == userId).ToListAsync(ct)).Select(i => i.ItemId.ToString()).ToHashSet();
                var (price, missing) = ShopCatalog.Quote(offer, owned, request.Currency);
                if (missing.Count == 0) throw new ShopException("ALREADY_OWNED");
                if (price != request.ExpectedPrice || !missing.Select(i => i.ItemId).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(request.ExpectedItemIds))
                    throw new ShopException("QUOTE_CHANGED");
                var field = request.Currency == "GOLDS" ? "wallet.golds" : "wallet.diamonds";
                var after = await users.FindOneAndUpdateAsync(s,
                    Builders<User>.Filter.Eq(u => u.Id, userId) & Builders<User>.Filter.Eq(u => u.IsActive, true) & Builders<User>.Filter.Gte(field, price),
                    Builders<User>.Update.Inc(field, -price).Set(u => u.UpdatedAt, DateTime.UtcNow),
                    new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After }, ct);
                if (after == null) throw new ShopException("INSUFFICIENT_CURRENCY");
                var receipt = new ShopPurchase { UserId = userId, RequestId = request.RequestId.ToString("D"), OfferId = request.OfferId,
                    Currency = request.Currency, TotalCost = price, GrantedItemIds = missing.Select(i => ObjectId.Parse(i.ItemId)).ToList(), WalletAfter = after.Wallet };
                await inventory.InsertManyAsync(s, receipt.GrantedItemIds.Select(id => new PlayerItem { UserId = userId, ItemId = id,
                    AcquiredSource = "SHOP", AcquiredAt = receipt.CreatedAt }), cancellationToken: ct);
                await ledger.InsertOneAsync(s, new CurrencyTransaction { UserId = userId, Currency = request.Currency, Amount = -price,
                    BalanceAfter = request.Currency == "GOLDS" ? after.Wallet.Golds : after.Wallet.Diamonds, Source = "SHOP",
                    RequestId = receipt.RequestId, EntryIndex = 0, CreatedAt = receipt.CreatedAt }, cancellationToken: ct);
                await purchases.InsertOneAsync(s, receipt, cancellationToken: ct);
                return Response(receipt);
            }, new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), token);
        }
        catch (MongoException ex) when (ex is MongoWriteException { WriteError.Category: ServerErrorCategory.DuplicateKey } ||
            ex is MongoCommandException { Code: 11000 })
        {
            // Another caller may have committed the same request. Never debit a second time.
            var previous = await purchases.WithReadConcern(ReadConcern.Majority).WithReadPreference(ReadPreference.Primary)
                .Find(p => p.UserId == userId && p.RequestId == request.RequestId.ToString("D")).FirstOrDefaultAsync(token);
            if (previous != null) return Replay(previous, request);
            throw new ShopException("PURCHASE_CONFLICT");
        }
    }
    public static ShopPurchaseResponse Replay(ShopPurchase receipt, ShopPurchaseRequest request)
    {
        if (receipt.OfferId != request.OfferId || receipt.Currency != request.Currency || receipt.TotalCost != request.ExpectedPrice ||
            !receipt.GrantedItemIds.Select(i => i.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(request.ExpectedItemIds))
            throw new ShopException("REQUEST_CONFLICT");
        return Response(receipt);
    }
    private static ShopPurchaseResponse Response(ShopPurchase receipt) => new()
    {
        RequestId = receipt.RequestId, OfferId = receipt.OfferId, Currency = receipt.Currency, TotalCost = receipt.TotalCost,
        GrantedItemIds = receipt.GrantedItemIds.Select(i => i.ToString()).ToList(),
        Wallet = new WalletResponse { Golds = receipt.WalletAfter.Golds, Diamonds = receipt.WalletAfter.Diamonds, Tickets = receipt.WalletAfter.Tickets }
    };
    public Task EnsureIndexesAsync() => purchases.Indexes.CreateOneAsync(new CreateIndexModel<ShopPurchase>(
        Builders<ShopPurchase>.IndexKeys.Ascending(p => p.UserId).Ascending(p => p.RequestId),
        new CreateIndexOptions { Unique = true, Name = "uq_shop_purchase_user_request" }));
}
