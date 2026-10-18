using ChessGame.Api.Online;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ChessGame.Api.Services.Ratings;

/// <summary>Read-only deployment gate: queries and pool indexes require the offline migration first.</summary>
public static class ChaosMigrationGuard
{
    public static async Task AssertReadyAsync(IMongoDatabase db, CancellationToken ct = default)
    {
        var marker = await db.GetCollection<BsonDocument>("schema_migrations")
            .Find(new BsonDocument("_id", "chaos-v1")).FirstOrDefaultAsync(ct);
        if (marker is not null && marker.GetValue("status", "").AsString != "Completed")
            throw new InvalidOperationException("Chaos migration is incomplete. Keep all API instances stopped and finish the offline migration.");
        BsonDocument Exists(string path) => new(path, new BsonDocument("$exists", true));
        var mode = new BsonRegularExpression("^\\s*" + ModeNormalization.LegacyMode + "\\s*$", "i");
        var filters = new Dictionary<string, BsonDocument>
        {
            ["users"] = new("$or", new BsonArray { Exists("ratings." + ModeNormalization.LegacyKey), Exists("onlineColors." + ModeNormalization.LegacyKey) }),
            ["online_matches"] = new("$or", new BsonArray { Exists(ModeNormalization.LegacyMode), new BsonDocument("Settings.Mode", mode), new BsonDocument("Result.RatingMode", mode) }),
            ["online_rooms"] = new("Settings.Mode", mode),
            ["online_tickets"] = new("Settings.Mode", mode),
            ["online_events"] = new("PayloadJson", new BsonRegularExpression("\"(?:" + ModeNormalization.LegacyKey + ")\"\\s*:|\"(?:mode|ratingMode)\"\\s*:\\s*\"" + ModeNormalization.LegacyMode + "\"", "i"))
        };
        foreach (var (collection, filter) in filters)
            if (await db.GetCollection<BsonDocument>(collection).Find(filter).Limit(1).AnyAsync(ct))
                throw new InvalidOperationException($"Legacy mode data remains in {collection}. Run Migrations/chaos-v1.mongosh.js offline before starting this build.");
    }
}
