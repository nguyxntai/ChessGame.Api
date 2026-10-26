using System.Text.Json;
using ChessGame.Api.DTOs.Inventory;
using ChessGame.Api.Services;
using MongoDB.Bson;
using Xunit;

namespace ChessGame.Api.Tests;

internal static class C1Fixture
{
    internal static readonly ObjectId First = ObjectId.Parse("111111111111111111111101");
    internal static readonly ObjectId Second = ObjectId.Parse("111111111111111111111102");
    internal static readonly ObjectId Board = ObjectId.Parse("111111111111111111111103");
    internal static ItemPresentationPolicy Policy() => ItemPresentationPolicy.ForControlledFixture("C1_FIXTURE_V1",
        new[] { new ItemCategoryRegistration("FIXTURE_PIECE", "PieceSkin", true, true),
            new ItemCategoryRegistration("FIXTURE_BOARD", "Board", true, true),
            new ItemCategoryRegistration("FIXTURE_ENVIRONMENT", "Environment", false, false),
            new ItemCategoryRegistration("FIXTURE_CONSUMABLE", null, false, false),
            new ItemCategoryRegistration("FIXTURE_DISPLAY_ONLY", "PieceSkin", true, false) },
        new[] { new ItemTypeRegistration("FIXTURE_PIECE_TYPE", "FIXTURE_PIECE"),
            new ItemTypeRegistration("FIXTURE_BOARD_TYPE", "FIXTURE_BOARD"),
            new ItemTypeRegistration("FIXTURE_ENVIRONMENT_TYPE", "FIXTURE_ENVIRONMENT"),
            new ItemTypeRegistration("FIXTURE_CONSUMABLE_TYPE", "FIXTURE_CONSUMABLE"),
            new ItemTypeRegistration("FIXTURE_DISPLAY_ONLY_TYPE", "FIXTURE_DISPLAY_ONLY") },
        new[] { new ItemArtworkRegistration(First, "FIXTURE_PIECE", "fixture/shared"),
            new ItemArtworkRegistration(Second, "FIXTURE_PIECE", "fixture/shared"),
            new ItemArtworkRegistration(Board, "FIXTURE_BOARD", "fixture/shared") });
    internal static void Save(string name, object value)
    {
        var folder = Environment.GetEnvironmentVariable("C1_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, name),
            JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }
}

public sealed class C1ItemAuthorityTests
{
    [Theory]
    [InlineData("FIXTURE_PIECE_TYPE", "SUPPORTED", true)]
    [InlineData("FIXTURE_BOARD_TYPE", "SUPPORTED", true)]
    [InlineData("FIXTURE_ENVIRONMENT_TYPE", "UNSUPPORTED", false)]
    [InlineData("FIXTURE_CONSUMABLE_TYPE", "UNSUPPORTED", false)]
    [InlineData("FIXTURE_DISPLAY_ONLY_TYPE", "SUPPORTED", false)]
    public void EveryRegisteredFixtureTypeHasExplicitSupportAndEquipState(string type, string state, bool equip)
    {
        var result = C1Fixture.Policy().Classify(type);
        Assert.Equal(state, result.State); Assert.Equal(equip, result.IsEquippable); Assert.NotNull(result.Category);
    }

    [Theory]
    [InlineData("UNKNOWN_CHESS_BOARD")][InlineData("CHESS_SKIN")][InlineData("BOARD_SKIN")]
    [InlineData("LEGENDARY")][InlineData("fixture_piece_type")][InlineData(" FIXTURE_PIECE_TYPE")]
    [InlineData("FIXTURE_PIECE_TYPE ")][InlineData("RAW:unapproved")][InlineData("MOVE_EFFECT")]
    public void UnknownOrCaseAndWhitespaceVariationNeverInfersCategory(string raw)
    {
        var type = C1Fixture.Policy().Classify(raw);
        Assert.Equal("UNKNOWN", type.State); Assert.Null(type.Category); Assert.Null(type.IsEquippable);
        var result = C1Fixture.Policy().ResolveLive(C1Fixture.First.ToString(), raw, "fixture/shared");
        Assert.Equal(ItemPresentationStatus.UnknownItemType, result.ResolutionStatus); Assert.Null(result.ApprovedUnityAssetKey);
    }

