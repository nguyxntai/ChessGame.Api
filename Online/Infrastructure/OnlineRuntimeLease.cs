using MongoDB.Driver;

namespace ChessGame.Api.Online;

/// <summary>Fail closed if another process owns the online runtime. A backplane is needed before horizontal scaling.</summary>
public sealed class OnlineRuntimeLease
{
    private readonly IMongoCollection<OnlineLease> leases;
    public string Owner { get; } = Guid.NewGuid().ToString("N");
    private DateTime validUntil;
    public OnlineRuntimeLease(IMongoDatabase db) => leases = db.GetCollection<OnlineLease>("online_leases");
    public void RequireOwner()
    { if (DateTime.UtcNow >= validUntil) throw new OnlineException("OnlineTemporarilyUnavailable", 503); }
    public async Task<bool> Acquire(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        try
        {
            var acquired = await leases.FindOneAndUpdateAsync(x => x.Id == "runtime" && (x.Owner == Owner || x.ExpiresAt <= now),
                Builders<OnlineLease>.Update.Set(x => x.Owner, Owner).Set(x => x.ExpiresAt, now.AddSeconds(15)),
                new FindOneAndUpdateOptions<OnlineLease> { IsUpsert = true, ReturnDocument = ReturnDocument.After }, ct);
            validUntil = acquired.ExpiresAt; return true;
        }
        catch (MongoCommandException ex) when (ex.Code == 11000) { return false; }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey) { return false; }
    }
    public async Task Renew(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var changed = await leases.UpdateOneAsync(x => x.Id == "runtime" && x.Owner == Owner && x.ExpiresAt > now,
            Builders<OnlineLease>.Update.Set(x => x.ExpiresAt, now.AddSeconds(15)), cancellationToken: ct);
        if (changed.ModifiedCount == 0) { validUntil = DateTime.MinValue; throw new OnlineException("OnlineLeaseLost", 503); }
        validUntil = now.AddSeconds(15);
    }
    public async Task Release(CancellationToken ct)
    {
        validUntil = DateTime.MinValue;
        await leases.UpdateOneAsync(x => x.Id == "runtime" && x.Owner == Owner,
            Builders<OnlineLease>.Update.Set(x => x.ExpiresAt, DateTime.UtcNow), cancellationToken: ct);
    }
}
