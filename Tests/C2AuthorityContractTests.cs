using System.Text.Json;
using ChessGame.Api.DTOs.Inventory;
using ChessGame.Api.Services;
using MongoDB.Bson;
using Xunit;

namespace ChessGame.Api.Tests;

public sealed class C2AuthorityContractTests
{
    [Theory]
    [InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)]
    [InlineData(5)][InlineData(6)][InlineData(7)][InlineData(8)][InlineData(9)]
    public void TenPinnedItemsAreCandidatesOrExplicitMissingNeverApproved(int index)
    {
        var item = C2OfflineFixture.Items()[index]; var association = C2OfflineFixture.Associations()[index];
        var r = C2OfflineFixture.Policy().ResolveLive(item.Id.ToString(), item.Type, item.UnityAssetKey);
        Assert.Equal(item.Id.ToString(), r.ItemId);
        Assert.Equal(item.Type == "CHESS_PIECE_SKIN" ? "PIECE_SKIN" : "BOARD_SKIN", r.Category);
        Assert.Equal(item.Type == "CHESS_PIECE_SKIN" ? "PieceSkin" : "Board", r.UnityPresentationCategory);
        Assert.Equal(association.UnityLocalCatalogId is null ? "MISSING_UNITY_CATALOG_ENTRY" : "INTEGRATION_CANDIDATE", r.ResolutionStatus);
        Assert.Equal(association.UnityLocalCatalogId, r.CandidateUnityLocalCatalogId);
        Assert.Equal(association.UnityAddressablesAddress, r.CandidateUnityAddressablesAddress);
        Assert.Null(r.ApprovedUnityAssetKey); Assert.Null(r.IsEquippable);
        Assert.Equal("INTEGRATION_CANDIDATE", r.CategoryAuthorityStatus);
        Assert.Equal("TEAM_AUTHORITY_PENDING", r.TeamAuthorityStatus);
        Assert.Equal("RUNTIME_NOT_TESTED", r.RuntimeVerificationStatus);
        Assert.Equal("PRODUCTION_BLOCKED", r.ProductionStatus); Assert.False(r.IsProductionArtworkVerified);
        Assert.Equal("OFFLINE_INTEGRATION_INPUT", r.MetadataSource);
    }

    [Fact]
    public void AliasKeysDoNotBecomeIdentityAndRarityDisagreementsAreNotCorrected()
    {
        var p = C2OfflineFixture.Policy(); var items = C2OfflineFixture.Items();
        var a = p.ResolveLive(items[0].Id.ToString(), items[0].Type, items[0].UnityAssetKey);
        var b = p.ResolveLive(items[1].Id.ToString(), items[1].Type, items[1].UnityAssetKey);
        Assert.Equal("tazji_lowpoly", a.CandidateUnityLocalCatalogId); Assert.Equal(a.CandidateUnityLocalCatalogId, b.CandidateUnityLocalCatalogId);
        Assert.NotEqual(a.ItemId, b.ItemId); Assert.NotEqual(a.Category, b.Category);
        Assert.Equal("PieceSkin/tazji_lowpoly", a.CandidateUnityAddressablesAddress);
        Assert.Equal("Board/tazji_lowpoly", b.CandidateUnityAddressablesAddress);
        Assert.All(new[] { items[4], items[5] }, i => Assert.Equal("RARE", i.Rarity));
        Assert.Equal(7, items.Count(i => p.ResolveLive(i.Id.ToString(), i.Type, i.UnityAssetKey).ResolutionStatus == "INTEGRATION_CANDIDATE"));
        Assert.Equal(3, items.Count(i => p.ResolveLive(i.Id.ToString(), i.Type, i.UnityAssetKey).ResolutionStatus == "MISSING_UNITY_CATALOG_ENTRY"));
    }

    [Theory]
    [InlineData("CHESS")][InlineData("SOME_CHESS_PIECE_SKIN")][InlineData("BOARD")]
    [InlineData("chess_piece_skin")][InlineData(" CHESS_PIECE_SKIN")][InlineData("CHESS_PIECE_SKIN ")]
    [InlineData("LEGENDARY")][InlineData("CHESS_DEFAULT")][InlineData("")][InlineData(null)][InlineData("X\0Y")]
    public void NoTypeGuessing(string? raw)
    {
        var r = C2OfflineFixture.Policy().ResolveLive(C2OfflineFixture.Items()[0].Id.ToString(), raw, "ChessSets/Default");
        Assert.Null(r.Category); Assert.Null(r.CandidateUnityLocalCatalogId); Assert.Null(r.ApprovedUnityAssetKey);
    }

    [Theory][InlineData("type")][InlineData("category")][InlineData("raw-key")][InlineData("address")]
    [InlineData("local")][InlineData("half-missing")][InlineData("duplicate")][InlineData("identity")]
    public void ConflictingOrMalformedCandidatesFailAtConstruction(string problem)
    {
        var a = C2OfflineFixture.Associations();
        a[0] = problem switch {
            "type" => a[0] with { ExactRawType = "UNKNOWN" },
            "category" => a[0] with { Category = "BOARD_SKIN" },
            "raw-key" => a[0] with { ExactRawAssetKey = "../guess" },
            "address" => a[0] with { UnityAddressablesAddress = "Board/tazji_lowpoly" },
            "local" => a[0] with { UnityLocalCatalogId = "../guess" },
            "half-missing" => a[0] with { UnityLocalCatalogId = null },
            "identity" => a[0] with { ItemId = ObjectId.Empty }, _ => a[0] };
        if (problem == "duplicate") a = a.Append(a[0]).ToArray();
        Assert.Throws<ArgumentException>(() => C2OfflineFixture.Policy(a));
    }

    [Fact]
    public void DifferentRawTypeInSameCategoryCannotBorrowCandidateAssociation()
    {
        var first = C2OfflineFixture.Associations()[0];
        var p = ItemPresentationPolicy.ForOfflineIntegrationCandidate("SYNTHETIC_SAME_CATEGORY",
            new[] { new ItemCategoryRegistration("PIECE_SKIN", "PieceSkin", true, false) },
            new[] { new ItemTypeRegistration("CHESS_PIECE_SKIN", "PIECE_SKIN"), new ItemTypeRegistration("SYNTHETIC_OTHER", "PIECE_SKIN") }, new[] { first });
        Assert.Equal("UNAPPROVED_ASSET_KEY", p.ResolveLive(first.ItemId.ToString(), "SYNTHETIC_OTHER", first.ExactRawAssetKey).ResolutionStatus);
        Assert.Equal("UNAPPROVED_ASSET_KEY", p.ResolveLive(first.ItemId.ToString(), first.ExactRawType, "Boards/Default").ResolutionStatus);
        Assert.Equal("UNAPPROVED_ASSET_KEY", p.ResolveLive(ObjectId.GenerateNewId().ToString(), first.ExactRawType, first.ExactRawAssetKey).ResolutionStatus);
    }

    [Fact]
    public void CandidateRegistryCopiesInputAndSharedKeysDoNotMergeDistinctIdentities()
    {
        var rows = C2OfflineFixture.Associations();
        rows[2] = rows[0] with { ItemId = rows[2].ItemId };
        var p = C2OfflineFixture.Policy(rows); var first = rows[0]; var second = rows[2];
        rows[0] = rows[0] with { ExactRawAssetKey = "mutated/key" };
        var a = p.ResolveLive(first.ItemId.ToString(), first.ExactRawType, first.ExactRawAssetKey);
        var b = p.ResolveLive(second.ItemId.ToString(), second.ExactRawType, second.ExactRawAssetKey);
        Assert.NotEqual(a.ItemId, b.ItemId); Assert.Equal(a.CandidateUnityAddressablesAddress, b.CandidateUnityAddressablesAddress);
        Assert.Equal("INTEGRATION_CANDIDATE", a.ResolutionStatus); Assert.Equal(a.ResolutionStatus, b.ResolutionStatus);
        Assert.Null(a.ApprovedUnityAssetKey); Assert.Null(b.ApprovedUnityAssetKey);
        Assert.Equal("UNAPPROVED_ASSET_KEY", p.ResolveLive(first.ItemId.ToString(), first.ExactRawType, "mutated/key").ResolutionStatus);
    }

    [Fact]
    public void CandidateConstructionCannotActivateCurrentOrBackfillV1AndDtosIgnoreForgedFields()
    {
        _ = C2OfflineFixture.Policy(); Assert.Empty(ItemPresentationPolicy.Current.RegisteredRawTypes);
        var first = C2OfflineFixture.Items()[0];
        var dto = new ItemCatalogResponse { ItemId = first.Id.ToString(), Type = first.Type, UnityAssetKey = first.UnityAssetKey };
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("PRODUCTION_BLOCKED", json); Assert.DoesNotContain("PieceSkin/tazji_lowpoly", json);
        Assert.Null(dto.PresentationAuthority.CandidateUnityLocalCatalogId);
        var historic = ItemPresentationPolicy.ForV1Receipt(first.Id.ToString());
        Assert.Equal("NO_STORED_AUTHORITY", historic.ApprovalScope); Assert.Null(historic.Category); Assert.Null(historic.CandidateUnityLocalCatalogId);
        var forged = JsonSerializer.Deserialize<ItemCatalogResponse>("""{"itemId":"6aacc041e50e17c25a690158","type":"CHESS_PIECE_SKIN","unityAssetKey":"ChessSets/Default","presentationAuthority":{"candidateUnityLocalCatalogId":"forged","approvedUnityAssetKey":"forged"}}""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Null(forged.PresentationAuthority.ApprovedUnityAssetKey); Assert.Null(forged.PresentationAuthority.CandidateUnityLocalCatalogId);
    }
}
