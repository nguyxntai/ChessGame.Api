using System.Collections.Concurrent;
using MongoDB.Driver;

namespace ChessGame.Api.Online;

public sealed partial class OnlineService
{
    public async Task Connected(string userId, string connectionId, CancellationToken ct)
    {
        lease.RequireOwner();
        await store.Transaction(async (s, token) =>
        {
            await UserAsync(s, userId, token); var now = DateTime.UtcNow;
            await store.Connections.ReplaceOneAsync(s, x => x.Id == connectionId,
                new OnlineConnection { Id = connectionId, UserId = userId, ExpiresAt = now.AddSeconds(30) }, new ReplaceOptions { IsUpsert = true }, token);
            var seat = await store.Seats.Find(s, x => x.UserId == userId && x.Kind == "Match").FirstOrDefaultAsync(token);
            if (seat is not null)
            {
                var m = await MatchAsync(s, seat.ReferenceId, userId, token); await Advance(s, m, now, token);
                if (Active(m))
                {
                    var p = m.Players.Single(p => p.UserId == userId);
                    if (!p.Connected)
                    {
                        p.Connected = true; p.ReconnectDeadline = null; m.StateVersion++;
                        await MatchEvent(s, m, "PlayerConnectionChanged", now, token);
                    }
                    await StartIfReady(s, m, now, token); await Save(s, m, token);
                }
            }
            return true;
        }, ct);
    }
    public async Task Disconnected(string userId, string connectionId, CancellationToken ct)
    {
        lease.RequireOwner();
        await store.Transaction(async (s, token) =>
        {
            await store.Connections.DeleteOneAsync(s, x => x.Id == connectionId && x.UserId == userId, cancellationToken: token);
            await UpdatePresence(s, userId, DateTime.UtcNow, token); return true;
        }, ct);
    }
    private async Task UpdatePresence(IClientSessionHandle s, string userId, DateTime now, CancellationToken ct)
    {
        var seat = await store.Seats.Find(s, x => x.UserId == userId && x.Kind == "Match").FirstOrDefaultAsync(ct);
        if (seat is null) return;
        var m = await MatchAsync(s, seat.ReferenceId, userId, ct); await Advance(s, m, now, ct);
        if (!Active(m)) return;
        bool connected = await store.Connections.Find(s, x => x.UserId == userId && x.ExpiresAt > now).AnyAsync(ct);
        var player = m.Players.Single(p => p.UserId == userId);
        if (player.Connected == connected) return;
        player.Connected = connected;
        player.ReconnectDeadline = connected || m.Status != "InProgress" ? null : now.AddSeconds(config.ReconnectSeconds);
        m.StateVersion++; await MatchEvent(s, m, "PlayerConnectionChanged", now, ct); await Save(s, m, ct);
    }
    public async Task Recover(CancellationToken ct)
    {
        await store.Transaction(async (s, token) =>
        { await store.Connections.DeleteManyAsync(s, Builders<OnlineConnection>.Filter.Empty, cancellationToken: token); return true; }, ct);
        var active = await store.Matches.Find(m => m.Status == "AwaitingReady" || m.Status == "InProgress").ToListAsync(ct);
        foreach (var entry in active)
            await store.Transaction(async (s, token) =>
            {
                var m = await store.Matches.Find(s, x => x.Id == entry.Id).FirstOrDefaultAsync(token);
                if (m is null) return true;
                var now = DateTime.UtcNow; await Advance(s, m, now, token);
                if (!Active(m)) return true;
                foreach (var p in m.Players)
                {
                    p.Connected = false;
                    if (m.Status == "InProgress") p.ReconnectDeadline ??= now.AddSeconds(config.ReconnectSeconds);
                }
                m.StateVersion++; await MatchEvent(s, m, "PlayerConnectionChanged", now, token); await Save(s, m, token);
                return true;
            }, ct);
    }
    public async Task Sweep(LiveConnections live, CancellationToken ct)
    {
        lease.RequireOwner();
        await store.Transaction(async (s, token) =>
        {
            var now = DateTime.UtcNow;
            // Renew only connections still attached to this process, not stale persisted rows.
            var ids = live.Users.Keys.ToList();
            if (ids.Count > 0)
                await store.Connections.UpdateManyAsync(s, Builders<OnlineConnection>.Filter.In(c => c.Id, ids),
                    Builders<OnlineConnection>.Update.Set(c => c.ExpiresAt, now.AddSeconds(30)), cancellationToken: token);
            var expired = await store.Connections.Find(s, c => c.ExpiresAt <= now).ToListAsync(token);
            await store.Connections.DeleteManyAsync(s, c => c.ExpiresAt <= now, cancellationToken: token);
            foreach (var userId in expired.Select(c => c.UserId).Distinct()) await UpdatePresence(s, userId, now, token);
            var rooms = await store.Rooms.Find(s, r => r.Status == "Open" && r.ExpiresAt <= now).Limit(100).ToListAsync(token);
            foreach (var r in rooms) await CloseRoom(s, r, "RoomExpired", token);
            await Pair(s, now, token); return true;
        }, ct);
        var matches = await store.Matches.Find(m => m.Status == "AwaitingReady" || m.Status == "InProgress").ToListAsync(ct);
        foreach (var entry in matches)
            await store.Transaction(async (s, token) =>
            {
                var m = await store.Matches.Find(s, x => x.Id == entry.Id).FirstOrDefaultAsync(token);
                if (m is not null) await Advance(s, m, DateTime.UtcNow, token);
                return true;
            }, ct);
    }
}
