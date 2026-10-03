using System.Security.Cryptography;
using MongoDB.Driver;

namespace ChessGame.Api.Online;

public sealed partial class OnlineService
{
    public async Task<TicketSnapshot> Queue(string userId, QueueRequest request, CancellationToken ct)
    {
        lease.RequireOwner(); RequestId(request.RequestId); var settings = ValidateSettings(request.Settings);
        return await store.Transaction(async (s, token) =>
        {
            var user = await UserAsync(s, userId, token); var now = DateTime.UtcNow;
            var previous = await store.Tickets.Find(s, x => x.UserId == userId && x.RequestId == request.RequestId).FirstOrDefaultAsync(token);
            if (previous is not null)
            {
                if (previous.Settings != settings) throw new OnlineException("RequestIdConflict");
                await ExpireTicket(s, previous, now, token); return Ticket(previous);
            }
            // Lazily release an expired queue seat even before the background sweep sees it.
            var current = await store.Tickets.Find(s, x => x.UserId == userId && x.Status == "Queued").FirstOrDefaultAsync(token);
            if (current is not null) await ExpireTicket(s, current, now, token);
            await AvailableAsync(s, userId, token);
            var ticket = new OnlineTicket { UserId = userId, RequestId = request.RequestId, Settings = settings,
                Rating = user.Stats.Elo, CreatedAt = now, ExpiresAt = now.AddSeconds(config.QueueSeconds) };
            await store.Seats.InsertOneAsync(s, new OnlineSeat { UserId = userId, Kind = "Ticket", ReferenceId = ticket.Id }, cancellationToken: token);
            await store.Tickets.InsertOneAsync(s, ticket, cancellationToken: token);
            await Emit(s, "QueueStatusChanged", Ticket(ticket), new() { userId }, token);
            await Pair(s, now, token);
            ticket = await store.Tickets.Find(s, x => x.Id == ticket.Id).FirstOrDefaultAsync(token);
            return Ticket(ticket!);
        }, ct);
    }
    public async Task<TicketSnapshot?> CurrentTicket(string userId, CancellationToken ct)
    {
        lease.RequireOwner();
        return await store.Transaction(async (s, token) =>
        {
            await UserAsync(s, userId, token);
            var ticket = await store.Tickets.Find(s, x => x.UserId == userId).SortByDescending(x => x.CreatedAt).FirstOrDefaultAsync(token);
            if (ticket is null) return null;
            await ExpireTicket(s, ticket, DateTime.UtcNow, token);
            return Ticket(ticket);
        }, ct);
    }
    public async Task<TicketSnapshot> CancelTicket(string userId, string ticketId, CancellationToken ct)
    {
        lease.RequireOwner(); OnlineIdentity.Id(ticketId);
        return await store.Transaction(async (s, token) =>
        {
            await UserAsync(s, userId, token);
            var t = await store.Tickets.Find(s, x => x.Id == ticketId).FirstOrDefaultAsync(token) ?? throw new OnlineException("TicketNotFound", 404);
            if (t.UserId != userId) throw new OnlineException("Forbidden", 403);
            await ExpireTicket(s, t, DateTime.UtcNow, token);
            // If pairing won the transaction race, return Matched + matchId. Cancelling the ticket cannot orphan its match.
            if (t.Status == "Queued")
            {
                t.Status = "Cancelled";
                await store.Tickets.ReplaceOneAsync(s, x => x.Id == t.Id, t, cancellationToken: token);
                await store.Seats.DeleteOneAsync(s, x => x.UserId == userId && x.ReferenceId == t.Id && x.Kind == "Ticket", cancellationToken: token);
                await Emit(s, "QueueStatusChanged", Ticket(t), new() { userId }, token);
            }
            return Ticket(t);
        }, ct);
    }
    private async Task ExpireTicket(IClientSessionHandle s, OnlineTicket t, DateTime now, CancellationToken ct)
    {
        if (t.Status != "Queued" || t.ExpiresAt > now) return;
        t.Status = "Expired";
        await store.Tickets.ReplaceOneAsync(s, x => x.Id == t.Id, t, cancellationToken: ct);
        await store.Seats.DeleteOneAsync(s, x => x.UserId == t.UserId && x.ReferenceId == t.Id && x.Kind == "Ticket", cancellationToken: ct);
        await Emit(s, "QueueStatusChanged", Ticket(t), new() { t.UserId }, ct);
    }
    private int RatingRange(OnlineTicket t, DateTime now) => config.InitialRatingRange +
        (int)Math.Min(3000, Math.Max(0, (now - t.CreatedAt).TotalSeconds) * config.RatingRangePerSecond);
    private async Task Pair(IClientSessionHandle s, DateTime now, CancellationToken ct)
    {
        var tickets = await store.Tickets.Find(s, x => x.Status == "Queued").SortBy(x => x.CreatedAt).Limit(200).ToListAsync(ct);
        foreach (var t in tickets)
        {
            await ExpireTicket(s, t, now, ct);
            if (t.Status != "Queued") continue;
            try { await Loadout(s, await UserAsync(s, t.UserId, ct), ct); }
            catch (OnlineException ex) when (ex.Code is "InvalidLoadout" or "Forbidden")
            {
                t.Status = "Cancelled";
                await store.Tickets.ReplaceOneAsync(s, x => x.Id == t.Id, t, cancellationToken: ct);
                await store.Seats.DeleteOneAsync(s, x => x.UserId == t.UserId && x.ReferenceId == t.Id, cancellationToken: ct);
                await Emit(s, "QueueStatusChanged", new { ticket = Ticket(t), reason = ex.Code }, new() { t.UserId }, ct);
            }
        }
        var remaining = tickets.Where(t => t.Status == "Queued").ToList();
        foreach (var a in remaining.ToList())
        {
            if (a.Status != "Queued") continue;
            var b = remaining.Where(b => b.Id != a.Id && b.UserId != a.UserId && b.Status == "Queued" && b.Settings == a.Settings &&
                Math.Abs((long)b.Rating - a.Rating) <= Math.Min(RatingRange(a, now), RatingRange(b, now)))
                .OrderBy(b => Math.Abs((long)b.Rating - a.Rating)).ThenBy(b => b.CreatedAt).FirstOrDefault();
            if (b is null) continue;
            var m = await CreateMatch(s, new() { a.UserId, b.UserId }, a.Settings, true, now, ct);
            foreach (var t in new[] { a, b })
            {
                t.Status = "Matched"; t.MatchId = m.Id;
                await store.Tickets.ReplaceOneAsync(s, x => x.Id == t.Id, t, cancellationToken: ct);
                await Emit(s, "QueueStatusChanged", Ticket(t), new() { t.UserId }, ct);
            }
        }
    }

