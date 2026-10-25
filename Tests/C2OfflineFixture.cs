using ChessGame.Api.Models;
using ChessGame.Api.Services;
using MongoDB.Bson;
namespace ChessGame.Api.Tests;
// Version-pinned supplied snapshot. NOT production authority or real banner membership.
public static class C2OfflineFixture
{
    public static Item[] Items() => new[] { new Item { Id = ObjectId.Parse("6aacc041e50e17c25a690158"), Code = "CHESS_DEFAULT", Type = "CHESS_PIECE_SKIN", Rarity = "COMMON", UnityAssetKey = "ChessSets/Default", IsDefault = true, IsActive = true },
        new Item { Id = ObjectId.Parse("6aacc04be50e17c25a69015a"), Code = "BOARD_DEFAULT", Type = "BOARD_SKIN", Rarity = "COMMON", UnityAssetKey = "Boards/Default", IsDefault = true, IsActive = true },
        new Item { Id = ObjectId.Parse("6aacc056e50e17c25a69015c"), Code = "CHESS_WOODLAND", Type = "CHESS_PIECE_SKIN", Rarity = "COMMON", UnityAssetKey = "ChessSets/Woodland", IsDefault = false, IsActive = true },
        new Item { Id = ObjectId.Parse("6aacc062e50e17c25a69015e"), Code = "BOARD_CLASSIC_MARBLE", Type = "BOARD_SKIN", Rarity = "COMMON", UnityAssetKey = "Boards/ClassicMarble", IsDefault = false, IsActive = true },
        new Item { Id = ObjectId.Parse("6aacc06be50e17c25a690160"), Code = "CHESS_CRYSTAL_BLUE", Type = "CHESS_PIECE_SKIN", Rarity = "RARE", UnityAssetKey = "ChessSets/CrystalBlue", IsDefault = false, IsActive = true },
        new Item { Id = ObjectId.Parse("6aacc072e50e17c25a690162"), Code = "BOARD_FOREST_GARDEN", Type = "BOARD_SKIN", Rarity = "RARE", UnityAssetKey = "Boards/ForestGarden", IsDefault = false, IsActive = true },
        new Item { Id = ObjectId.Parse("6aacc07ae50e17c25a690164"), Code = "CHESS_SHADOW_KNIGHT", Type = "CHESS_PIECE_SKIN", Rarity = "EPIC", UnityAssetKey = "ChessSets/ShadowKnight", IsDefault = false, IsActive = true },
        new Item { Id = ObjectId.Parse("6aacc085e50e17c25a690166"), Code = "BOARD_VOLCANO", Type = "BOARD_SKIN", Rarity = "EPIC", UnityAssetKey = "Boards/Volcano", IsDefault = false, IsActive = true },
        new Item { Id = ObjectId.Parse("6aacc099e50e17c25a690168"), Code = "CHESS_DRAGON_RED", Type = "CHESS_PIECE_SKIN", Rarity = "LEGENDARY", UnityAssetKey = "ChessSets/DragonRed", IsDefault = false, IsActive = true },
        new Item { Id = ObjectId.Parse("6aacc0a0e50e17c25a69016a"), Code = "BOARD_CELESTIAL", Type = "BOARD_SKIN", Rarity = "LEGENDARY", UnityAssetKey = "Boards/Celestial", IsDefault = false, IsActive = true } };
    public static ItemArtworkCandidate[] Associations() => new[] { new ItemArtworkCandidate(ObjectId.Parse("6aacc041e50e17c25a690158"), "CHESS_PIECE_SKIN", "PIECE_SKIN", "ChessSets/Default", "tazji_lowpoly", "PieceSkin/tazji_lowpoly"),
        new ItemArtworkCandidate(ObjectId.Parse("6aacc04be50e17c25a69015a"), "BOARD_SKIN", "BOARD_SKIN", "Boards/Default", "tazji_lowpoly", "Board/tazji_lowpoly"),
        new ItemArtworkCandidate(ObjectId.Parse("6aacc056e50e17c25a69015c"), "CHESS_PIECE_SKIN", "PIECE_SKIN", "ChessSets/Woodland", "ChessSets/Woodland", "PieceSkin/ChessSets/Woodland"),
        new ItemArtworkCandidate(ObjectId.Parse("6aacc062e50e17c25a69015e"), "BOARD_SKIN", "BOARD_SKIN", "Boards/ClassicMarble", "Boards/ClassicMarble", "Board/Boards/ClassicMarble"),
        new ItemArtworkCandidate(ObjectId.Parse("6aacc06be50e17c25a690160"), "CHESS_PIECE_SKIN", "PIECE_SKIN", "ChessSets/CrystalBlue", "ChessSets/CrystalBlue", "PieceSkin/ChessSets/CrystalBlue"),
        new ItemArtworkCandidate(ObjectId.Parse("6aacc072e50e17c25a690162"), "BOARD_SKIN", "BOARD_SKIN", "Boards/ForestGarden", "Boards/ForestGarden", "Board/Boards/ForestGarden"),
        new ItemArtworkCandidate(ObjectId.Parse("6aacc07ae50e17c25a690164"), "CHESS_PIECE_SKIN", "PIECE_SKIN", "ChessSets/ShadowKnight", null, null),
        new ItemArtworkCandidate(ObjectId.Parse("6aacc085e50e17c25a690166"), "BOARD_SKIN", "BOARD_SKIN", "Boards/Volcano", null, null),
        new ItemArtworkCandidate(ObjectId.Parse("6aacc099e50e17c25a690168"), "CHESS_PIECE_SKIN", "PIECE_SKIN", "ChessSets/DragonRed", null, null),
        new ItemArtworkCandidate(ObjectId.Parse("6aacc0a0e50e17c25a69016a"), "BOARD_SKIN", "BOARD_SKIN", "Boards/Celestial", "Boards/Celestial", "Board/Boards/Celestial") };
    public static ItemPresentationPolicy Policy(ItemArtworkCandidate[]? associations = null) =>
        ItemPresentationPolicy.ForOfflineIntegrationCandidate("C2_OFFLINE_C17_V1",
            new[] { new ItemCategoryRegistration("PIECE_SKIN", "PieceSkin", true, false),
                new ItemCategoryRegistration("BOARD_SKIN", "Board", true, false) },
            new[] { new ItemTypeRegistration("CHESS_PIECE_SKIN", "PIECE_SKIN"),
                new ItemTypeRegistration("BOARD_SKIN", "BOARD_SKIN") }, associations ?? Associations());
}
