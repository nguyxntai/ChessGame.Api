using MongoDB.Bson;
using MongoDB.Driver;

namespace ChessGame.Api.Online;

public sealed record MatchmakingQuality(string Mode, long Matches, double AverageRatingGap, int MaximumRatingGap,
    double AverageMaximumWaitSeconds, long MixedPlacementMatches, long MeasuredNetworkMatches, long CancelledMatches);

public sealed partial class OnlineService
{
    public async Task<List<MatchmakingQuality>> Quality(string userId, int days, CancellationToken ct)
    {
        if (days is < 1 or > 30) throw new OnlineException("InvalidWindow", 400);
        await RequireActiveReader(userId, ct);
        var records = await store.Matches.Aggregate().Match(new BsonDocument {
            { "CreatedAt", new BsonDocument("$gte", DateTime.UtcNow.AddDays(-days)) },
            { "Matchmaking", new BsonDocument("$type", "object") } })
            .Group<BsonDocument>(new BsonDocument {
                { "_id", "$Settings.Mode" }, { "count", new BsonDocument("$sum", 1) },
                { "gap", new BsonDocument("$avg", "$Matchmaking.RatingGap") },
                { "maxGap", new BsonDocument("$max", "$Matchmaking.RatingGap") },
                { "wait", new BsonDocument("$avg", "$Matchmaking.MaximumWaitSeconds") },
                { "mixed", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray { "$Matchmaking.MixedPlacement", 1, 0 })) },
                { "network", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray {
                    new BsonDocument("$and", new BsonArray {
                        new BsonDocument("$ne", new BsonArray { "$Matchmaking.WhiteRoundTripMilliseconds", BsonNull.Value }),
                        new BsonDocument("$ne", new BsonArray { "$Matchmaking.BlackRoundTripMilliseconds", BsonNull.Value }) }), 1, 0 })) },
                { "cancelled", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray {
                    new BsonDocument("$eq", new BsonArray { "$Status", "Cancelled" }), 1, 0 })) }
            }).ToListAsync(ct);
        return records.Select(x => new MatchmakingQuality(x["_id"].AsString, x["count"].ToInt64(), x["gap"].ToDouble(),
            x["maxGap"].ToInt32(), x["wait"].ToDouble(), x["mixed"].ToInt64(), x["network"].ToInt64(), x["cancelled"].ToInt64()))
            .OrderBy(x => x.Mode).ToList();
    }
}