    [Theory][InlineData(null)][InlineData("")][InlineData(" ")][InlineData("\t")][InlineData("X\0Y")]
    public void MissingOrMalformedTypeIsNotAuthority(string? raw)
    {
        var result = C1Fixture.Policy().ResolveLive(C1Fixture.First.ToString(), raw, "fixture/shared");
        Assert.Equal(ItemPresentationStatus.MissingOrInvalidMetadata, result.ResolutionStatus); Assert.Null(result.Category);
    }

    [Fact]
    public void ProductionPolicyHasNoInventedRegistrationsOrFixtureActivation()
    {
        Assert.Empty(ItemPresentationPolicy.Current.RegisteredRawTypes);
        foreach (var raw in C1Fixture.Policy().RegisteredRawTypes)
            Assert.Equal("UNKNOWN", ItemPresentationPolicy.Current.Classify(raw).State);
        var result = ItemPresentationPolicy.Current.ResolveLive(C1Fixture.First.ToString(), "CHESS", "tazji_lowpoly");
        Assert.Equal("NO_APPROVED_MANIFEST", result.ApprovalScope); Assert.Null(result.ManifestVersion);
        Assert.Null(result.Category); Assert.Null(result.ApprovedUnityAssetKey);
        C1Fixture.Save("production-policy.json", result);
    }

    [Fact]
    public void ExplicitAssociationPreservesDifferentIdsSharingOneKeyAndCategory()
    {
        var policy = C1Fixture.Policy();
        var rows = new[] { policy.ResolveLive(C1Fixture.First.ToString(), "FIXTURE_PIECE_TYPE", "fixture/shared"),
            policy.ResolveLive(C1Fixture.Second.ToString(), "FIXTURE_PIECE_TYPE", "fixture/shared"),
            policy.ResolveLive(C1Fixture.Board.ToString(), "FIXTURE_BOARD_TYPE", "fixture/shared") };
        Assert.All(rows, r => { Assert.Equal(ItemPresentationStatus.Approved, r.ResolutionStatus);
            Assert.Equal("CONTROLLED_FIXTURE", r.ApprovalScope); Assert.Equal("C1_FIXTURE_V1", r.ManifestVersion);
            Assert.Equal("UNVERIFIED", r.UnityAssetAvailability); Assert.False(r.IsProductionArtworkVerified); });
        Assert.Equal(3, rows.Select(r => r.ItemId).Distinct().Count());
        Assert.Single(rows.Select(r => r.ApprovedUnityAssetKey).Distinct());
        Assert.Equal(2, rows.Select(r => r.Category).Distinct().Count());
        C1Fixture.Save("fixture-approved-associations.json", rows);
    }

    [Theory][InlineData(null)][InlineData("")][InlineData(" ")]
    public void MissingKeysAreNotSubstituted(string? key) => AssertResolution(key, ItemPresentationStatus.MissingAssetKey);

    [Theory][InlineData("../outside")][InlineData("a/../b")][InlineData("/absolute")]
    [InlineData("a//b")][InlineData("https://x")][InlineData("a\\b")][InlineData("a b")][InlineData("a\0b")]
    public void MalformedKeysAreNotCertified(string key) => AssertResolution(key, ItemPresentationStatus.MissingOrInvalidMetadata);

    [Theory][InlineData("fixture/SHARED")][InlineData("other/key")]
    public void UnregisteredKeysAreNotCertified(string key) => AssertResolution(key, ItemPresentationStatus.UnapprovedAssetKey);

