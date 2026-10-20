using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace ChessGame.Api.Models;

// Immutable values, captured before receipt insertion. There is no catalog revision authority.
public sealed class GachaPresentationSnapshot
{
    public const int CurrentSchemaVersion = 1;
    public const string TransactionItemRead = "TRANSACTION_ITEM_READ";
    public int SchemaVersion => CurrentSchemaVersion;
    public string MetadataSource => TransactionItemRead;
    public string? CatalogVersion => null;
    public IReadOnlyList<GachaRewardMetadataSnapshot> Rewards { get; }

    public GachaPresentationSnapshot(IEnumerable<GachaRewardMetadataSnapshot> rewards) =>
        Rewards = Array.AsReadOnly(rewards.ToArray());
}

public sealed record GachaRewardMetadataSnapshot(
    int SlotIndex, ObjectId ItemId, string? ItemCode, string? RawItemType, string? UnityAssetKey);

// Presentation must not make a valid economic receipt unreadable. Unknown schema/invalid
// optional payloads become unavailable metadata; never repair them from the live catalog.
// The BSON reader still throws for corrupt economic fields or an invalid BSON stream.
public sealed class GachaPresentationSnapshotSerializer : SerializerBase<GachaPresentationSnapshot?>
{
    public override GachaPresentationSnapshot? Deserialize(
        BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var value = BsonValueSerializer.Instance.Deserialize(context, args);
        if (value is not BsonDocument doc
            || !doc.TryGetValue("schemaVersion", out var version)
            || !version.IsInt32 || version.AsInt32 != GachaPresentationSnapshot.CurrentSchemaVersion
            || !doc.TryGetValue("metadataSource", out var source)
            || !source.IsString || source.AsString != GachaPresentationSnapshot.TransactionItemRead
            || (doc.TryGetValue("catalogVersion", out var catalog) && !catalog.IsBsonNull)
            || !doc.TryGetValue("rewards", out var rewards) || !rewards.IsBsonArray
            || rewards.AsBsonArray.Count is not (1 or 10))
            return null;

        var rows = new List<GachaRewardMetadataSnapshot>(rewards.AsBsonArray.Count);
        foreach (var entry in rewards.AsBsonArray)
        {
            if (entry is not BsonDocument row
                || !row.TryGetValue("slotIndex", out var slot) || !slot.IsInt32
                || slot.AsInt32 != rows.Count
                || !row.TryGetValue("itemId", out var id) || !id.IsObjectId
                || id.AsObjectId == ObjectId.Empty
                || !ReadString(row, "itemCode", out var code)
                || !ReadString(row, "rawItemType", out var type)
                || !ReadString(row, "unityAssetKey", out var key))
                return null;
            rows.Add(new(slot.AsInt32, id.AsObjectId, code, type, key));
        }
        return new GachaPresentationSnapshot(rows);
    }

    public override void Serialize(BsonSerializationContext context,
        BsonSerializationArgs args, GachaPresentationSnapshot? value)
    {
        if (value is null)
        {
            context.Writer.WriteNull();
            return;
        }
        var doc = new BsonDocument
        {
            { "schemaVersion", value.SchemaVersion },
            { "metadataSource", value.MetadataSource },
            { "rewards", new BsonArray(value.Rewards.Select(row => new BsonDocument
                {
                    { "slotIndex", row.SlotIndex },
                    { "itemId", row.ItemId },
                    { "itemCode", Text(row.ItemCode) },
                    { "rawItemType", Text(row.RawItemType) },
                    { "unityAssetKey", Text(row.UnityAssetKey) }
                })) }
        };
        BsonDocumentSerializer.Instance.Serialize(context, doc);
    }

    private static BsonValue Text(string? value) =>
        value is null ? BsonNull.Value : new BsonString(value);

    private static bool ReadString(BsonDocument row, string name, out string? value)
    {
        value = null;
        if (!row.TryGetValue(name, out var field) || field.IsBsonNull) return true;
        if (!field.IsString) return false;
        value = field.AsString;
        return true;
    }
}
