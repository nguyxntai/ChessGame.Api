using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MongoDB.Bson;
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
                PoolKey = MatchmakingPolicy.PoolKey(settings), Provisional = user.Ratings.For(settings.Mode).Provisional,
                Rating = ChessGame.Api.Services.Ratings.RatingPolicies.Display(user.Ratings.For(settings.Mode).Rating), CreatedAt = now, ExpiresAt = now.AddSeconds(config.QueueSeconds) };
            await store.Seats.InsertOneAsync(s, new OnlineSeat { UserId = userId, Kind = "Ticket", ReferenceId = ticket.Id }, cancellationToken: token);
            await store.Tickets.InsertOneAsync(s, ticket, cancellationToken: token);
            await Emit(s, "QueueStatusChanged", Ticket(ticket), new() { userId }, token);
            await RegisterPool(s, ticket.PoolKey, token);
            await Pair(s, now, token, ticket.PoolKey);
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
    private Task RegisterPool(IClientSessionHandle s, string key, CancellationToken ct) =>
        store.Pools.UpdateOneAsync(s, x => x.Id == key,
            Builders<QueuePool>.Update.SetOnInsert(x => x.LastServedAt, DateTime.UnixEpoch)
                .SetOnInsert(x => x.CursorAt, DateTime.UnixEpoch).SetOnInsert(x => x.CursorId, ""), new UpdateOptions { IsUpsert = true }, ct);

    private async Task Pair(IClientSessionHandle s, DateTime now, CancellationToken ct, string? focusPool = null)
    {
        // Bounded migration also handles queued tickets written by the previous server version.
        var legacy = await store.Tickets.Find(s, Builders<OnlineTicket>.Filter.Eq(x => x.Status, "Queued") &
            (Builders<OnlineTicket>.Filter.Eq(x => x.PoolKey, "") | Builders<OnlineTicket>.Filter.Eq(x => x.PoolKey, null))).Limit(200).ToListAsync(ct);
        foreach (var ticket in legacy)
        {
            ticket.PoolKey = MatchmakingPolicy.PoolKey(ticket.Settings);
            await store.Tickets.UpdateOneAsync(s, x => x.Id == ticket.Id, Builders<OnlineTicket>.Update.Set(x => x.PoolKey, ticket.PoolKey), cancellationToken: ct);
            await RegisterPool(s, ticket.PoolKey, ct);
        }
        var expired = await store.Tickets.Find(s, x => x.Status == "Queued" && x.ExpiresAt <= now).Limit(200).ToListAsync(ct);
        foreach (var ticket in expired) await ExpireTicket(s, ticket, now, ct);
        var poolFilter = focusPool is null ? Builders<QueuePool>.Filter.Empty : Builders<QueuePool>.Filter.Eq(x => x.Id, focusPool);
        var pools = await store.Pools.Find(s, poolFilter).SortBy(x => x.LastServedAt).ThenBy(x => x.Id).Limit(config.PoolsPerSweep).ToListAsync(ct);
        int formed = 0;
        foreach (var pool in pools)
        {
            var filter = Builders<OnlineTicket>.Filter.Eq(x => x.Status, "Queued") & Builders<OnlineTicket>.Filter.Eq(x => x.PoolKey, pool.Id) & Builders<OnlineTicket>.Filter.Gt(x => x.ExpiresAt, now);
            var oldest = await store.Tickets.Find(s, filter).SortBy(x => x.CreatedAt).ThenBy(x => x.Id).Limit(config.CandidatesPerPool / 2).ToListAsync(ct);
            if (oldest.Count == 0) { await store.Pools.DeleteOneAsync(s, x => x.Id == pool.Id, cancellationToken: ct); continue; }
            var cursor = Builders<OnlineTicket>.Filter.Gt(x => x.CreatedAt, pool.CursorAt) |
                (Builders<OnlineTicket>.Filter.Eq(x => x.CreatedAt, pool.CursorAt) & Builders<OnlineTicket>.Filter.Gt(x => x.Id, pool.CursorId));
            var rotating = await store.Tickets.Find(s, filter & cursor).SortBy(x => x.CreatedAt).ThenBy(x => x.Id).Limit(config.CandidatesPerPool / 2).ToListAsync(ct);
            if (rotating.Count == 0) rotating = oldest;
            var end = rotating[^1];
            await store.Pools.UpdateOneAsync(s, x => x.Id == pool.Id, Builders<QueuePool>.Update.Set(x => x.LastServedAt, now)
                .Set(x => x.CursorAt, end.CreatedAt).Set(x => x.CursorId, end.Id), cancellationToken: ct);
            var candidates = oldest.Concat(rotating).DistinctBy(x => x.Id).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToList();
            var ids = candidates.Select(x => ObjectId.Parse(x.UserId)).ToList();
            var users = (await store.Users.Find(s, Builders<ChessGame.Api.Models.User>.Filter.In(x => x.Id, ids)).ToListAsync(ct)).ToDictionary(x => x.Id.ToString());
            var itemIds = users.Values.SelectMany(u => new[] { u.Equipped.ChessSkinId, u.Equipped.BoardSkinId }).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
            var items = itemIds.Count == 0 ? new Dictionary<ObjectId, ChessGame.Api.Models.Item>() : (await store.Items.Find(s, Builders<ChessGame.Api.Models.Item>.Filter.In(x => x.Id, itemIds)).ToListAsync(ct)).ToDictionary(x => x.Id);
            var owned = itemIds.Count == 0 ? new HashSet<(ObjectId, ObjectId)>() : (await store.PlayerItems.Find(s, Builders<ChessGame.Api.Models.PlayerItem>.Filter.In(x => x.UserId, ids) &
                Builders<ChessGame.Api.Models.PlayerItem>.Filter.In(x => x.ItemId, itemIds)).ToListAsync(ct)).Select(x => (x.UserId, x.ItemId)).ToHashSet();
            var network = await store.Connections.Find(s, Builders<OnlineConnection>.Filter.In(x => x.UserId, candidates.Select(x => x.UserId)) &
                Builders<OnlineConnection>.Filter.Gt(x => x.ExpiresAt, now) & Builders<OnlineConnection>.Filter.Gte(x => x.LatencyMeasuredAt, now.AddSeconds(-60)) &
                Builders<OnlineConnection>.Filter.Gte(x => x.LatencySamples, 3)).ToListAsync(ct);
            var refresh = new List<WriteModel<OnlineTicket>>();
            foreach (var t in candidates)
            {
                var previousRating = t.Rating; var previousPlacement = t.Provisional; var previousLatency = t.RoundTripMilliseconds;
                bool valid = users.TryGetValue(t.UserId, out var u) && u.IsActive;
                if (valid)
                    foreach (var entry in new[] { (Id: u!.Equipped.ChessSkinId, Type: "CHESS"), (Id: u!.Equipped.BoardSkinId, Type: "BOARD") })
                        if (entry.Id is { } itemId && (!items.TryGetValue(itemId, out var item) || !item.IsActive ||
                            !item.Type.Contains(entry.Type, StringComparison.OrdinalIgnoreCase) || !owned.Contains((u.Id, itemId)))) valid = false;
                if (!valid)
                {
                    t.Status = "Cancelled";
                    await store.Tickets.ReplaceOneAsync(s, x => x.Id == t.Id, t, cancellationToken: ct);
                    await store.Seats.DeleteOneAsync(s, x => x.UserId == t.UserId && x.ReferenceId == t.Id, cancellationToken: ct);
                    await Emit(s, "QueueStatusChanged", new { ticket = Ticket(t), reason = "UnavailableParticipant" }, new() { t.UserId }, ct);
                    continue;
                }
                t.Rating = ChessGame.Api.Services.Ratings.RatingPolicies.Display(u!.Ratings.For(t.Settings.Mode).Rating);
                t.Provisional = u.Ratings.For(t.Settings.Mode).Provisional;
                var samples = network.Where(x => x.UserId == t.UserId && x.RoundTripMilliseconds is not null).OrderBy(x => x.RoundTripMilliseconds).ToList();
                t.RoundTripMilliseconds = samples.Count == 0 ? null : samples[samples.Count / 2].RoundTripMilliseconds;
                if (t.Rating != previousRating || t.Provisional != previousPlacement || t.RoundTripMilliseconds != previousLatency)
                    refresh.Add(new UpdateOneModel<OnlineTicket>(Builders<OnlineTicket>.Filter.Eq(x => x.Id, t.Id) & Builders<OnlineTicket>.Filter.Eq(x => x.Status, "Queued"),
                        Builders<OnlineTicket>.Update.Set(x => x.Rating, t.Rating).Set(x => x.Provisional, t.Provisional).Set(x => x.RoundTripMilliseconds, t.RoundTripMilliseconds)));
            }
            if (refresh.Count > 0) await store.Tickets.BulkWriteAsync(s, refresh, cancellationToken: ct);
            candidates.RemoveAll(x => x.Status != "Queued");
            var keys = new List<string>();
            for (int i = 0; i < candidates.Count; i++) for (int j = i + 1; j < candidates.Count; j++)
                if (MatchmakingPolicy.Cost(candidates[i], candidates[j], now, config, 0, null) is not null)
                    keys.Add(MatchmakingPolicy.PairKey(candidates[i].UserId, candidates[j].UserId));
            var recent = await store.Matches.Aggregate(s).Match(new BsonDocument {
                { "SettlementPairKey", new BsonDocument("$in", new BsonArray(keys)) }, { "Result.RatingApplied", true },
                { "FinishedAt", new BsonDocument("$gte", now.AddHours(-24)) } })
                .Group<BsonDocument>(new BsonDocument { { "_id", "$SettlementPairKey" }, { "count", new BsonDocument("$sum", 1) }, { "last", new BsonDocument("$max", "$FinishedAt") } }).ToListAsync(ct);
            var pairs = recent.ToDictionary(x => x["_id"].AsString);
            var planned = PairingPlanner.Match(candidates.Count, (i, j) => {
                var a = candidates[i]; var b = candidates[j];
                pairs.TryGetValue(MatchmakingPolicy.PairKey(a.UserId, b.UserId), out var previous);
                return MatchmakingPolicy.Cost(a, b, now, config, previous?["count"].AsInt32 ?? 0, previous?["last"].ToUniversalTime());
            });
            foreach (var (i, j) in planned)
            {
                var a = candidates[i]; var b = candidates[j];
                var quality = new MatchmakingDiagnostics { RatingGap = Math.Abs(a.Rating - b.Rating),
                    MaximumWaitSeconds = Math.Max((now - a.CreatedAt).TotalSeconds, (now - b.CreatedAt).TotalSeconds),
                    BothProvisional = a.Provisional && b.Provisional, MixedPlacement = a.Provisional != b.Provisional };
                var m = await CreateMatch(s, new() { a.UserId, b.UserId }, a.Settings, true, now, ct, quality);
                quality.WhiteRoundTripMilliseconds = m.Players[0].UserId == a.UserId ? a.RoundTripMilliseconds : b.RoundTripMilliseconds;
                quality.BlackRoundTripMilliseconds = m.Players[1].UserId == a.UserId ? a.RoundTripMilliseconds : b.RoundTripMilliseconds;
                await Save(s, m, ct);
                foreach (var t in new[] { a, b })
                {
                    t.Status = "Matched"; t.MatchId = m.Id;
                    await store.Tickets.ReplaceOneAsync(s, x => x.Id == t.Id, t, cancellationToken: ct);
                    await Emit(s, "QueueStatusChanged", Ticket(t), new() { t.UserId }, ct);
                }
                // Bound Mongo writes as well as graph size within the shared transaction.
                if (++formed >= config.MaximumPairsPerSweep) return;
            }
        }
    }

    public async Task<RoomSnapshot> CreateRoom(string userId, CreateRoomRequest request, CancellationToken ct)
    {
        lease.RequireOwner(); RequestId(request.RequestId); var settings = ValidateSettings(request.Settings);
        var fingerprint = RoomCreationFingerprint(settings);
        return await store.Transaction(async (s, token) =>
        {
            await UserAsync(s, userId, token);
            var previous = await store.Rooms.Find(s, r => r.CreatorId == userId && r.RequestId == request.RequestId).FirstOrDefaultAsync(token);
            if (previous is not null)
            {
                await EnsureRoomCreationFingerprint(s, previous, token);
                if (previous.CreationFingerprint != fingerprint) throw new OnlineException("RequestIdConflict");
                return Room(previous);
            }
            await AvailableAsync(s, userId, token);
            string code;
            do { code = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)); }
            while (await store.Rooms.Find(s, r => r.Code == code).AnyAsync(token));
            var room = new OnlineRoom { OwnerId = userId, CreatorId = userId, RequestId = request.RequestId, Code = code, Members = new() { userId },
                CreationFingerprint = fingerprint, Settings = settings, ExpiresAt = DateTime.UtcNow.AddSeconds(config.RoomSeconds) };
            await store.Rooms.InsertOneAsync(s, room, cancellationToken: token);
            await store.Seats.InsertOneAsync(s, new OnlineSeat { UserId = userId, Kind = "Room", ReferenceId = room.Id }, cancellationToken: token);
            await Emit(s, "RoomUpdated", Room(room), room.Members, token); return Room(room);
        }, ct);
    }
    private static string RoomCreationFingerprint(GameSettings settings) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(OnlineJson.Write(settings))));

    private async Task EnsureRoomCreationFingerprint(IClientSessionHandle s, OnlineRoom room, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(room.CreationFingerprint)) return;
        // Older rooms have no fingerprint. Recover the original settings from the creation
        // event rather than treating the room's mutable settings as the original request.
        var filter = Builders<OnlineEvent>.Filter.Eq(e => e.Type, "RoomUpdated") &
            Builders<OnlineEvent>.Filter.Eq(e => e.MatchId, null) &
            Builders<OnlineEvent>.Filter.AnyEq(e => e.Recipients, room.CreatorId) &
            Builders<OnlineEvent>.Filter.Regex(e => e.PayloadJson,
                new BsonRegularExpression("^\\{\"roomId\":\"" + Regex.Escape(room.Id) + "\""));
        var creation = await store.Events.Find(s, filter).SortBy(e => e.CreatedAt).FirstOrDefaultAsync(ct);
        var original = creation is null ? null : JsonSerializer.Deserialize<RoomSnapshot>(creation.PayloadJson, OnlineJson.Options);
        if (original is null || original.RoomId != room.Id || original.Code != room.Code ||
            original.OwnerId != room.CreatorId || original.Members.Count != 1 || original.Members[0] != room.CreatorId)
            throw new OnlineException("RoomCreationRequestUnavailable");
        room.CreationFingerprint = RoomCreationFingerprint(original.Settings);
        await store.Rooms.UpdateOneAsync(s, r => r.Id == room.Id,
            Builders<OnlineRoom>.Update.Set(r => r.CreationFingerprint, room.CreationFingerprint), cancellationToken: ct);
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
            await EnsureRoomCreationFingerprint(s, r, token);
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
