using ChessGame.Api.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ChessGame.Api.Online;

public sealed class OnlineStore
{
    public IMongoCollection<OnlineMatch> Matches { get; }
    public IMongoCollection<OnlineTicket> Tickets { get; }
    public IMongoCollection<OnlineSeat> Seats { get; }
    public IMongoCollection<OnlineRoom> Rooms { get; }
    public IMongoCollection<OnlineCommand> Commands { get; }
    public IMongoCollection<OnlineMove> Moves { get; }
    public IMongoCollection<OnlineEvent> Events { get; }
    public IMongoCollection<OnlineConnection> Connections { get; }
    public IMongoCollection<OnlineLease> Leases { get; }
    public IMongoCollection<User> Users { get; }
    public IMongoCollection<Item> Items { get; }
    public IMongoCollection<PlayerItem> PlayerItems { get; }
    public IMongoCollection<CurrencyTransaction> Ledger { get; }
    private readonly IMongoCollection<BsonDocument> coordinator;
    private readonly IMongoClient client;
    private readonly OnlineRuntimeLease runtimeLease;

    public OnlineStore(IMongoDatabase db, IMongoClient client, OnlineRuntimeLease runtimeLease)
    {
        this.client = client;
        this.runtimeLease = runtimeLease;
        Matches = db.GetCollection<OnlineMatch>("online_matches");
        Tickets = db.GetCollection<OnlineTicket>("online_tickets");
        Seats = db.GetCollection<OnlineSeat>("online_player_seats");
        Rooms = db.GetCollection<OnlineRoom>("online_rooms");
        Commands = db.GetCollection<OnlineCommand>("online_commands");
        Moves = db.GetCollection<OnlineMove>("online_moves");
        Events = db.GetCollection<OnlineEvent>("online_events");
        Connections = db.GetCollection<OnlineConnection>("online_connections");
        Leases = db.GetCollection<OnlineLease>("online_leases");
        Users = db.GetCollection<User>("users"); Items = db.GetCollection<Item>("items");
        PlayerItems = db.GetCollection<PlayerItem>("player_items");
        Ledger = db.GetCollection<CurrencyTransaction>("currency_transactions");
        coordinator = db.GetCollection<BsonDocument>("online_coordinator");
    }
    public async Task<T> Transaction<T>(Func<IClientSessionHandle, CancellationToken, Task<T>> action, CancellationToken ct = default)
    {
        using var session = await client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync(async (s, token) =>
        {
            // Every online mutation first writes the same durable guard. MongoDB retries write conflicts;
            // reads after the guard share the transaction snapshot, including pairing/cancel/start races.
            await coordinator.UpdateOneAsync(s, new BsonDocument("_id", "serialization"),
                new BsonDocument("$inc", new BsonDocument("revision", 1L)), cancellationToken: token);
            // Fence stale processes at the database too, not just at the HTTP/hub boundary.
            var now = DateTime.UtcNow;
            var fence = await Leases.UpdateOneAsync(s, x => x.Id == "runtime" && x.Owner == runtimeLease.Owner && x.ExpiresAt > now,
                Builders<OnlineLease>.Update.Inc(x => x.Revision, 1), cancellationToken: token);
            if (fence.ModifiedCount != 1)
                throw new OnlineException("OnlineTemporarilyUnavailable", 503);
            return await action(s, token);
        }, new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), ct);
    }
    public async Task EnsureIndexesAsync()
    {
        await coordinator.UpdateOneAsync(new BsonDocument("_id", "serialization"),
            new BsonDocument("$setOnInsert", new BsonDocument("revision", 0L)), new UpdateOptions { IsUpsert = true });
        await Tickets.Indexes.CreateManyAsync(new[] {
            new CreateIndexModel<OnlineTicket>(Builders<OnlineTicket>.IndexKeys.Ascending(x => x.UserId).Ascending(x => x.RequestId), new CreateIndexOptions { Unique = true }),
            new CreateIndexModel<OnlineTicket>(Builders<OnlineTicket>.IndexKeys.Ascending(x => x.Status).Ascending(x => x.CreatedAt)) });
        await Rooms.Indexes.CreateManyAsync(new[] {
            new CreateIndexModel<OnlineRoom>(Builders<OnlineRoom>.IndexKeys.Ascending(x => x.Code), new CreateIndexOptions { Unique = true }),
            new CreateIndexModel<OnlineRoom>(Builders<OnlineRoom>.IndexKeys.Ascending(x => x.CreatorId).Ascending(x => x.RequestId), new CreateIndexOptions { Unique = true }) });
        await Matches.Indexes.CreateManyAsync(new[] {
            new CreateIndexModel<OnlineMatch>(Builders<OnlineMatch>.IndexKeys.Ascending("Players.UserId").Descending(x => x.CreatedAt)),
            new CreateIndexModel<OnlineMatch>(Builders<OnlineMatch>.IndexKeys.Ascending(x => x.Status)) });
        await Commands.Indexes.CreateOneAsync(new CreateIndexModel<OnlineCommand>(Builders<OnlineCommand>.IndexKeys
            .Ascending(x => x.MatchId).Ascending(x => x.UserId).Ascending(x => x.CommandId), new CreateIndexOptions { Unique = true }));
        await Moves.Indexes.CreateOneAsync(new CreateIndexModel<OnlineMove>(Builders<OnlineMove>.IndexKeys
            .Ascending(x => x.MatchId).Ascending(x => x.Sequence), new CreateIndexOptions { Unique = true }));
        await Events.Indexes.CreateManyAsync(new[] {
            new CreateIndexModel<OnlineEvent>(Builders<OnlineEvent>.IndexKeys.Ascending(x => x.Published).Ascending(x => x.CreatedAt)),
            new CreateIndexModel<OnlineEvent>(Builders<OnlineEvent>.IndexKeys.Ascending(x => x.MatchId).Ascending(x => x.Sequence)) });
        await Connections.Indexes.CreateOneAsync(new CreateIndexModel<OnlineConnection>(Builders<OnlineConnection>.IndexKeys.Ascending(x => x.UserId)));
        await Ledger.Indexes.CreateOneAsync(new CreateIndexModel<CurrencyTransaction>(Builders<CurrencyTransaction>.IndexKeys
            .Ascending(x => x.UserId).Ascending(x => x.RequestId).Ascending(x => x.EntryIndex),
            new CreateIndexOptions<CurrencyTransaction> { Unique = true, Name = "online_reward_once",
                PartialFilterExpression = Builders<CurrencyTransaction>.Filter.Eq(x => x.Source, "ONLINE_MATCH") }));
    }
}