    public async Task<RoomSnapshot> CreateRoom(string userId, CreateRoomRequest request, CancellationToken ct)
    {
        lease.RequireOwner(); RequestId(request.RequestId); var settings = ValidateSettings(request.Settings);
        return await store.Transaction(async (s, token) =>
        {
            await UserAsync(s, userId, token);
            var previous = await store.Rooms.Find(s, r => r.CreatorId == userId && r.RequestId == request.RequestId).FirstOrDefaultAsync(token);
            if (previous is not null)
            {
                if (previous.Settings != settings) throw new OnlineException("RequestIdConflict");
                return Room(previous);
            }
            await AvailableAsync(s, userId, token);
            string code;
            do { code = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)); }
            while (await store.Rooms.Find(s, r => r.Code == code).AnyAsync(token));
            var room = new OnlineRoom { OwnerId = userId, CreatorId = userId, RequestId = request.RequestId, Code = code, Members = new() { userId },
                Settings = settings, ExpiresAt = DateTime.UtcNow.AddSeconds(config.RoomSeconds) };
            await store.Rooms.InsertOneAsync(s, room, cancellationToken: token);
            await store.Seats.InsertOneAsync(s, new OnlineSeat { UserId = userId, Kind = "Room", ReferenceId = room.Id }, cancellationToken: token);
            await Emit(s, "RoomUpdated", Room(room), room.Members, token); return Room(room);
        }, ct);
    }
    public async Task<RoomSnapshot> JoinRoom(string userId, string code, CancellationToken ct)
    {
        lease.RequireOwner();
        if (string.IsNullOrWhiteSpace(code) || code.Length > 12) throw new OnlineException("InvalidRoomCode", 400);
        code = code.Trim().ToUpperInvariant();
        return await store.Transaction(async (s, token) =>
        {
            await UserAsync(s, userId, token);
            var room = await store.Rooms.Find(s, r => r.Code == code).FirstOrDefaultAsync(token) ?? throw new OnlineException("RoomNotFound", 404);
            if (room.Status != "Open" || room.ExpiresAt <= DateTime.UtcNow) throw new OnlineException("RoomClosed");
            if (room.Members.Contains(userId)) return Room(room);
            await AvailableAsync(s, userId, token);
            if (room.Members.Count >= 2) throw new OnlineException("RoomFull");
            room.Members.Add(userId);
            await store.Seats.InsertOneAsync(s, new OnlineSeat { UserId = userId, Kind = "Room", ReferenceId = room.Id }, cancellationToken: token);
            await store.Rooms.ReplaceOneAsync(s, r => r.Id == room.Id, room, cancellationToken: token);
            await Emit(s, "RoomUpdated", Room(room), room.Members, token); return Room(room);
        }, ct);
    }
    private async Task<OnlineRoom> RoomAsync(IClientSessionHandle s, string roomId, string userId, CancellationToken ct)
    {
        var r = await store.Rooms.Find(s, r => r.Id == roomId).FirstOrDefaultAsync(ct) ?? throw new OnlineException("RoomNotFound", 404);
        if (!r.Members.Contains(userId)) throw new OnlineException("Forbidden", 403);
        return r;
    }
    public async Task<RoomSnapshot> GetRoom(string userId, string roomId, CancellationToken ct)
    {
        lease.RequireOwner(); OnlineIdentity.Id(roomId);
        return await store.Transaction(async (s, token) =>
        {
            await UserAsync(s, userId, token); var r = await RoomAsync(s, roomId, userId, token);
            if (r.Status == "Open" && r.ExpiresAt <= DateTime.UtcNow) await CloseRoom(s, r, "RoomExpired", token);
            return Room(r);
        }, ct);
    }
    public async Task<RoomSnapshot> RoomSettings(string userId, string roomId, GameSettings settings, CancellationToken ct)
    {
        lease.RequireOwner(); OnlineIdentity.Id(roomId); settings = ValidateSettings(settings);
        return await store.Transaction(async (s, token) =>
        {
            await UserAsync(s, userId, token); var r = await RoomAsync(s, roomId, userId, token);
            if (r.OwnerId != userId) throw new OnlineException("Forbidden", 403);
            if (r.Status != "Open" || r.ExpiresAt <= DateTime.UtcNow) throw new OnlineException("RoomClosed");
            r.Settings = settings;
            await store.Rooms.ReplaceOneAsync(s, x => x.Id == r.Id, r, cancellationToken: token);
            await Emit(s, "RoomUpdated", Room(r), r.Members, token); return Room(r);
        }, ct);
    }
    public async Task<RoomSnapshot> LeaveRoom(string userId, string roomId, CancellationToken ct)
    {
        lease.RequireOwner(); OnlineIdentity.Id(roomId);
        return await store.Transaction(async (s, token) =>
        {
            await UserAsync(s, userId, token);
            var r = await store.Rooms.Find(s, x => x.Id == roomId).FirstOrDefaultAsync(token) ?? throw new OnlineException("RoomNotFound", 404);
            if (!r.Members.Contains(userId)) throw new OnlineException("Forbidden", 403);
            if (r.Status == "Closed") return Room(r);
            if (r.Status != "Open") throw new OnlineException("RoomAlreadyStarted");
            var recipients = r.Members.ToList(); r.Members.Remove(userId);
            await store.Seats.DeleteOneAsync(s, x => x.UserId == userId && x.ReferenceId == roomId && x.Kind == "Room", cancellationToken: token);
            if (r.Members.Count == 0) r.Status = "Closed";
            else if (r.OwnerId == userId) r.OwnerId = r.Members[0];
            await store.Rooms.ReplaceOneAsync(s, x => x.Id == r.Id, r, cancellationToken: token);
            await Emit(s, r.Status == "Closed" ? "RoomClosed" : "RoomUpdated", new { room = Room(r), leftUserId = userId }, recipients, token);
            return Room(r);
        }, ct);
    }
    public async Task<MatchSnapshot> StartRoom(string userId, string roomId, CancellationToken ct)
    {
        lease.RequireOwner(); OnlineIdentity.Id(roomId);
        return await store.Transaction(async (s, token) =>
        {
            await UserAsync(s, userId, token); var r = await RoomAsync(s, roomId, userId, token);
            if (r.OwnerId != userId) throw new OnlineException("Forbidden", 403);
            if (r.MatchId is not null) return Snapshot(await MatchAsync(s, r.MatchId, userId, token), DateTime.UtcNow, userId);
            if (r.Status != "Open" || r.ExpiresAt <= DateTime.UtcNow) throw new OnlineException("RoomClosed");
            if (r.Members.Count != 2) throw new OnlineException("RoomNotFull");
            foreach (var id in r.Members)
            {
                var seat = await store.Seats.Find(s, x => x.UserId == id && x.Kind == "Room" && x.ReferenceId == r.Id).FirstOrDefaultAsync(token);
                if (seat is null) throw new OnlineException("RoomMembershipConflict");
            }
            var match = await CreateMatch(s, r.Members, r.Settings, false, DateTime.UtcNow, token);
            r.MatchId = match.Id; r.Status = "Started";
            await store.Rooms.ReplaceOneAsync(s, x => x.Id == r.Id, r, cancellationToken: token);
            await Emit(s, "RoomUpdated", Room(r), r.Members, token); return Snapshot(match, DateTime.UtcNow, userId);
        }, ct);
    }
    public async Task<RoomSnapshot> DeleteRoom(string userId, string roomId, CancellationToken ct)
    {
        lease.RequireOwner(); OnlineIdentity.Id(roomId);
        return await store.Transaction(async (s, token) =>
        {
            await UserAsync(s, userId, token); var r = await RoomAsync(s, roomId, userId, token);
            if (r.OwnerId != userId) throw new OnlineException("Forbidden", 403);
            if (r.Status == "Started") throw new OnlineException("RoomAlreadyStarted");
            if (r.Status != "Closed") await CloseRoom(s, r, "OwnerClosed", token);
            return Room(r);
        }, ct);
    }
    private async Task CloseRoom(IClientSessionHandle s, OnlineRoom r, string reason, CancellationToken ct)
    {
        r.Status = "Closed";
        await store.Seats.DeleteManyAsync(s, x => x.ReferenceId == r.Id && x.Kind == "Room", cancellationToken: ct);
        await store.Rooms.ReplaceOneAsync(s, x => x.Id == r.Id, r, cancellationToken: ct);
        await Emit(s, "RoomClosed", new { room = Room(r), reason }, r.Members, ct);
    }
}
