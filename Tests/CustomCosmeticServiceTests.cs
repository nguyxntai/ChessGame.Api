using ChessGame.Api.DTOs.Inventory;
using ChessGame.Api.Models;
using ChessGame.Api.Services;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace ChessGame.Api.Tests;

public sealed class CustomCosmeticServiceTests
{
    [Theory][InlineData(true)][InlineData(false)]
    public async Task SaveLoadoutWritesOnlyAuthenticatedEquipmentAfterOwnershipCheck(bool ownedByCaller)
    {
        var caller = ObjectId.GenerateNewId();
        var set = new Item { Id = ObjectId.GenerateNewId(), Type = CosmeticLoadoutPolicy.SetType, IsActive = true };
        var board = new Item { Id = ObjectId.GenerateNewId(), Type = CosmeticLoadoutPolicy.BoardType, IsActive = true };
        var piece = new Item { Id = ObjectId.GenerateNewId(), Type = "CHESS_BLACK_KNIGHT_SKIN", IsActive = true };
        var db = new Mock<IMongoDatabase>(MockBehavior.Strict);
        var items = Collection<Item>(db, "items"); var inventory = Collection<PlayerItem>(db, "player_items"); var users = Collection<User>(db, "users");
        items.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<Item>>(), It.IsAny<FindOptions<Item, Item>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Cursor(new[] { set, board, piece }));
        inventory.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<PlayerItem>>(), It.IsAny<FindOptions<PlayerItem, PlayerItem>>(), It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<PlayerItem> filter, FindOptions<PlayerItem, PlayerItem> _, CancellationToken _) => {
                Assert.Equal(caller, Render(filter)["userId"].AsObjectId);
                var rows = (ownedByCaller ? new[] { set, board, piece } : new[] { set, board })
                    .Select(i => new PlayerItem { UserId = caller, ItemId = i.Id }).ToArray();
                return Task.FromResult(Cursor(rows));
            });
        BsonDocument? write = null;
        var backgroundId = ObjectId.GenerateNewId();
        users.Setup(c => c.FindOneAndUpdateAsync(It.IsAny<FilterDefinition<User>>(), It.IsAny<UpdateDefinition<User>>(), It.IsAny<FindOneAndUpdateOptions<User, User>>(), It.IsAny<CancellationToken>()))
            .Callback((FilterDefinition<User> f, UpdateDefinition<User> u, FindOneAndUpdateOptions<User, User> options, CancellationToken _) => {
                Assert.Equal(caller, Render(f)["_id"].AsObjectId);
                Assert.Equal(ReturnDocument.After, options.ReturnDocument);
                write = u.Render(new RenderArgs<User>(BsonSerializer.LookupSerializer<User>(), BsonSerializer.SerializerRegistry)).AsBsonDocument;
            }).ReturnsAsync(() => new User { Id = caller, Equipped = new EquippedSkins { BackgroundSkinId = backgroundId,
                ChessSkinId = set.Id, BoardSkinId = board.Id, PieceMode = "CUSTOM", Pieces = new() { new() { Slot = "BLACK_KNIGHT", ItemId = piece.Id } } } });
        var request = new EquipLoadoutRequest { PieceMode = "CUSTOM", ChessSkinId = set.Id.ToString(), BoardSkinId = board.Id.ToString(),
            Pieces = new() { new() { Slot = "BLACK_KNIGHT", ItemId = piece.Id.ToString() } } };
        var service = new InventoryService(db.Object);
        if (!ownedByCaller)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EquipLoadoutAsync(caller, request));
            Assert.Equal("ITEM_NOT_OWNED", error.Message); Assert.Null(write);
        }
        else
        {
            var equipped = await service.EquipLoadoutAsync(caller, request);
            Assert.Equal(piece.Id, Assert.Single(equipped.Pieces).ItemId);
            Assert.NotNull(write); Assert.Single(write);
            Assert.Equal(new[] { "equipped.boardSkinId", "equipped.chessSkinId", "equipped.pieceMode", "equipped.pieces", "updatedAt" }, write["$set"].AsBsonDocument.Names.Order().ToArray());
            Assert.Equal(backgroundId, equipped.BackgroundSkinId);
        }
        // Strict mocks also reject grants, user inserts, catalog writes, sessions and extra collections.
    }
    private static BsonDocument Render<T>(FilterDefinition<T> filter) => filter.Render(new RenderArgs<T>(BsonSerializer.LookupSerializer<T>(), BsonSerializer.SerializerRegistry));
    private static Mock<IMongoCollection<T>> Collection<T>(Mock<IMongoDatabase> db, string name)
    {
        var c = new Mock<IMongoCollection<T>>(MockBehavior.Strict); db.Setup(d => d.GetCollection<T>(name, null)).Returns(c.Object); return c;
    }
    private static IAsyncCursor<T> Cursor<T>(T[] rows)
    {
        var c = new Mock<IAsyncCursor<T>>(MockBehavior.Strict); c.SetupGet(x => x.Current).Returns(rows);
        c.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false); c.Setup(x => x.Dispose()); return c.Object;
    }
}