    private static void AssertResolution(string? key, string expected)
    {
        var result = C1Fixture.Policy().ResolveLive(C1Fixture.First.ToString(), "FIXTURE_PIECE_TYPE", key);
        Assert.Equal(expected, result.ResolutionStatus); Assert.Null(result.ApprovedUnityAssetKey);
        Assert.Equal(C1Fixture.First.ToString(), result.ItemId);
    }

    [Theory][InlineData(null)][InlineData("invalid")][InlineData("000000000000000000000000")]
    public void MissingIdentityCannotBeReplacedByKey(string? id)
    {
        var r = C1Fixture.Policy().ResolveLive(id, "FIXTURE_PIECE_TYPE", "fixture/shared");
        Assert.Equal(ItemPresentationStatus.MissingOrInvalidMetadata, r.ResolutionStatus); Assert.Null(r.ApprovedUnityAssetKey);
    }

    [Fact]
    public void WrongItemOrCategoryCannotBorrowAnApprovedKey()
    {
        var policy = C1Fixture.Policy();
        Assert.Equal(ItemPresentationStatus.UnapprovedAssetKey, policy.ResolveLive(ObjectId.GenerateNewId().ToString(), "FIXTURE_PIECE_TYPE", "fixture/shared").ResolutionStatus);
        Assert.Equal(ItemPresentationStatus.UnapprovedAssetKey, policy.ResolveLive(C1Fixture.First.ToString(), "FIXTURE_BOARD_TYPE", "fixture/shared").ResolutionStatus);
        Assert.Equal(ItemPresentationStatus.UnsupportedCategory, policy.ResolveLive(C1Fixture.First.ToString(), "FIXTURE_CONSUMABLE_TYPE", "fixture/shared").ResolutionStatus);
    }

    [Theory][InlineData("category")][InlineData("type")][InlineData("artwork")][InlineData("unbound")]
    [InlineData("version")][InlineData("unsupported-art")]
    public void AmbiguousOrInvalidManifestsAreRejected(string problem)
    {
        var cats = new List<ItemCategoryRegistration> { new("FIXTURE", "PieceSkin", true, true) };
        var types = new List<ItemTypeRegistration> { new("fixture", "FIXTURE") };
        var art = new List<ItemArtworkRegistration> { new(C1Fixture.First, "FIXTURE", "fixture/key") };
        if (problem == "category") cats.Add(cats[0]);
        if (problem == "type") types.Add(types[0]);
        if (problem == "artwork") art.Add(art[0]);
        if (problem == "unbound") types[0] = new("fixture", "OTHER");
        if (problem == "unsupported-art") cats[0] = new("FIXTURE", "Environment", false, false);
        Assert.Throws<ArgumentException>(() => ItemPresentationPolicy.ForControlledFixture(problem == "version" ? " " : "FIXTURE_V1", cats, types, art));
    }

    [Fact]
    public void RegistryDefensivelyCopiesAndReadDtosCannotAcceptForgedAuthority()
    {
        var types = new List<ItemTypeRegistration> { new("fixture", "FIXTURE") };
        var p = ItemPresentationPolicy.ForControlledFixture("FIXTURE_V1", new[] { new ItemCategoryRegistration("FIXTURE", "PieceSkin", true, false) }, types, Array.Empty<ItemArtworkRegistration>());
        types.Clear(); Assert.Single(p.RegisteredRawTypes); Assert.False(p.Classify("fixture").IsEquippable);
        var json = """{"itemId":"111111111111111111111101","type":"CHESS_SKIN","unityAssetKey":"tazji_lowpoly","presentationAuthority":{"resolutionStatus":"APPROVED","category":"FORGED","approvedUnityAssetKey":"tazji_lowpoly"}}""";
        var dto = JsonSerializer.Deserialize<ItemCatalogResponse>(json, GachaSnapshotTests.WebJson)!;
        Assert.Equal(ItemPresentationStatus.UnknownItemType, dto.PresentationAuthority.ResolutionStatus);
        Assert.Null(dto.PresentationAuthority.Category); Assert.Null(dto.PresentationAuthority.ApprovedUnityAssetKey);
    }
}
