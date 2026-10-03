using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ChessGame.Api.Online;

[Authorize]
public sealed class GameHub(OnlineService service, LiveConnections live) : Hub
{
    private string UserId => OnlineIdentity.UserId(Context.User);
    public override async Task OnConnectedAsync()
    {
        try
        {
            await service.Connected(UserId, Context.ConnectionId, Context.ConnectionAborted);
            live.Users[Context.ConnectionId] = UserId;
            await base.OnConnectedAsync();
        }
        catch (OnlineException ex) { Context.Abort(); throw new HubException(ex.Code); }
    }
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        live.Users.TryRemove(Context.ConnectionId, out var id);
        if (id is not null)
            try { await service.Disconnected(id, Context.ConnectionId, CancellationToken.None); }
            catch (OnlineException ex) when (ex.Status == 503) { /* Expiration/recovery will reconcile presence. */ }
        await base.OnDisconnectedAsync(exception);
    }
    private static async Task<T> Invoke<T>(Func<Task<T>> action)
    { try { return await action(); } catch (OnlineException ex) { throw new HubException(ex.Code); } }
    // Events are addressed to authenticated user IDs, so live delivery is active before this snapshot.
    // Buffer them while SubscribeMatch is pending, discard <= ResumeAfterSequence, then apply/replay in order.
    public Task<SubscriptionSnapshot> SubscribeMatch(string matchId) => Invoke(async () =>
    {
        var state = await service.State(UserId, matchId, Context.ConnectionAborted);
        return new SubscriptionSnapshot(state, state.EventSequence);
    });
    public Task<EventReplay> ReplayEvents(string matchId, long afterSequence, int limit = 100) =>
        Invoke(() => service.Replay(UserId, matchId, afterSequence, limit, Context.ConnectionAborted));
    public Task<MatchSnapshot> AcceptMatch(string matchId) => Invoke(() => service.State(UserId, matchId, Context.ConnectionAborted));
    public Task<MatchSnapshot> DeclineMatch(string matchId) => Invoke(() => service.Decline(UserId, matchId, Context.ConnectionAborted));
    public Task<MatchSnapshot> SetReady(string matchId) => Invoke(() => service.SetReady(UserId, matchId, Context.ConnectionAborted));
    public Task<CommandAck> SubmitMove(string matchId, string commandId, long expectedVersion, MoveCommand move) =>
        Invoke(() => service.SubmitMove(UserId, matchId, commandId, expectedVersion, move, Context.ConnectionAborted));
    public Task<CommandAck> UseAbility(string matchId, string commandId, long expectedVersion, AbilityCommand ability) =>
        Invoke(() => service.UseAbility(UserId, matchId, commandId, expectedVersion, ability, Context.ConnectionAborted));
    public Task<CommandAck> Resign(string matchId, string commandId) => Invoke(() => service.Resign(UserId, matchId, commandId, Context.ConnectionAborted));
    public Task<CommandAck> OfferDraw(string matchId, string commandId) => Invoke(() => service.OfferDraw(UserId, matchId, commandId, Context.ConnectionAborted));
    public Task<CommandAck> RespondDraw(string matchId, string offerId, bool accept) => Invoke(() => service.RespondDraw(UserId, matchId, offerId, accept, Context.ConnectionAborted));
    public Task<CommandAck> ClaimDraw(string matchId, string commandId) => Invoke(() => service.ClaimDraw(UserId, matchId, commandId, Context.ConnectionAborted));
    public Task<MatchSnapshot> RequestRematch(string matchId) => Invoke(() => service.Rematch(UserId, matchId, null, Context.ConnectionAborted));
    public Task<MatchSnapshot> RespondRematch(string matchId, bool accept) => Invoke(() => service.Rematch(UserId, matchId, accept, Context.ConnectionAborted));
}
