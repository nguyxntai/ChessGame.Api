using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ChessGame.Api.DTOs.Gacha;
using ChessGame.Api.Services;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Xunit;

namespace ChessGame.Api.Tests;

public sealed class C1ReceiptCompatibilityTests
{
    [Theory][InlineData(1)][InlineData(10)]
    public void V1BsonAndEconomicsDoNotStoreOrInferNewAuthority(int count)
    {
        var receipt = GachaSnapshotTests.Receipt(count); var before = receipt.ToBson();
        var read = BsonSerializer.Deserialize<ChessGame.Api.Models.GachaRoll>(before); var response = GachaSnapshotTests.Map(read);
        Assert.Equal(before, read.ToBson()); Assert.Equal(1, response.PresentationSnapshot!.SchemaVersion);
        Assert.DoesNotContain("presentationAuthority", receipt.ToJson());
        Assert.All(response.PresentationSnapshot.Rewards, r => {
            Assert.Equal(ItemPresentationStatus.HistoricalSnapshotUnavailable, r.PresentationAuthority.ResolutionStatus);
            Assert.Null(r.PresentationAuthority.Category); Assert.Null(r.PresentationAuthority.PolicyVersion);
            Assert.Null(r.PresentationAuthority.ApprovedUnityAssetKey); Assert.Equal("NO_STORED_AUTHORITY", r.PresentationAuthority.ApprovalScope); });
        Assert.Equal(receipt.Results.Select(r => r.ItemId.ToString()), response.Results.Select(r => r.ItemId));
        Assert.Equal(long.MaxValue, response.Wallet.Golds); Assert.All(response.Results, r => Assert.Equal(long.MaxValue, r.DuplicateReward!.Amount));
        C1Fixture.Save("receipt-v1-x" + count + ".json", response);
    }

    [Theory][InlineData(1)][InlineData(10)]
    public async Task NativeRollHistoryAndRecoveryPreserveRawSnapshotAfterCatalogMutationAndDeletion(int count)
    {
        await using var f = await UnknownHttpFixture.Create();
        var request = f.Store.Request(count);
        var posted = await f.Http.PostAsJsonAsync("/api/gacha/roll", request); Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        var initial = (await posted.Content.ReadFromJsonAsync<GachaRollResponse>())!;
        var storedBefore = f.Store.Stored!.ToBson(); var callsBefore = f.Store.Calls.ToArray();
        var initialAuthority = JsonSerializer.Serialize(initial.PresentationSnapshot, GachaSnapshotTests.WebJson);
        f.Store.Legendary.Type = "FIXTURE_PIECE_TYPE"; f.Store.Legendary.UnityAssetKey = "fixture/shared"; f.Store.Items.Clear();
        var recovery = await f.Http.GetAsync("/api/gacha/rolls/by-request/" + request.RequestId);
        Assert.Equal(HttpStatusCode.OK, recovery.StatusCode);
        var recovered = (await recovery.Content.ReadFromJsonAsync<GachaRollResponse>())!;
        var history = await f.Http.GetFromJsonAsync<GachaHistoryResponse>("/api/gacha/history");
        Assert.Equal(initialAuthority, JsonSerializer.Serialize(recovered.PresentationSnapshot, GachaSnapshotTests.WebJson));
        Assert.Equal(initialAuthority, JsonSerializer.Serialize(Assert.Single(history!.Items).PresentationSnapshot, GachaSnapshotTests.WebJson));
        Assert.Equal(storedBefore, f.Store.Stored.ToBson());
        Assert.Equal(callsBefore.Count(c => c == "items.find.session"), f.Store.Calls.Count(c => c == "items.find.session"));
        Assert.Equal(callsBefore.Count(c => c == "wallet.debit"), f.Store.Calls.Count(c => c == "wallet.debit"));
        Assert.Equal(1, f.Posts); Assert.Equal(100 - count, recovered.Wallet.Tickets);
        Assert.All(recovered.PresentationSnapshot!.Rewards, r => Assert.Equal(ItemPresentationStatus.HistoricalSnapshotUnavailable, r.PresentationAuthority.ResolutionStatus));
        C1Fixture.Save("native-history-recovery-x" + count + ".json", new { initial, recovered, history, calls = f.Store.Calls });
    }

