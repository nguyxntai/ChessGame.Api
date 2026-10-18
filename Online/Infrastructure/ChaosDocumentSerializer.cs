using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace ChessGame.Api.Online;

/// <summary>Normalize the document before the class-map reader; serialization writes only the new schema.</summary>
public sealed class ChaosDocumentSerializer<T> : SerializerBase<T>, IBsonDocumentSerializer where T : class
{
    private BsonClassMapSerializer<T> Inner => new(BsonClassMap.LookupClassMap(typeof(T)));
    public override T Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var doc = BsonDocumentSerializer.Instance.Deserialize(context);
        string oldKey = typeof(T) == typeof(OnlineMatch) ? ModeNormalization.LegacyMode : ModeNormalization.LegacyKey;
        const string newKey = "chaos";
        if (doc.TryGetValue(oldKey, out var old))
        {
            if (doc.TryGetValue(newKey, out var current) && current != old)
                throw new FormatException("Conflicting legacy/Chaos BSON fields; resolve offline before rollout.");
            if (!doc.Contains(newKey)) doc[newKey] = old;
            doc.Remove(oldKey);
        }
        using var reader = new BsonDocumentReader(doc);
        return Inner.Deserialize(BsonDeserializationContext.CreateRoot(reader), args);
    }
    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, T value) => Inner.Serialize(context, args, value);
    public bool TryGetMemberSerializationInfo(string memberName, out BsonSerializationInfo serializationInfo) =>
        Inner.TryGetMemberSerializationInfo(memberName, out serializationInfo);
}
