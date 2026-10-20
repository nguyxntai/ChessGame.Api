using System.Reflection;
using System.Text.Json;
using B = ChessGame.Api.DTOs.Gacha;
using ChessGame.Api.Models;
using ChessGame.Api.Services;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Xunit;

namespace ChessGame.Api.Tests;

public sealed class GachaSnapshotTests
{
    internal static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    internal static GachaRoll Receipt(int count = 1)
    {
        var rows = Enumerable.Range(0, count).Select(i => new GachaRollReward
        {
            ItemId = ObjectId.GenerateNewId(), ItemCode = $"test-{i}", ItemName = $"Name {i}",
            Rarity = "LEGENDARY", IsDuplicate = true,
            DuplicateReward = new GachaDuplicateReward { Currency = "GOLDS", Amount = long.MaxValue }
        }).ToList();
        return new GachaRoll
        {
            UserId = ObjectId.GenerateNewId(), RequestId = Guid.NewGuid().ToString("D"),
            CreatedAt = DateTime.UnixEpoch.AddMilliseconds(123456789),
            BannerCode = "TEST", Count = count, Results = rows, Currency = "TICKETS", TotalCost = count,
            WalletAfter = new GachaWalletSnapshot { Golds = long.MaxValue, Tickets = 99 },
            PityAfter = new GachaPitySnapshot { Epic = 1, Legendary = 2 },
            PityLimit = new GachaPityLimits { Epic = 3, Legendary = 5 },
            PresentationSnapshot = new GachaPresentationSnapshot(rows.Select((r, i) =>
                new GachaRewardMetadataSnapshot(i, r.ItemId, r.ItemCode, "Unapproved raw TYPE", "shared-key")))
        };
    }

    internal static B.GachaRollResponse Map(GachaRoll roll) =>
        (B.GachaRollResponse)typeof(GachaService).GetMethod("ToRollResponse",
            BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { roll })!;

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void BsonRoundTripPreservesOrderedSnapshotAndExistingValues(int count)
    {
        var roll = Receipt(count);
        var stored = BsonSerializer.Deserialize<GachaRoll>(roll.ToBson());
        var mapped = Map(stored);
        Assert.Equal(JsonSerializer.Serialize(Map(roll), WebJson), JsonSerializer.Serialize(mapped, WebJson));
        Assert.Equal(1, mapped.PresentationSnapshot!.SchemaVersion);
        Assert.Equal("TRANSACTION_ITEM_READ", mapped.PresentationSnapshot.MetadataSource);
        Assert.Null(mapped.PresentationSnapshot.CatalogVersion);
        Assert.Equal(Enumerable.Range(0, count), mapped.PresentationSnapshot.Rewards.Select(r => r.SlotIndex));
        Assert.Equal(roll.Results.Select(r => r.ItemId.ToString()), mapped.PresentationSnapshot.Rewards.Select(r => r.ItemId));
        Assert.All(mapped.Results, r => Assert.Equal(long.MaxValue, r.DuplicateReward!.Amount));
    }

    [Fact]
    public void SnapshotDefensivelyCopiesRowsAndDoesNotAliasResponse()
    {
        var rows = Receipt().PresentationSnapshot!.Rewards.ToList();
        var snapshot = new GachaPresentationSnapshot(rows);
        rows.Clear();
        Assert.Single(snapshot.Rewards);
        Assert.True(((ICollection<GachaRewardMetadataSnapshot>)snapshot.Rewards).IsReadOnly);
        var roll = Receipt();
        var response = Map(roll);
        response.PresentationSnapshot!.Rewards[0].RawItemType = "client edit";
        response.PresentationSnapshot.Rewards.Clear();
        Assert.Equal("Unapproved raw TYPE", Map(roll).PresentationSnapshot!.Rewards[0].RawItemType);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData(" UNKNOWN_CHESS_BOARD ", " ")]
    [InlineData("raw-category-A", "same/key")]
    public void PartialOrUnknownMetadataIsPreservedWithoutClassification(string? type, string? key)
    {
        var doc = Receipt().ToBsonDocument();
        var row = doc["presentationSnapshot"]["rewards"][0].AsBsonDocument;
        row["rawItemType"] = type is null ? BsonNull.Value : new BsonString(type);
        row["unityAssetKey"] = key is null ? BsonNull.Value : new BsonString(key);
        var response = Map(BsonSerializer.Deserialize<GachaRoll>(doc));
        Assert.Equal(type, response.PresentationSnapshot!.Rewards[0].RawItemType);
        Assert.Equal(key, response.PresentationSnapshot.Rewards[0].UnityAssetKey);
        Assert.Null(typeof(B.GachaRewardMetadataResponse).GetProperty("Category"));
    }

    [Fact]
    public void MissingMetadataFieldsRemainNull_NotFabricated()
    {
        var doc = Receipt().ToBsonDocument();
        var row = doc["presentationSnapshot"]["rewards"][0].AsBsonDocument;
        row.Remove("rawItemType"); row.Remove("unityAssetKey");
        var metadata = Map(BsonSerializer.Deserialize<GachaRoll>(doc)).PresentationSnapshot!;
        Assert.Null(metadata.Rewards[0].RawItemType);
        Assert.Null(metadata.Rewards[0].UnityAssetKey);
    }

    [Fact]
    public void SharedArtworkKeysDoNotCollapseDifferentItemIdentities()
    {
        var roll = Receipt(10);
        var doc = roll.ToBsonDocument();
        doc["presentationSnapshot"]["rewards"][1]["rawItemType"] = "different-unapproved-type";
        roll = BsonSerializer.Deserialize<GachaRoll>(doc);
        var mapped = Map(roll).PresentationSnapshot!;
        Assert.Single(mapped.Rewards.Select(r => r.UnityAssetKey).Distinct());
        Assert.Equal(2, mapped.Rewards.Select(r => r.RawItemType).Distinct().Count());
        Assert.Equal(10, mapped.Rewards.Select(r => r.ItemId).Distinct().Count());
    }