    [Theory][InlineData(1, "missing")][InlineData(10, "missing")]
    [InlineData(1, "invalid")][InlineData(10, "invalid")][InlineData(1, "future-version")][InlineData(10, "future-version")]
    public async Task MissingCorruptOrFutureOptionalMetadataDoesNotPoisonRecovery(int count, string variant)
    {
        await using var f = new GachaRecoveryFixture(count);
        if (variant == "missing") f.Rows[0].Remove("presentationSnapshot");
        else f.Rows[0]["presentationSnapshot"] = variant == "future-version"
            ? new BsonDocument { { "schemaVersion", 2 }, { "category", "FORGED" } } : new BsonDocument("rewards", "bad");
        var before = f.State(); await f.StartApi(); var response = await f.Http.GetAsync(GachaRecoveryFixture.PathFor());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<GachaRollResponse>())!;
        Assert.Equal(count, dto.Results.Count); Assert.Null(dto.PresentationSnapshot); Assert.Equal(before, f.State()); f.AssertRecoveryOnly();
    }

    [Fact]
    public void RepeatedIdentitiesAndSharedArtworkStayInSlotOrderWithoutCertification()
    {
        var receipt = GachaSnapshotTests.Receipt(10);
        receipt.Results[1].ItemId = receipt.Results[0].ItemId; receipt.Results[1].ItemCode = receipt.Results[0].ItemCode;
        var doc = receipt.ToBsonDocument();
        for (var i = 0; i < 10; i++)
        {
            var row = doc["presentationSnapshot"]["rewards"][i].AsBsonDocument;
            row["itemId"] = receipt.Results[i].ItemId; row["itemCode"] = receipt.Results[i].ItemCode;
            row["rawItemType"] = "FIXTURE_PIECE_TYPE"; row["unityAssetKey"] = "fixture/shared";
        }
        var response = GachaSnapshotTests.Map(BsonSerializer.Deserialize<ChessGame.Api.Models.GachaRoll>(doc)); Assert.Equal(10, response.Results.Count);
        var rows = response.PresentationSnapshot!.Rewards; Assert.Equal(Enumerable.Range(0, 10), rows.Select(r => r.SlotIndex));
        Assert.Equal(9, rows.Select(r => r.ItemId).Distinct().Count()); Assert.Single(rows.Select(r => r.UnityAssetKey).Distinct());
        Assert.All(rows, r => Assert.Null(r.PresentationAuthority.ApprovedUnityAssetKey));
    }

    [Fact]
    public void ExistingV1ReaderIgnoresAdditiveAuthorityWithoutBsonUpgrade()
    {
        var response = GachaSnapshotTests.Map(GachaSnapshotTests.Receipt());
        var node = JsonSerializer.SerializeToNode(response, GachaSnapshotTests.WebJson)!;
        Assert.NotNull(node["presentationSnapshot"]!["rewards"]![0]!["presentationAuthority"]);
        node["presentationSnapshot"]!["rewards"]![0]!["presentationAuthority"] = new JsonObject { ["resolutionStatus"] = "APPROVED", ["category"] = "FORGED" };
        var read = JsonSerializer.Deserialize<GachaRollResponse>(node.ToJsonString(), GachaSnapshotTests.WebJson)!;
        Assert.Null(read.PresentationSnapshot!.Rewards[0].PresentationAuthority.Category);
        Assert.Equal(ItemPresentationStatus.HistoricalSnapshotUnavailable, read.PresentationSnapshot.Rewards[0].PresentationAuthority.ResolutionStatus);
    }
}
