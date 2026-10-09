namespace ChessGame.Api.Online;

public sealed partial class OnlineService
{
    public async Task<MatchSnapshot> AcceptMatch(string userId, string matchId, CancellationToken ct)
    {
        lease.RequireOwner(); OnlineIdentity.Id(matchId);
        return await store.Transaction(async (s, token) =>
        {
            await UserAsync(s, userId, token);
            var m = await MatchAsync(s, matchId, userId, token);
            var now = DateTime.UtcNow;
            await Advance(s, m, now, token);
            // Returning the cancelled snapshot commits expiration and seat release.
            if (m.Status == "Cancelled" || m.Status == "InProgress") return Snapshot(m, now, userId);
            if (m.Status != "AwaitingReady") throw new OnlineException("MatchNotActive");
            if (m.AcceptDeadline is null) return Snapshot(m, now, userId);
            var player = m.Players.Single(p => p.UserId == userId);
            if (!player.Accepted)
            {
                player.Accepted = true; m.StateVersion++;
                if (m.Players.All(p => p.Accepted))
                {
                    m.AcceptDeadline = null;
                    m.ReadyDeadline = now.AddSeconds(config.ReadySeconds);
                    if (m.Aram is not null) m.Aram.SetupDeadline = m.ReadyDeadline;
                }
                await MatchEvent(s, m, "MatchAccepted", now, token);
                await Save(s, m, token);
            }
            return Snapshot(m, now, userId);
        }, ct);
    }
}