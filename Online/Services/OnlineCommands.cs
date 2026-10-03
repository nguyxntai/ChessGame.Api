using System.Security.Cryptography;
using System.Text;
using MongoDB.Driver;

namespace ChessGame.Api.Online;

public sealed partial class OnlineService
{
    public Task<CommandAck> SubmitMove(string userId, string matchId, string commandId, long expectedVersion, MoveCommand move, CancellationToken ct) =>
        Command(userId, matchId, commandId, "Move", expectedVersion, move, null, ct);
    public Task<CommandAck> UseAbility(string userId, string matchId, string commandId, long expectedVersion, AbilityCommand ability, CancellationToken ct) =>
        Command(userId, matchId, commandId, "Ability", expectedVersion, ability, null, ct);
    public Task<CommandAck> Resign(string userId, string matchId, string commandId, CancellationToken ct) =>
        Command(userId, matchId, commandId, "Resign", null, null, null, ct);
    public Task<CommandAck> OfferDraw(string userId, string matchId, string commandId, CancellationToken ct) =>
        Command(userId, matchId, commandId, "OfferDraw", null, null, null, ct);
    public Task<CommandAck> ClaimDraw(string userId, string matchId, string commandId, CancellationToken ct) =>
        Command(userId, matchId, commandId, "ClaimDraw", null, null, null, ct);
    public Task<CommandAck> RespondDraw(string userId, string matchId, string offerId, bool accept, CancellationToken ct) =>
        Command(userId, matchId, "draw:" + offerId + ":" + accept, "RespondDraw", null, accept, offerId, ct);

