using ChessGame.Api.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ChessGame.Api.Services.Ratings;

public static class RatingMigration
{
    public static Task InitializeAsync(IMongoDatabase database, CancellationToken ct = default)
    {
        // One atomic, idempotent update per legacy account. Never infer Classic skill from mixed Elo.
        var template = new PlayerRatings().ToBsonDocument();
        template["legacyMixedElo"] = "$stats.elo";
        PipelineDefinition<BsonDocument, BsonDocument> pipeline = new[]
        { new BsonDocument("$set", new BsonDocument("ratings", template)) };
        return database.GetCollection<BsonDocument>("users").UpdateManyAsync(
            new BsonDocument("ratings", new BsonDocument("$exists", false)),
            new PipelineUpdateDefinition<BsonDocument>(pipeline), cancellationToken: ct);
    }
}
