using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;

namespace ChessGame.Api.Online;

public sealed class OnlineWorker(OnlineStore store, OnlineService service, OnlineRuntimeLease lease,
    LiveConnections live, IHubContext<GameHub> hub, IHostApplicationLifetime lifetime, ILogger<OnlineWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Render starts the replacement before stopping the previous container.
        // Let HTTP start while we wait; online endpoints remain fenced during takeover/recovery.
        await Task.Yield();
        using var runtime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var ct = runtime.Token;
        Task heartbeat = Task.CompletedTask;
        try
        {
            await WaitForLease(ct);
            heartbeat = Heartbeat(runtime);
            await service.Recover(ct);
            ct.ThrowIfCancellationRequested();
            lease.MarkReady();
            logger.LogInformation("Online runtime ready; lease acquired and match recovery completed.");

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    lease.RequireOwner();
                    await service.Sweep(live, ct);
                    await Publish(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (OnlineException) when (!lease.HasLease)
                {
                    logger.LogCritical("Online runtime lease was lost; stopping to preserve authority.");
                    lifetime.StopApplication();
                    break;
                }
                catch (Exception ex) { logger.LogError(ex, "Online worker iteration failed; persisted state and outbox will be retried."); }
                if (!await timer.WaitForNextTickAsync(ct)) break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Online runtime initialization failed; stopping before accepting online commands.");
            lifetime.StopApplication();
        }
        finally
        {
            lease.MarkUnavailable();
            runtime.Cancel();
            await heartbeat;
        }
    }
    private async Task WaitForLease(CancellationToken ct)
    {
        bool waitingLogged = false;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await lease.Acquire(ct)) return;
                if (!waitingLogged)
                {
                    logger.LogInformation("Another instance owns online gameplay. HTTP is available; waiting for its lease to be released or expire.");
                    waitingLogged = true;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogError(ex, "Online lease acquisition failed; retrying in 3 seconds."); }
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }
    }
    private async Task Heartbeat(CancellationTokenSource runtime)
    {
        var ct = runtime.Token;
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
                    if (!lease.HasLease)
                    {
                        lease.MarkUnavailable();
                        runtime.Cancel();
                        lifetime.StopApplication();
                        break;
                    }
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
    {
        lease.MarkUnavailable();
        await base.StopAsync(ct);
        await lease.Release(ct);
    }
}
