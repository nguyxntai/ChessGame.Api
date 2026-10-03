using MongoDB.Driver;

namespace ChessGame.Api.Online;

/// <summary>Fence online operations until this process owns the lease and recovery completes.</summary>
public sealed class OnlineRuntimeLease
{
    private readonly IMongoCollection<OnlineLease> leases;
    public string Owner { get; } = Guid.NewGuid().ToString("N");
    private long validUntilTicks;
    private int ready;
    public bool HasLease => DateTime.UtcNow.Ticks < Interlocked.Read(ref validUntilTicks);
    public OnlineRuntimeLease(IMongoDatabase db) => leases = db.GetCollection<OnlineLease>("online_leases");
    public void RequireOwner()
    { if (Volatile.Read(ref ready) == 0 || !HasLease) throw new OnlineException("OnlineTemporarilyUnavailable", 503); }
    public void MarkReady()
    {
        if (!HasLease) throw new OnlineException("OnlineLeaseLost", 503);
        Volatile.Write(ref ready, 1);
    }
    public void MarkUnavailable() => Volatile.Write(ref ready, 0);
    public async Task<bool> Acquire(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        try
        {
            var acquired = await leases.FindOneAndUpdateAsync(x => x.Id == "runtime" && (x.Owner == Owner || x.ExpiresAt <= now),
                Builders<OnlineLease>.Update.Set(x => x.Owner, Owner).Set(x => x.ExpiresAt, now.AddSeconds(15)),
                new FindOneAndUpdateOptions<OnlineLease> { IsUpsert = true, ReturnDocument = ReturnDocument.After }, ct);
            MarkUnavailable();
            Interlocked.Exchange(ref validUntilTicks, acquired.ExpiresAt.Ticks);
            return true;
        }
        catch (MongoCommandException ex) when (ex.Code == 11000) { return false; }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey) { return false; }
    }
    public async Task Renew(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var changed = await leases.UpdateOneAsync(x => x.Id == "runtime" && x.Owner == Owner && x.ExpiresAt > now,
            Builders<OnlineLease>.Update.Set(x => x.ExpiresAt, now.AddSeconds(15)), cancellationToken: ct);
        if (changed.ModifiedCount == 0)
        {
            MarkUnavailable();
            Interlocked.Exchange(ref validUntilTicks, 0);
            throw new OnlineException("OnlineLeaseLost", 503);
        }
        Interlocked.Exchange(ref validUntilTicks, now.AddSeconds(15).Ticks);
    }
    public async Task Release(CancellationToken ct)
    {
        MarkUnavailable();
        Interlocked.Exchange(ref validUntilTicks, 0);
        await leases.UpdateOneAsync(x => x.Id == "runtime" && x.Owner == Owner,
            Builders<OnlineLease>.Update.Set(x => x.ExpiresAt, DateTime.UtcNow), cancellationToken: ct);
    }
}
