using ChessGame.Api.DTOs.Gacha;
using ChessGame.Api.Models;

namespace ChessGame.Api.Services;

public static class GachaPresentationMapping
{
    // Fail closed on uncorrelated metadata while retaining the economic receipt.
    // No I/O, category inference, catalog fallback or post-commit dependency.
    public static GachaPresentationSnapshotResponse? ToResponse(GachaRoll roll)
    {
        var snapshot = roll.PresentationSnapshot;
        if (snapshot is null || roll.Count is not (1 or 10)
            || snapshot.Rewards.Count != roll.Count || roll.Results.Count != roll.Count)
            return null;

        var rows = new List<GachaRewardMetadataResponse>(roll.Count);
        for (var i = 0; i < roll.Count; i++)
        {
            var row = snapshot.Rewards[i];
            var reward = roll.Results[i];
            if (row is null || row.SlotIndex != i || row.ItemId == MongoDB.Bson.ObjectId.Empty
                || row.ItemId != reward.ItemId || row.ItemCode != reward.ItemCode)
                return null;
            rows.Add(new GachaRewardMetadataResponse
            {
                SlotIndex = row.SlotIndex,
                ItemId = row.ItemId.ToString(),
                ItemCode = row.ItemCode,
                RawItemType = row.RawItemType,
                UnityAssetKey = row.UnityAssetKey
            });
        }
        return new GachaPresentationSnapshotResponse
        {
            SchemaVersion = snapshot.SchemaVersion,
            MetadataSource = snapshot.MetadataSource,
            CatalogVersion = snapshot.CatalogVersion,
            Rewards = rows
        };
    }
}
