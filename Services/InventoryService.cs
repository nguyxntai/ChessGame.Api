using ChessGame.Api.DTOs.Inventory;
using ChessGame.Api.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ChessGame.Api.Services;

public class InventoryService
{
    private readonly IMongoCollection<Item> _items;
    private readonly IMongoCollection<PlayerItem> _playerItems;
    private readonly IMongoCollection<User> _users;

    public InventoryService(IMongoDatabase database)
    {
        _items = database.GetCollection<Item>("items");
        _playerItems = database.GetCollection<PlayerItem>("player_items");
        _users = database.GetCollection<User>("users");
    }

    public async Task<List<InventoryItemResponse>> GetUserInventoryAsync(ObjectId userId, string? typeFilter = null)
    {
        var playerItems = await _playerItems.Find(pi => pi.UserId == userId).ToListAsync();
        if (playerItems.Count == 0)
        {
            return new List<InventoryItemResponse>();
        }

        var itemIds = playerItems.Select(pi => pi.ItemId).Distinct().ToList();
        var itemFilter = Builders<Item>.Filter.In(i => i.Id, itemIds);

        if (!string.IsNullOrWhiteSpace(typeFilter))
        {
            string normalizedType = typeFilter.Trim().ToUpperInvariant();
            itemFilter &= Builders<Item>.Filter.Eq(i => i.Type, normalizedType);
        }

        var items = await _items.Find(itemFilter).ToListAsync();
        var itemsDict = items.ToDictionary(i => i.Id);

        var user = await _users.Find(u => u.Id == userId).FirstOrDefaultAsync();
        var equippedChessId = user?.Equipped?.ChessSkinId;
        var equippedBoardId = user?.Equipped?.BoardSkinId;

        var result = new List<InventoryItemResponse>();

        foreach (var pi in playerItems)
        {
            if (itemsDict.TryGetValue(pi.ItemId, out var item))
            {
                bool isEquipped = (equippedChessId.HasValue && equippedChessId.Value == item.Id) ||
                                 (equippedBoardId.HasValue && equippedBoardId.Value == item.Id);

                result.Add(new InventoryItemResponse
                {
                    PlayerItemId = pi.Id.ToString(),
                    ItemId = item.Id.ToString(),
                    Code = item.Code,
                    Name = item.Name,
                    Type = item.Type,
                    Rarity = item.Rarity,
                    UnityAssetKey = item.UnityAssetKey,
                    IsDefault = item.IsDefault,
                    IsEquipped = isEquipped,
                    AcquiredSource = pi.AcquiredSource,
                    AcquiredAt = pi.AcquiredAt
                });
            }
        }

        return result;
    }

    public async Task<List<ItemCatalogResponse>> GetCatalogAsync(string? typeFilter = null)
    {
        var filter = Builders<Item>.Filter.Eq(i => i.IsActive, true);

        if (!string.IsNullOrWhiteSpace(typeFilter))
        {
            string normalizedType = typeFilter.Trim().ToUpperInvariant();
            filter &= Builders<Item>.Filter.Eq(i => i.Type, normalizedType);
        }

        var items = await _items.Find(filter).ToListAsync();

        return items.Select(item => new ItemCatalogResponse
        {
            ItemId = item.Id.ToString(),
            Code = item.Code,
            Name = item.Name,
            Type = item.Type,
            Rarity = item.Rarity,
            UnityAssetKey = item.UnityAssetKey,
            IsDefault = item.IsDefault,
            IsActive = item.IsActive
        }).ToList();
    }

    public async Task<EquippedSkins> EquipItemAsync(ObjectId userId, ObjectId itemId)
    {
        var item = await _items.Find(i => i.Id == itemId && i.IsActive).FirstOrDefaultAsync();
        if (item is null)
        {
            throw new InvalidOperationException("ITEM_NOT_FOUND");
        }

        var ownsItem = await _playerItems.Find(pi => pi.UserId == userId && pi.ItemId == itemId).AnyAsync();
        if (!ownsItem)
        {
            throw new InvalidOperationException("ITEM_NOT_OWNED");
        }

        var user = await _users.Find(u => u.Id == userId).FirstOrDefaultAsync();
        if (user is null)
        {
            throw new InvalidOperationException("USER_NOT_FOUND");
        }

        var equipped = user.Equipped ?? new EquippedSkins();

        string typeUpper = item.Type.Trim().ToUpperInvariant();
        if (typeUpper.Contains("CHESS"))
        {
            equipped.ChessSkinId = itemId;
        }
        else if (typeUpper.Contains("BOARD"))
        {
            equipped.BoardSkinId = itemId;
        }
        else
        {
            throw new InvalidOperationException("ITEM_NOT_EQUIPPABLE");
        }

        var update = Builders<User>.Update
            .Set(u => u.Equipped, equipped)
            .Set(u => u.UpdatedAt, DateTime.UtcNow);

        await _users.UpdateOneAsync(u => u.Id == userId, update);

        return equipped;
    }

    public async Task<(Item ChessSkin, Item BoardSkin)> GetDefaultSkinsAsync()
    {
        var filter = Builders<Item>.Filter.In(
            item => item.Code,
            new[] { "CHESS_DEFAULT", "BOARD_DEFAULT" }
        ) & Builders<Item>.Filter.Eq(item => item.IsActive, true);

        var items = await _items.Find(filter).ToListAsync();

        var chessSkin = items.FirstOrDefault(item => item.Code == "CHESS_DEFAULT");
        var boardSkin = items.FirstOrDefault(item => item.Code == "BOARD_DEFAULT");

        if (chessSkin is null || boardSkin is null)
        {
            throw new InvalidOperationException("DEFAULT_ITEMS_NOT_CONFIGURED");
        }

        return (chessSkin, boardSkin);
    }

    public async Task GrantDefaultSkinsAsync(
        IClientSessionHandle session,
        ObjectId userId,
        ObjectId chessSkinId,
        ObjectId boardSkinId)
    {
        var now = DateTime.UtcNow;

        var playerItems = new[]
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
        };

        await _playerItems.InsertManyAsync(session, playerItems);
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
            }
        );

        await _playerItems.Indexes.CreateOneAsync(index);
    }
}
