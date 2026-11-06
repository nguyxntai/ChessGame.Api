using ChessGame.Api.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ChessGame.Api.Services.Shop;

// Stable catalog installation and explicit pre-release retirement of the mixed set/board.
// Keeps art/history and the original Legendary background; never changes wallets or banners.
public static class KingdomRushCatalog
{
    public const string AssetKey = "kingdom_rush";
    public const string DenasKey = "kingdom_rush_denas", VeznanKey = "kingdom_rush_veznan";
    public static readonly string[] RetiredCodes = { "CHESS_KINGDOM_RUSH", "BOARD_KINGDOM_RUSH" };
    public static IReadOnlyList<Item> Definitions() => new[]
    {
        Product("6b0c4f010000000000000001", "CHESS_KINGDOM_RUSH", "Kingdom Rush — Denas & Vez'Nan (Archived)", CosmeticLoadoutPolicy.SetType, AssetKey, false),
        Product("6b0c4f010000000000000002", "BOARD_KINGDOM_RUSH", "Kingdom Rush — Citadel Board (Archived)", CosmeticLoadoutPolicy.BoardType, AssetKey, false),
        Product("6b0c4f010000000000000003", "BACKGROUND_KINGDOM_RUSH", "Kingdom Rush — Linirea Valley", CosmeticLoadoutPolicy.BackgroundType, AssetKey),
        Product("6b0c4f010000000000000011", "CHESS_KINGDOM_RUSH_DENAS", "Kingdom Rush — Denas Royal Guard", CosmeticLoadoutPolicy.SetType, DenasKey),
        Product("6b0c4f010000000000000012", "BOARD_KINGDOM_RUSH_DENAS", "Kingdom Rush — Denas Royal Citadel", CosmeticLoadoutPolicy.BoardType, DenasKey),
        Product("6b0c4f010000000000000013", "BACKGROUND_KINGDOM_RUSH_DENAS", "Kingdom Rush — Linirea Royal Heartlands", CosmeticLoadoutPolicy.BackgroundType, DenasKey),
        Product("6b0c4f010000000000000021", "CHESS_KINGDOM_RUSH_VEZNAN", "Kingdom Rush — Vez'Nan Dark Dominion", CosmeticLoadoutPolicy.SetType, VeznanKey),
        Product("6b0c4f010000000000000022", "BOARD_KINGDOM_RUSH_VEZNAN", "Kingdom Rush — Vez'Nan Ruin Throne", CosmeticLoadoutPolicy.BoardType, VeznanKey),
        Product("6b0c4f010000000000000023", "BACKGROUND_KINGDOM_RUSH_VEZNAN", "Kingdom Rush — Vez'Nan Scorched Dominion", CosmeticLoadoutPolicy.BackgroundType, VeznanKey)
    };
    private static Item Product(string id, string code, string name, string type, string key, bool active = true) => new()
    {
        Id = ObjectId.Parse(id), Code = code, Name = name, Type = type, Rarity = "LEGENDARY",
        UnityAssetKey = key, IsActive = active, IsDefault = false, ShopExclusive = true
    };

    public static async Task EnsureItemsAsync(IMongoDatabase database, CancellationToken token = default)
    {
        var items = database.GetCollection<Item>("items");
        var definitions = Definitions();
        // Check the complete plan before applying any change. Do not rebind a
        // pre-existing code/asset mapping or overwrite another product's ID.
        var resolved = new List<Item>();
        foreach (var definition in definitions)
        {
            var matches = await items.Find(i => i.Code == definition.Code || i.Id == definition.Id ||
                i.Type == definition.Type && i.UnityAssetKey == definition.UnityAssetKey).ToListAsync(token);
            if (matches.Count > 1 || matches.Count == 1 &&
                (matches[0].Code != definition.Code || matches[0].Type != definition.Type || matches[0].UnityAssetKey != definition.UnityAssetKey || matches[0].IsDefault))
                throw new InvalidOperationException("KINGDOM_RUSH_CATALOG_CONFLICT: " + definition.Code);
            if (matches.Count == 1) definition.Id = matches[0].Id;
            resolved.Add(definition);
        }
        foreach (var definition in resolved)
        {
            var update = Builders<Item>.Update.SetOnInsert(i => i.Code, definition.Code)
                .SetOnInsert(i => i.Type, definition.Type).SetOnInsert(i => i.UnityAssetKey, definition.UnityAssetKey)
                .SetOnInsert(i => i.IsDefault, false)
                .SetOnInsert(i => i.Name, definition.Name).SetOnInsert(i => i.Rarity, definition.Rarity)
                .Set(i => i.IsActive, definition.IsActive).Set(i => i.ShopExclusive, true);
            await items.UpdateOneAsync(i => i.Id == definition.Id, update, new UpdateOptions { IsUpsert = true }, token);
        }
        await RetireOwnershipAsync(database, resolved.Where(i => !i.IsActive).Select(i => i.Id).ToArray(), token);
    }

    private static async Task RetireOwnershipAsync(IMongoDatabase database, ObjectId[] retired, CancellationToken token)
    {
        // User explicitly chose pre-release revocation without compensation. Preserve original
        // ownership documents in an audit collection, atomically with removal and equipment reset.
        var ownership=database.GetCollection<PlayerItem>("player_items");
        var archive=database.GetCollection<BsonDocument>("kingdom_rush_retired_ownership");
        var users=database.GetCollection<User>("users");
        using var session=await database.Client.StartSessionAsync(cancellationToken:token);
        await session.WithTransactionAsync(async (transaction, ct) =>
        {
            var filter=Builders<PlayerItem>.Filter.In(i=>i.ItemId,retired);
            var old=await ownership.Find(transaction,filter).ToListAsync(ct);
            foreach(var row in old)
            {
                var doc=row.ToBsonDocument();doc["retiredAt"]=DateTime.UtcNow;doc["reason"]="PRE_RELEASE_FACTION_SPLIT_NO_COMPENSATION";
                await archive.UpdateOneAsync(transaction,new BsonDocument("_id",row.Id),new BsonDocument("$setOnInsert",doc),new UpdateOptions{IsUpsert=true},ct);
            }
            await users.UpdateManyAsync(transaction,Builders<User>.Filter.In(u=>u.Equipped.ChessSkinId,retired.Select(i=>(ObjectId?)i)),
                Builders<User>.Update.Set(u=>u.Equipped.ChessSkinId,null).Set(u=>u.Equipped.PieceMode,"SET"),cancellationToken:ct);
            await users.UpdateManyAsync(transaction,Builders<User>.Filter.In(u=>u.Equipped.BoardSkinId,retired.Select(i=>(ObjectId?)i)),
                Builders<User>.Update.Set(u=>u.Equipped.BoardSkinId,null),cancellationToken:ct);
            await ownership.DeleteManyAsync(transaction,filter,cancellationToken:ct);
            return old.Count;
        },cancellationToken:token);
    }
}
