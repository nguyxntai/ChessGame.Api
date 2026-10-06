using System.Security.Cryptography;
using System.Text;
using ChessButWeird.Domain;
using ChessGame.Api.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ChessGame.Api.Online;

public sealed partial class OnlineService(OnlineStore store, GameRules rules, AramEngine aram,
    IOptions<OnlineOptions> options, OnlineRuntimeLease lease)
{
    private readonly OnlineOptions config = options.Value;
    private static bool Active(OnlineMatch m) => m.Status is "AwaitingReady" or "InProgress";
    private static TicketSnapshot Ticket(OnlineTicket t) => new(t.Id, t.Status, t.Settings, t.CreatedAt, t.ExpiresAt, t.MatchId);
    private static RoomSnapshot Room(OnlineRoom r) => new(r.Id, r.Code, r.OwnerId, r.Members, r.Settings, r.Status, r.MatchId, r.ExpiresAt);
    private static PlayerSnapshot Player(MatchPlayer p) => new(p.UserId, p.Username, p.DisplayName, p.Color, p.Rating,
        p.Ready, p.Connected, p.ReconnectDeadline, p.Loadout);
    private static MatchSummary Summary(OnlineMatch m) => new(m.Id, m.Status, m.Settings, m.Players.Select(Player).ToList(),
        m.CreatedAt, m.StartedAt, m.FinishedAt, m.Result);
    private static double Elapsed(OnlineMatch m, DateTime now) => m.ClockStartedAt is null ? 0 : Math.Max(0, (now - m.ClockStartedAt.Value).TotalMilliseconds);
    public static MatchSnapshot Snapshot(OnlineMatch m, DateTime now, string? userId = null)
    {
        double white = m.WhiteMilliseconds, black = m.BlackMilliseconds;
        if (m.Status == "InProgress") { if (m.Turn == "White") white -= Elapsed(m, now); else black -= Elapsed(m, now); }
        string fen = FenCodec.Write(GameRules.Read(m));
        var custom = m.Aram is null ? null : OnlineJson.Clone(m.Aram);
        if (custom is not null && userId is not null)
            foreach (var s in custom.Sides) if (s.Team != m.Players.Single(p => p.UserId == userId).Color) s.DraftOptions.Clear();
        return new(m.Id, m.Status, m.Settings, m.StateVersion, m.EventSequence, now, m.Turn,
            m.Board.Select(p => new PieceSnapshot(p.Id, p.Kind, p.Team, p.Square, p.HasMoved, p.Forward)).ToList(),
            m.Aram is null ? fen : null, fen.Split(' ')[2], m.EnPassantTarget, m.HalfMoveClock, m.FullMoveNumber,
            new(Math.Max(0, white), Math.Max(0, black), m.ClockStartedAt is not null && m.Status == "InProgress" ? m.Turn : null, now),
            m.Players.Select(Player).ToList(), m.ReadyDeadline, m.DrawOffer, custom, m.Result, m.RematchId);
    }
    private GameSettings ValidateSettings(GameSettings settings)
    {
        if (settings is null || settings.ProtocolVersion != config.ProtocolVersion) throw new OnlineException("ProtocolVersionMismatch", 400);
        string mode = settings.Mode?.Trim().ToUpperInvariant() switch { "CLASSIC" => "Classic", "ARAM" => "Aram", _ => "" };
        string region = settings.Region?.Trim().ToUpperInvariant() ?? "";
        if (mode == "" || region.Length is < 2 or > 16 || region.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') ||
            settings.InitialSeconds is < 60 or > 7200 || settings.IncrementSeconds is < 0 or > 60)
            throw new OnlineException("InvalidSettings", 400);
        return settings with { Mode = mode, Region = region };
    }
    private static void RequestId(string? id)
    { if (string.IsNullOrWhiteSpace(id) || id.Length > 80) throw new OnlineException("InvalidCommandId", 400); }
    private async Task<User> UserAsync(IClientSessionHandle s, string id, CancellationToken ct)
    {
        var u = await store.Users.Find(s, u => u.Id == ObjectId.Parse(id)).FirstOrDefaultAsync(ct);
        if (u is null || !u.IsActive) throw new OnlineException("Forbidden", 403);
        return u;
    }
    private async Task<OnlineMatch> MatchAsync(IClientSessionHandle s, string id, string userId, CancellationToken ct)
    {
        var m = await store.Matches.Find(s, m => m.Id == id).FirstOrDefaultAsync(ct);
        if (m is null) throw new OnlineException("MatchNotFound", 404);
        if (!m.Players.Any(p => p.UserId == userId)) throw new OnlineException("Forbidden", 403);
        return m;
    }
    private async Task AvailableAsync(IClientSessionHandle s, string userId, CancellationToken ct)
    {
        var seat = await store.Seats.Find(s, x => x.UserId == userId).FirstOrDefaultAsync(ct);
        if (seat is not null) throw new OnlineException(seat.Kind switch { "Ticket" => "AlreadyQueued", "Room" => "AlreadyInRoom", _ => "AlreadyInMatch" });
    }
    private async Task Emit(IClientSessionHandle s, string type, object payload, List<string> recipients, CancellationToken ct,
        OnlineMatch? match = null)
    {
        if (match is not null) match.EventSequence++;
        await store.Events.InsertOneAsync(s, new OnlineEvent { Type = type, MatchId = match?.Id,
            Sequence = match?.EventSequence ?? 0, StateVersion = match?.StateVersion ?? 0,
            Recipients = recipients.Distinct().ToList(), PayloadJson = OnlineJson.Write(payload), CreatedAt = DateTime.UtcNow }, cancellationToken: ct);
    }
    private async Task MatchEvent(IClientSessionHandle s, OnlineMatch m, string type, DateTime now, CancellationToken ct,
        string? commandId = null, object? command = null)
    {
        // Reserve sequence before taking the snapshot; all snapshots carry the event watermark.
        m.EventSequence++;
        await store.Events.InsertOneAsync(s, new OnlineEvent { Type = type, MatchId = m.Id, Sequence = m.EventSequence,
            StateVersion = m.StateVersion, Recipients = m.Players.Select(p => p.UserId).ToList(),
            PayloadJson = OnlineJson.Write(new { state = Snapshot(m, now), commandId, command }), CreatedAt = now }, cancellationToken: ct);
    }
    private Task Save(IClientSessionHandle s, OnlineMatch m, CancellationToken ct) =>
        store.Matches.ReplaceOneAsync(s, x => x.Id == m.Id, m, cancellationToken: ct);

    public async Task<MatchSnapshot> State(string userId, string matchId, CancellationToken ct)
    {
        lease.RequireOwner(); OnlineIdentity.Id(matchId);
        return await store.Transaction(async (s, token) =>
        {
            await UserAsync(s, userId, token); var m = await MatchAsync(s, matchId, userId, token);
            await Advance(s, m, DateTime.UtcNow, token);
            return Snapshot(m, DateTime.UtcNow, userId);
        }, ct);
    }
    public async Task<MatchSnapshot?> CurrentMatch(string userId, CancellationToken ct)
    {
        lease.RequireOwner();
        return await store.Transaction(async (s, token) =>
        {
            await UserAsync(s, userId, token);
            var seat = await store.Seats.Find(s, x => x.UserId == userId && x.Kind == "Match").FirstOrDefaultAsync(token);
            if (seat is null) return null;
            var m = await MatchAsync(s, seat.ReferenceId, userId, token); await Advance(s, m, DateTime.UtcNow, token);
            return Active(m) ? Snapshot(m, DateTime.UtcNow, userId) : null;
        }, ct);
    }
    public async Task<MatchSummary> GetMatch(string userId, string matchId, CancellationToken ct)
    { await State(userId, matchId, ct); return Summary((await store.Matches.Find(m => m.Id == matchId).FirstOrDefaultAsync(ct))!); }
    public async Task<OfficialResult> Result(string userId, string matchId, CancellationToken ct)
    {
        var snapshot = await State(userId, matchId, ct);
        return snapshot.Result ?? throw new OnlineException("ResultNotReady");
    }
    public async Task<Page<MatchSummary>> History(string userId, int page, int pageSize, CancellationToken ct)
    {
        lease.RequireOwner(); Pagination(page, pageSize);
        if (!await store.Users.Find(u => u.Id == ObjectId.Parse(userId) && u.IsActive).AnyAsync(ct)) throw new OnlineException("Forbidden", 403);
        var f = Builders<OnlineMatch>.Filter.ElemMatch(m => m.Players, p => p.UserId == userId) &
            Builders<OnlineMatch>.Filter.In(m => m.Status, new[] { "Finished", "Cancelled" });
        var count = await store.Matches.CountDocumentsAsync(f, cancellationToken: ct);
        var matches = await store.Matches.Find(f).SortByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync(ct);
        return new(page, pageSize, count, matches.Select(Summary).ToList());
    }
    public async Task<Page<OnlineMove>> Moves(string userId, string matchId, int page, int pageSize, long? afterSequence, CancellationToken ct)
    {
        await State(userId, matchId, ct); Pagination(page, pageSize);
        if (afterSequence < 0) throw new OnlineException("InvalidPagination", 400);
        var f = Builders<OnlineMove>.Filter.Eq(x => x.MatchId, matchId);
        if (afterSequence is not null) f &= Builders<OnlineMove>.Filter.Gt(x => x.Sequence, afterSequence.Value);
        var count = await store.Moves.CountDocumentsAsync(f, cancellationToken: ct);
        var moves = await store.Moves.Find(f).SortBy(x => x.Sequence).Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync(ct);
        return new(page, pageSize, count, moves);
    }
    public async Task<EventReplay> Replay(string userId, string matchId, long afterSequence, int limit, CancellationToken ct)
    {
        var state = await State(userId, matchId, ct);
        if (afterSequence < 0 || afterSequence > state.EventSequence || limit is < 1 or > 100) throw new OnlineException("InvalidSequence", 400);
        var events = await store.Events.Find(e => e.MatchId == matchId && e.Sequence > afterSequence && e.Sequence <= state.EventSequence)
            .SortBy(e => e.Sequence).Limit(limit).ToListAsync(ct);
        return new(state.EventSequence, events.Select(e => new PublicEvent(e.Id, e.Type, e.MatchId, e.Sequence,
            e.StateVersion, e.CreatedAt, OnlineJson.EventPayload(e, userId))).ToList());
    }
    private static void Pagination(int page, int size)
    { if (page is < 1 or > 100000 || size is < 1 or > 100) throw new OnlineException("InvalidPagination", 400); }

    private async Task<List<LoadoutSnapshot>> Loadout(IClientSessionHandle s, User user, CancellationToken ct)
    {
        var result = new List<LoadoutSnapshot>();
        foreach (var entry in new[] { (Id: user.Equipped.ChessSkinId, Type: "CHESS"), (Id: user.Equipped.BoardSkinId, Type: "BOARD") })
        {
            if (entry.Id is null) continue;
            var item = await store.Items.Find(s, x => x.Id == entry.Id.Value && x.IsActive).FirstOrDefaultAsync(ct);
            if (item is null || !item.Type.Contains(entry.Type, StringComparison.OrdinalIgnoreCase) ||
                !await store.PlayerItems.Find(s, x => x.UserId == user.Id && x.ItemId == entry.Id.Value).AnyAsync(ct))
                throw new OnlineException("InvalidLoadout");
            result.Add(new(item.Id.ToString(), item.Code, item.UnityAssetKey, item.Type));
        }
        return result;
    }
    private async Task<OnlineMatch> CreateMatch(IClientSessionHandle s, List<string> ids, GameSettings settings, bool rated, DateTime now, CancellationToken ct)
    {
        if (ids.Count != 2 || ids.Distinct().Count() != 2) throw new OnlineException("InvalidParticipants");
        var ordered = RandomNumberGenerator.GetInt32(2) == 0 ? ids : ids.AsEnumerable().Reverse().ToList();
        var m = new OnlineMatch { Settings = settings, Rated = rated, CreatedAt = now, ReadyDeadline = now.AddSeconds(config.ReadySeconds),
            WhiteMilliseconds = settings.InitialSeconds * 1000.0, BlackMilliseconds = settings.InitialSeconds * 1000.0 };
        foreach (var id in ordered)
        {
            var user = await UserAsync(s, id, ct);
            bool connected = await store.Connections.Find(s, x => x.UserId == id && x.ExpiresAt > now).AnyAsync(ct);
            m.Players.Add(new MatchPlayer { UserId = id, Username = user.Username, DisplayName = user.Profile.DisplayName,
                Color = m.Players.Count == 0 ? "White" : "Black", Rating = user.Stats.Elo, Connected = connected, Loadout = await Loadout(s, user, ct) });
            await store.Seats.ReplaceOneAsync(s, x => x.UserId == id, new OnlineSeat { UserId = id, Kind = "Match", ReferenceId = m.Id },
                new ReplaceOptions { IsUpsert = true }, ct);
        }
        GameRules.Write(m, FenCodec.Parse(FenCodec.InitialPosition));
        if (settings.Mode == "Aram") aram.Initialize(m, now);
        else GameRules.RecordPosition(m);
        await MatchEvent(s, m, "MatchFound", now, ct);
        await store.Matches.InsertOneAsync(s, m, cancellationToken: ct);
        return m;
    }
    private static void ConsumeClock(OnlineMatch m, DateTime now)
    {
        if (m.ClockStartedAt is null || m.Status != "InProgress") return;
        if (m.Turn == "White") m.WhiteMilliseconds = Math.Max(0, m.WhiteMilliseconds - Elapsed(m, now));
        else m.BlackMilliseconds = Math.Max(0, m.BlackMilliseconds - Elapsed(m, now));
        m.ClockStartedAt = now;
    }
    private async Task StartIfReady(IClientSessionHandle s, OnlineMatch m, DateTime now, CancellationToken ct)
    {
        if (m.Status != "AwaitingReady") return;
        if (ReadyExpired(m, now)) { await Cancel(s, m, "ReadyTimeout", now, ct); return; }
        if (!m.Players.All(p => p.Ready && p.Connected) || !aram.SetupDone(m)) return;
        // Revalidate against current inventory and then freeze this loadout for the lifetime of the match.
        try
        {
            foreach (var p in m.Players) p.Loadout = await Loadout(s, await UserAsync(s, p.UserId, ct), ct);
        }
        catch (OnlineException ex) when (ex.Code is "InvalidLoadout" or "Forbidden")
        { await Cancel(s, m, ex.Code, now, ct); return; }
        m.Status = "InProgress"; m.StartedAt = now; m.ClockStartedAt = now;
        foreach (var p in m.Players) p.ReconnectDeadline = null;
        var initialOutcome = rules.Outcome(m);
        await MatchEvent(s, m, "MatchStarted", now, ct);
        if (initialOutcome is { } outcome) await Finish(s, m, outcome.WinnerTeam, outcome.Reason, now, ct);
    }
    private async Task Cancel(IClientSessionHandle s, OnlineMatch m, string reason, DateTime now, CancellationToken ct)
    {
        if (!Active(m)) return;
        m.Status = "Cancelled"; m.FinishedAt = now; m.ClockStartedAt = null; m.StateVersion++;
        m.Result = new OfficialResult { Outcome = "Cancelled", Reason = reason, FinishedAt = now };
        await store.Seats.DeleteManyAsync(s, x => x.ReferenceId == m.Id && x.Kind == "Match", cancellationToken: ct);
        await MatchEvent(s, m, "MatchCancelled", now, ct); await Save(s, m, ct);
    }
    private async Task Finish(IClientSessionHandle s, OnlineMatch m, string? winnerTeam, string reason, DateTime now, CancellationToken ct)
    {
        if (m.Status != "InProgress") return;
        ConsumeClock(m, now); m.ClockStartedAt = null; m.DrawOffer = null;
        m.Status = "Finished"; m.FinishedAt = now; m.StateVersion++;
        var result = new OfficialResult { Outcome = winnerTeam is null ? "Draw" : winnerTeam + "Win",
            WinnerId = m.Players.FirstOrDefault(p => p.Color == winnerTeam)?.UserId, Reason = reason, FinishedAt = now };
        // Account deactivation after start must not prevent an official match from settling.
        var whiteId = ObjectId.Parse(m.Players.Single(p => p.Color == "White").UserId);
        var blackId = ObjectId.Parse(m.Players.Single(p => p.Color == "Black").UserId);
        var white = await store.Users.Find(s, u => u.Id == whiteId).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("A match participant was deleted.");
        var black = await store.Users.Find(s, u => u.Id == blackId).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("A match participant was deleted.");
        double expected = 1 / (1 + Math.Pow(10, (black.Stats.Elo - white.Stats.Elo) / 400.0));
        double score = winnerTeam is null ? .5 : winnerTeam == "White" ? 1 : 0;
        int whiteDelta = m.Rated ? (int)Math.Round(config.EloK * (score - expected), MidpointRounding.AwayFromZero) : 0;
        // Only rated matchmaking awards wallet currency. Private rooms and all rematches
        // are unrated, so repeated resignations cannot mint currency through these flows.
        bool rewardEligible = m.Rated;
        foreach (var user in new[] { white, black })
        {
            var p = m.Players.Single(p => p.UserId == user.Id.ToString());
            bool win = p.Color == winnerTeam, draw = winnerTeam is null;
            int delta = p.Color == "White" ? whiteDelta : -whiteDelta;
            var entry = new PlayerResult { UserId = p.UserId, RatingBefore = user.Stats.Elo,
                RatingAfter = Math.Max(0, user.Stats.Elo + delta), Golds = rewardEligible ? (win ? 360 : draw ? 180 : 85) : 0,
                Diamonds = rewardEligible ? (win ? 12 : draw ? 6 : 3) : 0, Tickets = rewardEligible && win ? 1 : 0 };
            entry.RatingChange = entry.RatingAfter - entry.RatingBefore;
            var update = Builders<User>.Update.Set(u => u.Stats.Elo, entry.RatingAfter)
                .Inc(u => u.Stats.GamesPlayed, 1).Inc(u => u.Stats.Wins, win ? 1 : 0)
                .Inc(u => u.Stats.Draws, draw ? 1 : 0).Inc(u => u.Stats.Losses, !win && !draw ? 1 : 0)
                .Inc(u => u.Wallet.Golds, entry.Golds).Inc(u => u.Wallet.Diamonds, entry.Diamonds)
                .Inc(u => u.Wallet.Tickets, entry.Tickets).Set(u => u.UpdatedAt, now);
            var updated = await store.Users.FindOneAndUpdateAsync(s, Builders<User>.Filter.Eq(u => u.Id, user.Id), update,
                new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After }, ct);
            var currencies = new[] { (Currency: "GOLDS", Amount: entry.Golds, Balance: updated.Wallet.Golds),
                (Currency: "DIAMONDS", Amount: entry.Diamonds, Balance: updated.Wallet.Diamonds),
                (Currency: "TICKETS", Amount: entry.Tickets, Balance: updated.Wallet.Tickets) };
            for (int i = 0; i < currencies.Length; i++) if (currencies[i].Amount > 0)
                await store.Ledger.InsertOneAsync(s, new CurrencyTransaction { UserId = user.Id, Currency = currencies[i].Currency,
                    Amount = currencies[i].Amount, BalanceAfter = currencies[i].Balance, Source = "ONLINE_MATCH",
                    RequestId = "match:" + m.Id, EntryIndex = i, CreatedAt = now }, cancellationToken: ct);
            result.Players.Add(entry);
        }
        m.Result = result;
        await store.Seats.DeleteManyAsync(s, x => x.ReferenceId == m.Id && x.Kind == "Match", cancellationToken: ct);
        await MatchEvent(s, m, "MatchEnded", now, ct); await Save(s, m, ct);
    }
    private static bool ReadyExpired(OnlineMatch m, DateTime now) => now >= m.ReadyDeadline ||
        m.Aram is { Phase: not "Playing" } a && now >= a.SetupDeadline;

    private async Task Advance(IClientSessionHandle s, OnlineMatch m, DateTime now, CancellationToken ct)
    {
        if (!Active(m)) return;
        if (m.Status == "AwaitingReady")
        {
            // Expiration takes precedence over formation fallback, including delayed sweeps/recovery.
            if (ReadyExpired(m, now)) { await Cancel(s, m, "ReadyTimeout", now, ct); return; }
            if (aram.Tick(m, now))
            {
                m.StateVersion++; await MatchEvent(s, m, "GameStateUpdated", now, ct);
                await StartIfReady(s, m, now, ct); await Save(s, m, ct);
            }
            return;
        }
        // Resolve competing deadlines by their actual expiration instant, not worker iteration order.
        var clockDeadline = m.ClockStartedAt!.Value.AddMilliseconds(m.Turn == "White" ? m.WhiteMilliseconds : m.BlackMilliseconds);
        var absent = m.Players.Where(p => !p.Connected && p.ReconnectDeadline is not null).OrderBy(p => p.ReconnectDeadline).ToList();
        var disconnectDeadline = absent.FirstOrDefault()?.ReconnectDeadline;
        if (clockDeadline <= now && (disconnectDeadline is null || clockDeadline <= disconnectDeadline))
        {
            var winner = GameRules.Other(m.Turn);
            await Finish(s, m, GameRules.CanPossiblyMate(m, winner) ? winner : null, "Timeout", clockDeadline, ct); return;
        }
        if (disconnectDeadline <= now)
        {
            var loser = absent[0];
            bool both = absent.Count == 2 && absent[1].ReconnectDeadline == loser.ReconnectDeadline;
            await Finish(s, m, both ? null : GameRules.Other(loser.Color), both ? "BothAbandoned" : "Abandonment", disconnectDeadline!.Value, ct); return;
        }
        bool changed = false;
        if (m.DrawOffer is { } offer && offer.ExpiresAt <= now)
        {
            m.DrawOffer = null; m.StateVersion++; changed = true;
            await Emit(s, "DrawResolved", new { matchId = m.Id, offerId = offer.Id, accepted = false, reason = "DrawOfferExpired" },
                m.Players.Select(p => p.UserId).ToList(), ct, m);
        }
        if (aram.Tick(m, now))
        {
            m.StateVersion++; changed = true;
            var outcome = rules.Outcome(m);
            await MatchEvent(s, m, "GameStateUpdated", now, ct);
            if (outcome is { } ended) await Finish(s, m, ended.WinnerTeam, ended.Reason, now, ct);
        }
        if (changed) await Save(s, m, ct);
    }
}
