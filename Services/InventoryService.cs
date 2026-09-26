using ChessGame.Api.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ChessGame.Api.Services;

public class InventoryService
{
    private readonly IMongoCollection<Item> _items;
    private readonly IMongoCollection<PlayerItem> _playerItems;

    public InventoryService(IMongoDatabase database)
    {
        _items = database.GetCollection<Item>("items");
        _playerItems = database.GetCollection<PlayerItem>("player_items");
    }

    public async Task<(Item ChessSkin, Item BoardSkin)> GetDefaultSkinsAsync()
    {
        var filter = Builders<Item>.Filter.In(
                item => item.Code, new[] { "CHESS_DEFAULT", "BOARD_DEFAULT" })
            & Builders<Item>.Filter.Eq(item => item.IsActive, true);

        var items = await _items.Find(filter).ToListAsync();
        var chessSkin = items.FirstOrDefault(item => item.Code == "CHESS_DEFAULT");
        var boardSkin = items.FirstOrDefault(item => item.Code == "BOARD_DEFAULT");

        if (chessSkin is null || boardSkin is null)
            throw new InvalidOperationException("DEFAULT_ITEMS_NOT_CONFIGURED");

        return (chessSkin, boardSkin);
    }

    public async Task GrantDefaultSkinsAsync(
        IClientSessionHandle session,
        ObjectId userId,
        ObjectId chessSkinId,
        ObjectId boardSkinId)
    {
        var now = DateTime.UtcNow;
        await _playerItems.InsertManyAsync(session, new[]
        {
            new PlayerItem
            {
                UserId = userId,
                ItemId = chessSkinId,
                AcquiredSource = "DEFAULT",
                AcquiredAt = now
            },
            new PlayerItem
            {
                UserId = userId,
                ItemId = boardSkinId,
                AcquiredSource = "DEFAULT",
                AcquiredAt = now
            }
        });
    }

    public async Task<bool> GrantGachaItemAsync(
        IClientSessionHandle session,
        ObjectId userId,
        ObjectId itemId,
        DateTime now)
    {
        var alreadyOwned = await _playerItems
            .Find(session, x => x.UserId == userId && x.ItemId == itemId)
            .AnyAsync();

        if (alreadyOwned)
            return true;

        await _playerItems.InsertOneAsync(session, new PlayerItem
        {
            UserId = userId,
            ItemId = itemId,
            AcquiredSource = "GACHA",
            AcquiredAt = now
        });
        return false;
    }

    public async Task EnsureIndexesAsync()
    {
        var index = new CreateIndexModel<PlayerItem>(
            Builders<PlayerItem>.IndexKeys
                .Ascending(x => x.UserId)
                .Ascending(x => x.ItemId),
            new CreateIndexOptions
            {
                Unique = true,
                Name = "uq_player_items_user_item"
            });

        await _playerItems.Indexes.CreateOneAsync(index);
    }
}
