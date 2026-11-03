using ChessGame.Api.Models;
using ChessGame.Api.Services;
using ChessGame.Api.DTOs.User;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Xunit;

namespace ChessGame.Api.Tests;

public sealed class CustomCosmeticLoadoutTests
{
    public static IEnumerable<object[]> Slots => CosmeticLoadoutPolicy.Slots.Select(s => new object[] { s });
    private static (Item Set, Item Board, Item Single, Dictionary<ObjectId, Item> Items, HashSet<ObjectId> Owned) Fixture(string slot)
    {
        var set = new Item { Id = ObjectId.GenerateNewId(), Type = CosmeticLoadoutPolicy.SetType, IsActive = true };
        var board = new Item { Id = ObjectId.GenerateNewId(), Type = CosmeticLoadoutPolicy.BoardType, IsActive = true };
        var single = new Item { Id = ObjectId.GenerateNewId(), Type = "CHESS_" + slot + "_SKIN", IsActive = true };
        var items = new[] { set, board, single }.ToDictionary(i => i.Id);
        return (set, board, single, items, items.Keys.ToHashSet());
    }
    [Theory, MemberData(nameof(Slots))]
    public void IndividualOwnershipAppliesOnlyToExactKindAndSide(string slot)
    {
        var f = Fixture(slot);
        var result = CosmeticLoadoutPolicy.Validate("CUSTOM", f.Set.Id, f.Board.Id,
            new[] { new EquippedPiece { Slot = slot, ItemId = f.Single.Id } }, f.Items, f.Owned);
        Assert.Equal(f.Single.Id, Assert.Single(result.Pieces).ItemId);
        foreach (var other in CosmeticLoadoutPolicy.Slots.Where(s => s != slot))
            Assert.False(CosmeticLoadoutPolicy.Fits(f.Single.Type, other));
        Assert.Equal(slot, CosmeticLoadoutPolicy.SlotForType(f.Single.Type));
        var bson = result.ToBson(); var restored = BsonSerializer.Deserialize<EquippedSkins>(bson);
        Assert.Equal(bson, restored.ToBson());
        var dto = EquippedSkinsResponse.From(restored);
        Assert.Equal(f.Single.Id.ToString(), Assert.Single(dto.Pieces).ItemId);
    }
    [Fact]
    public void OwnedSetsCanMixTwelveSlotsWithoutGrantingComponentItems()
    {
        var f = Fixture("WHITE_PAWN");
        var pieces = CosmeticLoadoutPolicy.Slots.Select(s => new EquippedPiece { Slot = s, ItemId = f.Set.Id }).ToArray();
        var result = CosmeticLoadoutPolicy.Validate("CUSTOM", f.Set.Id, f.Board.Id, pieces, f.Items, f.Owned);
        Assert.Equal(12, result.Pieces.Count); Assert.Equal(3, f.Owned.Count);
        Assert.Equal(14, CosmeticLoadoutPolicy.References(result).Count());
        result.PieceMode = "SET"; Assert.Equal(2, CosmeticLoadoutPolicy.References(result).Count());
        Assert.Equal(12, result.Pieces.Count); // Switching back to set does not destroy saved custom choices.
    }
    [Theory]
    [InlineData("CHESS_WHITE_PAWN")][InlineData("NOT_CHESS_WHITE_PAWN_SKIN")]
    [InlineData("chess_white_pawn_skin")][InlineData("CHESS_SHADOW_KNIGHT")][InlineData("")][InlineData(null)]
    public void UnknownRawTypesNeverBecomePieceTypes(string? type)
    { Assert.Null(CosmeticLoadoutPolicy.SlotForType(type)); Assert.False(CosmeticLoadoutPolicy.Fits(type, "WHITE_PAWN")); }
    [Theory]
    [InlineData("unowned")][InlineData("inactive")][InlineData("wrong-side")][InlineData("board-as-piece")]
    [InlineData("duplicate-slot")][InlineData("unknown-slot")][InlineData("mode")]
    public void RejectsUnsafeLoadouts(string reason)
    {
        var f = Fixture("WHITE_PAWN");
        var pieces = new List<EquippedPiece> { new() { Slot = "WHITE_PAWN", ItemId = f.Single.Id } };
        string mode = "CUSTOM";
        switch (reason) {
            case "unowned": f.Owned.Remove(f.Single.Id); break;
            case "inactive": f.Single.IsActive = false; break;
            case "wrong-side": pieces[0].Slot = "BLACK_PAWN"; break;
            case "board-as-piece": pieces[0].ItemId = f.Board.Id; break;
            case "duplicate-slot": pieces.Add(pieces[0]); break;
            case "unknown-slot": pieces[0].Slot = "WHITE_DRAGON"; break;
            case "mode": mode = "CUSTOMISH"; break;
        }
        Assert.Throws<InvalidOperationException>(() => CosmeticLoadoutPolicy.Validate(mode, f.Set.Id, f.Board.Id, pieces, f.Items, f.Owned));
    }
    [Fact]
    public void OldEquippedBsonRemainsSetModeWithoutMigration()
    {
        var e = BsonSerializer.Deserialize<EquippedSkins>(new BsonDocument { { "chessSkinId", ObjectId.GenerateNewId() }, { "boardSkinId", BsonNull.Value } });
        Assert.Equal("SET", e.PieceMode); Assert.Empty(e.Pieces);
        Assert.Single(CosmeticLoadoutPolicy.References(e));
    }
    [Fact]
    public void ProductionAuthorityIsStillEmpty()
    { Assert.Equal("NO_APPROVED_MANIFEST", ItemPresentationPolicy.Current.ApprovalScope); }

    [Fact]
    public void HistoricalOnlineLoadoutWithoutSlotIsReadable()
    {
        var source = new ChessGame.Api.Online.LoadoutSnapshot("fixture-item", "fixture-code", "fixture-key", "CHESS_PIECE_SKIN");
        var old = source.ToBsonDocument(); old.Remove("Slot"); old.Remove("slot");
        var read = BsonSerializer.Deserialize<ChessGame.Api.Online.LoadoutSnapshot>(old);
        Assert.Null(read.Slot); Assert.Equal(source.ItemId, read.ItemId); Assert.Equal(source.Type, read.Type);
    }

    [Theory][InlineData(1)][InlineData(10)]
    public void V1ReceiptKeepsIndividualPieceIdentityOrderRarityAndDuplicateSemantics(int count)
    {
        var receipt = GachaSnapshotTests.Receipt(count);
        var before = receipt.ToBsonDocument(); before.Remove("presentationSnapshot");
        var types = CosmeticLoadoutPolicy.Slots.Select(s => "CHESS_" + s + "_SKIN").ToArray();
        var doc = receipt.ToBsonDocument();
        for (int index = 0; index < count; index++)
        {
            doc["presentationSnapshot"]["rewards"][index]["rawItemType"] = types[index];
            doc["presentationSnapshot"]["rewards"][index]["unityAssetKey"] = "fixture-only-local-key";
        }
        var read = BsonSerializer.Deserialize<GachaRoll>(doc);
        var response = GachaSnapshotTests.Map(read);
        var after = read.ToBsonDocument(); after.Remove("presentationSnapshot");
        Assert.Equal(before, after);
        Assert.Equal(types.Take(count), response.PresentationSnapshot!.Rewards.Select(r => r.RawItemType));
        Assert.Equal(receipt.Results.Select(r => r.ItemId.ToString()), response.Results.Select(r => r.ItemId));
        Assert.Equal(1, response.PresentationSnapshot.SchemaVersion);
    }
}
