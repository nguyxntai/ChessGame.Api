using ChessGame.Api.DTOs.Leaderboard;
using ChessGame.Api.Models;
using ChessGame.Api.Services.Ratings;
using ChessGame.Api.Online;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ChessGame.Api.Services;

public sealed class LeaderboardService(IMongoDatabase database, IMongoClient client)
{
    private readonly IMongoCollection<User> users = database.GetCollection<User>("users");
    private static readonly ProjectionDefinition<User, User> PublicProjection =
        Builders<User>.Projection.Expression(u => new User
        {
            Id = u.Id, Username = u.Username, Profile = u.Profile,
            Ratings = u.Ratings
        });

    public async Task<List<LeaderboardEntry>> Top(string userId, int limit, CancellationToken ct, string mode = "Classic", bool includeProvisional = false)
    {
        if (limit is < 1 or > 100) throw new OnlineException("InvalidLimit", 400);
        mode = mode.Trim().ToUpperInvariant() switch { "CLASSIC" => "Classic", "ARAM" => "Aram", _ => throw new OnlineException("InvalidMode", 400) };
        var field = mode == "Classic" ? "ratings.classic.rating" : "ratings.aram.rating";
        var id = ObjectId.Parse(OnlineIdentity.Id(userId));
        if (!await users.Find(u => u.Id == id && u.IsActive).Project(u => u.Id).AnyAsync(ct))
            throw new OnlineException("Forbidden", 403);

        var players = await users.Find(Eligible(mode, includeProvisional)).Sort(Builders<User>.Sort.Descending(field).Ascending(u => u.Id))
            .Limit(limit).Project(PublicProjection).ToListAsync(ct);
        var result = new List<LeaderboardEntry>(players.Count);
        long rank = 1;
        for (var i = 0; i < players.Count; i++)
        {
            if (i > 0 && players[i].Ratings.For(mode).Rating != players[i - 1].Ratings.For(mode).Rating) rank = i + 1;
            result.Add(Entry(players[i], rank, mode));
        }
        return result;
    }

    public async Task<LeaderboardEntry> Me(string userId, CancellationToken ct, string mode = "Classic", bool includeProvisional = false)
    {
        mode = mode.Trim().ToUpperInvariant() switch { "CLASSIC" => "Classic", "ARAM" => "Aram", _ => throw new OnlineException("InvalidMode", 400) };
        var field = mode == "Classic" ? "ratings.classic.rating" : "ratings.aram.rating";
        var id = ObjectId.Parse(OnlineIdentity.Id(userId));
        // Pin both reads to one snapshot, even if a match changes ELO between them.
        // A snapshot session avoids the write guard and commit round trip of gameplay transactions.
        using var session = await client.StartSessionAsync(new ClientSessionOptions { Snapshot = true }, ct);
        var player = await users.Find(session, u => u.Id == id && u.IsActive).Project(PublicProjection).FirstOrDefaultAsync(ct)
            ?? throw new OnlineException("Forbidden", 403);
        // An indexed count finds the rank outside the top 100 without fetching all players.
        if (!includeProvisional && player.Ratings.For(mode).Provisional) return Entry(player, null, mode);
        var higher = await users.CountDocumentsAsync(session, Eligible(mode, includeProvisional) & Builders<User>.Filter.Gt(field, player.Ratings.For(mode).Rating),
            cancellationToken: ct);
        return Entry(player, higher + 1, mode);
    }

    private static FilterDefinition<User> Eligible(string mode, bool includeProvisional) =>
        Builders<User>.Filter.Eq(u => u.IsActive, true) & (includeProvisional ? Builders<User>.Filter.Empty :
            Builders<User>.Filter.Gte(mode == "Classic" ? "ratings.classic.ratedGames" : "ratings.aram.ratedGames", 10));

    private static LeaderboardEntry Entry(User user, long? rank, string mode)
    {
        var rating = user.Ratings.For(mode);
        return new(rank, user.Id.ToString(), user.Username, user.Profile.DisplayName, user.Profile.AvatarId,
            RatingPolicies.Display(rating.Rating), mode, rating.RatedGames, mode == "Aram" ? rating.Deviation : null, rating.Provisional);
    }
}
