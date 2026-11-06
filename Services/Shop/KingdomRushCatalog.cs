using ChessGame.Api.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ChessGame.Api.Services.Shop;

// Only installs the three newly authored shop products. Never grants ownership,
// modifies wallets or changes banner pools. Stable IDs make first install atomic
// across simultaneous API instances; existing matching codes retain their IDs.
public static class KingdomRushCatalog
{
    public const string AssetKey = "kingdom_rush";
    public static IReadOnlyList<Item> Definitions() => new[]
    {
        Product("6b0c4f010000000000000001", "CHESS_KINGDOM_RUSH", "Kingdom Rush — Denas & Vez'Nan", CosmeticLoadoutPolicy.SetType),
        Product("6b0c4f010000000000000002", "BOARD_KINGDOM_RUSH", "Kingdom Rush — Citadel Board", CosmeticLoadoutPolicy.BoardType),
        Product("6b0c4f010000000000000003", "BACKGROUND_KINGDOM_RUSH", "Kingdom Rush — Linirea Valley", CosmeticLoadoutPolicy.BackgroundType)
    };
    private static Item Product(string id, string code, string name, string type) => new()
    {
        Id = ObjectId.Parse(id), Code = code, Name = name, Type = type, Rarity = "LEGENDARY",
        UnityAssetKey = AssetKey, IsActive = true, IsDefault = false, ShopExclusive = true
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
                i.Type == definition.Type && i.UnityAssetKey == AssetKey).ToListAsync(token);
            if (matches.Count > 1 || matches.Count == 1 &&
                (matches[0].Code != definition.Code || matches[0].Type != definition.Type || matches[0].UnityAssetKey != AssetKey || matches[0].IsDefault))
                throw new InvalidOperationException("KINGDOM_RUSH_CATALOG_CONFLICT: " + definition.Code);
            if (matches.Count == 1) definition.Id = matches[0].Id;
            resolved.Add(definition);
        }
        foreach (var definition in resolved)
        {
            var update = Builders<Item>.Update.SetOnInsert(i => i.Code, definition.Code)
                .SetOnInsert(i => i.Type, definition.Type).SetOnInsert(i => i.UnityAssetKey, definition.UnityAssetKey)
                .SetOnInsert(i => i.IsDefault, false).SetOnInsert(i => i.IsActive, true)
                .SetOnInsert(i => i.Name, definition.Name).SetOnInsert(i => i.Rarity, definition.Rarity)
                .Set(i => i.ShopExclusive, true);
            await items.UpdateOneAsync(i => i.Id == definition.Id, update, new UpdateOptions { IsUpsert = true }, token);
        }
    }
}
