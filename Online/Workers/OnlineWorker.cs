using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;

namespace ChessGame.Api.Online;

public sealed class OnlineWorker(OnlineStore store, OnlineService service, OnlineRuntimeLease lease,
    LiveConnections live, IHubContext<GameHub> hub, IHostApplicationLifetime lifetime, ILogger<OnlineWorker> logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken ct)
    {
        if (!await lease.Acquire(ct)) throw new InvalidOperationException("Another API instance owns online gameplay. Run one online instance until a SignalR backplane is configured.");
        using var renewDuringRecovery = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeat = Heartbeat(renewDuringRecovery.Token);
        try { await service.Recover(ct); }
        finally { renewDuringRecovery.Cancel(); await heartbeat; }
        await base.StartAsync(ct);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var heartbeat = Heartbeat(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                lease.RequireOwner();
                await service.Sweep(live, stoppingToken);
                await Publish(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (OnlineException ex) when (ex.Code == "OnlineLeaseLost")
            { logger.LogCritical("Online runtime lease was lost; stopping to preserve authority."); lifetime.StopApplication(); break; }
            catch (Exception ex) { logger.LogError(ex, "Online worker iteration failed; persisted state and outbox will be retried."); }
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
        await heartbeat;
    }
    private async Task Heartbeat(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try { await lease.Renew(ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Online lease renewal failed.");
                    try { lease.RequireOwner(); }
                    catch (OnlineException) { lifetime.StopApplication(); break; }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
    private async Task Publish(CancellationToken ct)
    {
        var events = await store.Events.Find(e => !e.Published).SortBy(e => e.CreatedAt).ThenBy(e => e.Sequence).Limit(100).ToListAsync(ct);
        foreach (var e in events)
        {
            lease.RequireOwner();
            foreach (var userId in e.Recipients)
            {
                var id = MongoDB.Bson.ObjectId.Parse(userId);
                if (!await store.Users.Find(u => u.Id == id && u.IsActive).AnyAsync(ct)) continue;
                await hub.Clients.User(userId).SendAsync(e.Type, new PublicEvent(e.Id, e.Type, e.MatchId, e.Sequence,
                    e.StateVersion, e.CreatedAt, OnlineJson.EventPayload(e, userId)), ct);
            }
            await store.Events.UpdateOneAsync(x => x.Id == e.Id, Builders<OnlineEvent>.Update.Set(x => x.Published, true), cancellationToken: ct);
        }
    }
    public override async Task StopAsync(CancellationToken ct)
    { await base.StopAsync(ct); await lease.Release(ct); }
}