    private async Task<CommandAck> Command(string userId, string matchId, string commandId, string kind,
        long? expectedVersion, object? payload, string? offerId, CancellationToken ct)
    {
        lease.RequireOwner(); OnlineIdentity.Id(matchId); RequestId(commandId);
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(OnlineJson.Write(new { kind, expectedVersion, payload, offerId }))));
        return await store.Transaction(async (s, token) =>
        {
            await UserAsync(s, userId, token);
            var original = await MatchAsync(s, matchId, userId, token);
            var existing = await store.Commands.Find(s, x => x.MatchId == matchId && x.UserId == userId && x.CommandId == commandId).FirstOrDefaultAsync(token);
            if (existing is not null)
            {
                if (existing.Fingerprint != fingerprint) throw new OnlineException("CommandIdConflict");
                return existing.Ack with { Replayed = true };
            }
            var now = DateTime.UtcNow;
            await Advance(s, original, now, token);
            var m = OnlineJson.Clone(original);
            var actor = m.Players.Single(p => p.UserId == userId);
            string? error = null;
            try
            {
                bool setup = kind == "Ability" && m.Status == "AwaitingReady" && m.Aram is not null;
                if (m.Status != "InProgress" && !setup) throw new OnlineException("MatchNotActive");
                if (expectedVersion is not null && expectedVersion != m.StateVersion) throw new OnlineException("StateVersionMismatch");
                if (expectedVersion < 0) throw new OnlineException("StateVersionMismatch");
                if (kind == "Move" && payload is not MoveCommand) throw new OnlineException("InvalidMove", 400);
                if (kind == "Ability" && (payload is not AbilityCommand ability || string.IsNullOrWhiteSpace(ability.Kind) ||
                    ability.Kind.Length > 40 || ability.PieceIds is null || ability.PieceIds.Count > 64 ||
                    ability.Formation is null || ability.Formation.Count > 64 || ability.Formation.Any(x => x is null)))
                    throw new OnlineException("InvalidAbility", 400);
                // Validate all game changes on a detached working copy before writing side effects.
                if (kind == "Move") rules.ApplyMove(m, actor.Color, (MoveCommand)payload!);
                if (kind == "Ability") rules.ApplyAbility(m, actor.Color, (AbilityCommand)payload!, now);
                if (kind == "OfferDraw")
                {
                    if (m.DrawOffer is not null) throw new OnlineException("DrawOfferPending");
                    m.DrawOffer = new DrawOffer { UserId = userId, ExpiresAt = now.AddSeconds(config.DrawOfferSeconds) };
                }
                if (kind == "RespondDraw")
                {
                    if (m.DrawOffer is null || m.DrawOffer.Id != offerId || m.DrawOffer.ExpiresAt <= now) throw new OnlineException("DrawOfferExpired");
                    if (m.DrawOffer.UserId == userId) throw new OnlineException("Forbidden", 403);
                    m.DrawOffer = null;
                }
                if (kind == "ClaimDraw") rules.ClaimDraw(m, actor.Color);
            }
            catch (OnlineException ex) { error = ex.Code; }
            if (error is null)
            {
                // Charge elapsed time to the pre-command turn. Abilities may or may not spend a turn.
                if (original.Status == "InProgress")
                {
                    ConsumeClock(original, now);
                    m.WhiteMilliseconds = original.WhiteMilliseconds; m.BlackMilliseconds = original.BlackMilliseconds;
                    m.ClockStartedAt = now;
                    if (kind == "Move" || kind == "Ability" && original.Turn != m.Turn)
                    {
                        if (actor.Color == "White") m.WhiteMilliseconds += m.Settings.IncrementSeconds * 1000.0;
                        else m.BlackMilliseconds += m.Settings.IncrementSeconds * 1000.0;
                    }
                }
                m.StateVersion++;
                // Compute pending escape/decision state before taking the outgoing snapshot.
                var outcome = m.Status == "InProgress" ? rules.Outcome(m) : null;
                if (kind is "Move" or "Ability")
                {
                    await MatchEvent(s, m, "GameStateUpdated", now, token, commandId, payload);
                    await store.Moves.InsertOneAsync(s, new OnlineMove { MatchId = m.Id, UserId = userId, CommandId = commandId,
                        Sequence = m.EventSequence, StateVersion = m.StateVersion, Kind = kind, PayloadJson = OnlineJson.Write(payload), PlayedAt = now }, cancellationToken: token);
                }
                if (kind == "OfferDraw") await MatchEvent(s, m, "DrawOffered", now, token);
                if (kind == "RespondDraw") await MatchEvent(s, m, "DrawResolved", now, token);
                await StartIfReady(s, m, now, token);
                if (kind == "Resign") await Finish(s, m, GameRules.Other(actor.Color), "Resignation", now, token);
                else if (kind == "ClaimDraw") await Finish(s, m, null, rules.ClaimDraw(m, actor.Color), now, token);
                else if (kind == "RespondDraw" && payload is true)
                    await Finish(s, m, null, "DrawAgreement", now, token);
                else if (outcome is { } resolved)
                    await Finish(s, m, resolved.WinnerTeam, resolved.Reason, now, token);
                await Save(s, m, token);
            }
            else m = original;
            var ack = new CommandAck(commandId, error is null, error, m.StateVersion, m.EventSequence, now);
            await store.Commands.InsertOneAsync(s, new OnlineCommand { MatchId = matchId, UserId = userId,
                CommandId = commandId, Fingerprint = fingerprint, Ack = ack }, cancellationToken: token);
            if (error is not null)
                await Emit(s, "CommandRejected", ack, new() { userId }, token);
            return ack;
        }, ct);
    }
    public async Task<MatchSnapshot> SetReady(string userId, string matchId, CancellationToken ct)
    {
        lease.RequireOwner(); OnlineIdentity.Id(matchId);
        return await store.Transaction(async (s, token) =>
        {
            await UserAsync(s, userId, token); var m = await MatchAsync(s, matchId, userId, token); var now = DateTime.UtcNow;
            await Advance(s, m, now, token);
            if (!Active(m)) throw new OnlineException("MatchNotActive");
            var player = m.Players.Single(p => p.UserId == userId);
            if (!player.Ready)
            {
                if (m.Status != "AwaitingReady") throw new OnlineException("MatchNotActive");
                player.Ready = true; m.StateVersion++;
                await MatchEvent(s, m, "PlayerReadyChanged", now, token);
                await StartIfReady(s, m, now, token); await Save(s, m, token);
            }
            return Snapshot(m, now, userId);
        }, ct);
    }
    public async Task<MatchSnapshot> Decline(string userId, string matchId, CancellationToken ct)
    {
        lease.RequireOwner(); OnlineIdentity.Id(matchId);
        return await store.Transaction(async (s, token) =>
        {
            await UserAsync(s, userId, token); var m = await MatchAsync(s, matchId, userId, token); var now = DateTime.UtcNow;
            await Advance(s, m, now, token);
            if (m.Status != "AwaitingReady") throw new OnlineException("MatchNotActive");
            await Cancel(s, m, "PlayerDeclined", now, token); return Snapshot(m, now, userId);
        }, ct);
    }
    public async Task<MatchSnapshot> Rematch(string userId, string matchId, bool? accept, CancellationToken ct)
    {
        lease.RequireOwner(); OnlineIdentity.Id(matchId);
        return await store.Transaction(async (s, token) =>
        {
            await UserAsync(s, userId, token); var m = await MatchAsync(s, matchId, userId, token); var now = DateTime.UtcNow;
            if (m.Status != "Finished" || now > m.FinishedAt!.Value.AddMinutes(2)) throw new OnlineException("RematchExpired");
            if (m.RematchId is not null) return Snapshot(await MatchAsync(s, m.RematchId, userId, token), now, userId);
            if (accept == false)
            {
                m.RematchRequests.Clear(); m.StateVersion++;
                await MatchEvent(s, m, "RematchResolved", now, token); await Save(s, m, token); return Snapshot(m, now, userId);
            }
            if (accept == true && (m.RematchRequests.Count == 0 || m.RematchRequests.All(x => x == userId))) throw new OnlineException("RematchNotOffered");
            await AvailableAsync(s, userId, token);
            if (!m.RematchRequests.Contains(userId)) m.RematchRequests.Add(userId);
            m.StateVersion++;
            if (m.RematchRequests.Count == 2)
            {
                foreach (var id in m.RematchRequests) await AvailableAsync(s, id, token);
                var next = await CreateMatch(s, m.RematchRequests, m.Settings, false, now, token);
                m.RematchId = next.Id; await MatchEvent(s, m, "RematchResolved", now, token); await Save(s, m, token);
                return Snapshot(next, now, userId);
            }
            await MatchEvent(s, m, "RematchRequested", now, token); await Save(s, m, token); return Snapshot(m, now, userId);
        }, ct);
    }
}
