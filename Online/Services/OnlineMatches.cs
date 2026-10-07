using MongoDB.Bson;
using MongoDB.Driver;

namespace ChessGame.Api.Online;

public sealed partial class OnlineService
{
    // Metadata/history must not download board, ARAM state or repetition history.
    private static readonly ProjectionDefinition<OnlineMatch, OnlineMatch> SummaryProjection =
        Builders<OnlineMatch>.Projection.Expression(m => new OnlineMatch
        {
            Id = m.Id, Status = m.Status, Settings = m.Settings, Players = m.Players, Rated = m.Rated,
            CreatedAt = m.CreatedAt, StartedAt = m.StartedAt, FinishedAt = m.FinishedAt, Result = m.Result
        });

    private async Task RequireActiveReader(string userId, CancellationToken ct)
    {
        var id = ObjectId.Parse(OnlineIdentity.Id(userId));
        if (!await store.Users.Find(u => u.Id == id && u.IsActive).Project(u => u.Id).AnyAsync(ct))
            throw new OnlineException("Forbidden", 403);
    }

    public async Task<OfficialResult> CompleteMatch(string userId, string matchId, CancellationToken ct)
    {
        lease.RequireOwner(); OnlineIdentity.Id(matchId);
        var completed = await store.Transaction(async (session, token) =>
        {
            await UserAsync(session, userId, token);
            var match = await MatchAsync(session, matchId, userId, token);
            var now = DateTime.UtcNow;
            await Advance(session, match, now, token);
            if (match.Status == "InProgress" && rules.Outcome(match) is { } outcome)
                await Finish(session, match, outcome.WinnerTeam, outcome.Reason, now, token);
            return (match.Status, match.Result);
        }, ct);

        // Check after commit so valid deadline/setup transitions from Advance are retained.
        if (completed.Status == "Cancelled") throw new OnlineException("MatchCancelled");
        return completed.Status == "Finished" && completed.Result is not null
            ? completed.Result : throw new OnlineException("ResultNotReady");
    }
}