    [Fact]
    public void HistoricalReceiptWithoutSnapshotDeserializesAndMaps()
    {
        var doc = Receipt().ToBsonDocument(); doc.Remove("presentationSnapshot");
        var historical = BsonSerializer.Deserialize<GachaRoll>(doc);
        Assert.Null(historical.PresentationSnapshot);
        Assert.Null(Map(historical).PresentationSnapshot);
        Assert.False(historical.ToBsonDocument().Contains("presentationSnapshot"));
        var json = JsonSerializer.Serialize(Map(historical), WebJson);
        var read = JsonSerializer.Deserialize<B.GachaRollResponse>(json, WebJson)!;
        Assert.Null(read.PresentationSnapshot);
        var oldJson = JsonSerializer.SerializeToNode(Map(historical), WebJson)!;
        oldJson.AsObject().Remove("presentationSnapshot");
        Assert.Null(JsonSerializer.Deserialize<B.GachaRollResponse>(oldJson.ToJsonString(), WebJson)!.PresentationSnapshot);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("scalar")]
    [InlineData("array")]
    [InlineData("empty")]
    [InlineData("version")]
    [InlineData("version-type")]
    [InlineData("source")]
    [InlineData("catalog")]
    [InlineData("rows-null")]
    [InlineData("row-scalar")]
    [InlineData("slot")]
    [InlineData("id")]
    [InlineData("code-type")]
    [InlineData("type")]
    [InlineData("key")]
    [InlineData("count")]
    [InlineData("id-correlation")]
    [InlineData("code-correlation")]
    public void MalformedOptionalSnapshotCannotPoisonEconomicReceipt(string damage)
    {
        var roll = Receipt(); var doc = roll.ToBsonDocument();
        var snapshot = doc["presentationSnapshot"].AsBsonDocument;
        var row = snapshot["rewards"][0].AsBsonDocument;
        switch (damage)
        {
            case "null": doc["presentationSnapshot"] = BsonNull.Value; break;
            case "scalar": doc["presentationSnapshot"] = "invalid"; break;
            case "array": doc["presentationSnapshot"] = new BsonArray(); break;
            case "empty": doc["presentationSnapshot"] = new BsonDocument(); break;
            case "version": snapshot["schemaVersion"] = 2; break;
            case "version-type": snapshot["schemaVersion"] = "1"; break;
            case "source": snapshot["metadataSource"] = "CURRENT_CATALOG"; break;
            case "catalog": snapshot["catalogVersion"] = "invented"; break;
            case "rows-null": snapshot["rewards"] = BsonNull.Value; break;
            case "row-scalar": snapshot["rewards"][0] = "invalid"; break;
            case "slot": row["slotIndex"] = 1; break;
            case "id": row["itemId"] = "not-object-id"; break;
            case "code-type": row["itemCode"] = 1; break;
            case "type": row["rawItemType"] = new BsonDocument(); break;
            case "key": row["unityAssetKey"] = new BsonArray(); break;
            case "count": snapshot["rewards"] = new BsonArray(); break;
            case "id-correlation": row["itemId"] = ObjectId.GenerateNewId(); break;
            case "code-correlation": row["itemCode"] = "another-item"; break;
        }
        var mapped = Map(BsonSerializer.Deserialize<GachaRoll>(doc));
        Assert.Null(mapped.PresentationSnapshot);
        Assert.Equal(roll.RequestId, mapped.RequestId);
        Assert.Equal(long.MaxValue, mapped.Results[0].DuplicateReward!.Amount);
        Assert.Equal(roll.Results[0].ItemId.ToString(), mapped.Results[0].ItemId);
    }

    [Fact]
    public void UnknownAdditiveFieldsInKnownSchemaAreIgnored()
    {
        var doc = Receipt().ToBsonDocument();
        doc["presentationSnapshot"]["future"] = true;
        doc["presentationSnapshot"]["rewards"][0]["future"] = "extra";
        Assert.NotNull(Map(BsonSerializer.Deserialize<GachaRoll>(doc)).PresentationSnapshot);
    }

    [Fact]
    public void SnapshotNeverAddsFieldsToExistingRewardDocuments()
    {
        var doc = Receipt().ToBsonDocument();
        Assert.Equal(new[] { "duplicateReward", "isDuplicate", "itemCode", "itemId", "itemName", "rarity" },
            doc["results"][0].AsBsonDocument.Names.OrderBy(n => n));
    }

    [Fact]
    public void InMemoryUncorrelatedSnapshotIsUnavailable()
    {
        var roll = Receipt(10);
        roll.Results.Reverse();
        Assert.Null(Map(roll).PresentationSnapshot);
        roll = Receipt(10); roll.Results.RemoveAt(0);
        Assert.Null(Map(roll).PresentationSnapshot);
        roll = Receipt(10); roll.Count = 2;
        Assert.Null(Map(roll).PresentationSnapshot);
    }

    [Fact]
    public void LegacyBsonReaderIgnoresNewRootAndRejectsNestedChanges()
    {
        var roll = Receipt(10); var doc = roll.ToBsonDocument();
        var old = BsonSerializer.Deserialize<LegacyB1.GachaRoll>(doc);
        Assert.Equal(roll.RequestId, old.RequestId);
        Assert.Equal(roll.Results.ToJson(), old.Results.ToJson());
        doc["results"][0]["rawItemType"] = "unapproved";
        Assert.Throws<FormatException>(() => BsonSerializer.Deserialize<LegacyB1.GachaRoll>(doc));
    }
}
