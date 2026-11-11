using ChessGame.Api.Models;
using ChessGame.Api.Online;
using ChessGame.Api.Services.Ranks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;

// Always uses a dedicated local replica set and a fresh database. No appsettings,
// account credentials or production connections are loaded by this probe.
const string uri = "mongodb://127.0.0.1:28291/?directConnection=true&serverSelectionTimeoutMS=5000";
var client = new MongoClient(uri);
var admin = client.GetDatabase("admin");
try { await admin.RunCommandAsync<BsonDocument>(new BsonDocument("replSetGetStatus", 1)); }
catch (MongoCommandException e) when (e.Code == 94)
{
    await admin.RunCommandAsync<BsonDocument>(new BsonDocument("replSetInitiate", new BsonDocument {
        { "_id", "cbw-multiplayer-test" }, { "members", new BsonArray { new BsonDocument { { "_id", 0 }, { "host", "127.0.0.1:28291" } } } } }));
}
for (int attempt = 0; ; attempt++)
{
    var hello = await admin.RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1));
    if (hello["isWritablePrimary"].AsBoolean) break;
    if (attempt > 50) throw new Exception("Test replica set did not become primary.");
    await Task.Delay(200);
}
var databaseName = "cbw_multiplayer_test_" + Guid.NewGuid().ToString("N");
var db = client.GetDatabase(databaseName);
Console.WriteLine("Isolated database: " + databaseName);
var lease = new OnlineRuntimeLease(db);
var store = new OnlineStore(db, client, lease);
await store.EnsureIndexesAsync();
if (!await lease.Acquire(default)) throw new Exception("Cannot acquire test lease.");
lease.MarkReady();
using var heartbeatCancel = new CancellationTokenSource();
var heartbeat = Task.Run(async () => {
    try { while (true) { await Task.Delay(2000, heartbeatCancel.Token); await lease.Renew(heartbeatCancel.Token); } }
    catch (OperationCanceledException) { }
});
try
{
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    var rankConfig = new ConfigurationBuilder().SetBasePath(root).AddJsonFile("ranks.json").Build().GetSection("Ranks").Get<RankOptions>()!;
    var chaos = new ChaosEngine();
    var service = new OnlineService(store, new GameRules(chaos), chaos, Options.Create(new OnlineOptions()), lease, new RankService(Options.Create(rankConfig)));
    void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    async Task<string[]> Users()
    {
        var users = new[] { new User { Username = "white-fixture" }, new User { Username = "black-fixture" } };
        await store.Users.InsertManyAsync(users);
        foreach (var user in users) await service.Connected(user.Id.ToString(), Guid.NewGuid().ToString("N"), default);
        return users.Select(u => u.Id.ToString()).ToArray();
    }
    async Task<MatchSnapshot> Start(string[] ids, string mode, bool rated)
    {
        var settings = new GameSettings { Mode = mode };
        await service.Queue(ids[0], new QueueRequest { RequestId = Guid.NewGuid().ToString("N"), Settings = settings, Rated = rated }, default);
        var ticket = await service.Queue(ids[1], new QueueRequest { RequestId = Guid.NewGuid().ToString("N"), Settings = settings, Rated = rated }, default);
        Check(ticket.Status == "Matched", "Two compatible players must pair immediately.");
        var match = await service.State(ids[0], ticket.MatchId!, default);
        Check(match.Rated == rated && match.AcceptDeadline != null, "Queue rating/acceptance contract lost.");
        foreach (var id in ids) await service.AcceptMatch(id, match.MatchId, default);
        if (mode == "Chaos") foreach (var id in ids)
        {
            match = await service.State(id, match.MatchId, default);
            var team = match.Players.Single(p => p.UserId == id).Color;
            int buff = match.Chaos!.Sides.Single(s => s.Team == team).DraftOptions.First(id => id is not (0 or 2 or 3 or 13 or 23 or 27));
            await service.UseAbility(id, match.MatchId, Guid.NewGuid().ToString("N"), match.StateVersion, new AbilityCommand { Kind = "SelectBuff", BuffId = buff }, default);
        }
        foreach (var id in ids) await service.SetReady(id, match.MatchId, default);
        return await service.State(ids[0], match.MatchId, default);
    }
    {
        var ids = await Users();
        var boards = new[] { new Item { Code = "TEST_DENAS", Type = "BOARD_SKIN", UnityAssetKey = "kingdom_rush_denas", IsActive = true },
            new Item { Code = "TEST_VEZNAN", Type = "BOARD_SKIN", UnityAssetKey = "kingdom_rush_veznan", IsActive = true } };
        await store.Items.InsertManyAsync(boards);
        for (int i = 0; i < 2; i++)
        {
            var id = ObjectId.Parse(ids[i]);
            await store.PlayerItems.InsertOneAsync(new PlayerItem { UserId = id, ItemId = boards[i].Id });
            await store.Users.UpdateOneAsync(u => u.Id == id, Builders<User>.Update.Set(u => u.Equipped.BoardSkinId, boards[i].Id)
                .Set(u => u.Equipped.BackgroundSkinId, ObjectId.GenerateNewId()));
        }
        var room = await service.CreateRoom(ids[0], new CreateRoomRequest { RequestId = "shared-board" }, default);
        await service.JoinRoom(ids[1], room.Code, default);
        var match = await service.StartRoom(ids[0], room.RoomId, default);
        var chosen = match.SharedBoardAssetKey;
        Check(boards.Any(b => b.UnityAssetKey == chosen), "Shared board was not chosen from the two loadouts.");
        Check(match.Players.All(p => p.Loadout.All(l => l.Type != "BACKGROUND_SKIN")), "Multiplayer includes a personal background.");
        // A late equipment change cannot invalidate the models already being loaded by both clients.
        await store.Users.UpdateOneAsync(u => u.Id == ObjectId.Parse(ids[0]), Builders<User>.Update.Set(u => u.Equipped.BoardSkinId, (ObjectId?)null));
        foreach (var id in ids) await service.SetReady(id, match.MatchId, default);
        foreach (var id in ids)
        {
            var state = await service.State(id, match.MatchId, default);
            Check(state.Status == "InProgress" && state.SharedBoardAssetKey == chosen, "Ready/reconnect changed the shared board.");
        }
        Check(match.Players.Select(p => p.Color).Distinct().Count() == 2, "Random assignment duplicated a color.");
        Console.WriteLine("PASS server random colors/shared board, background exclusion, frozen loadout and reconnect persistence");
    }
    foreach (string mode in new[] { "Classic", "Chaos" })
    {
        var ids = await Users();
        var room = await service.CreateRoom(ids[0], new CreateRoomRequest { RequestId = "create", Settings = new GameSettings { Mode = mode } }, default);
        var joined = await service.JoinRoom(ids[1], " " + room.Code.ToLowerInvariant() + " ", default);
        Check(joined.Members.Count == 2, "Room join failed.");
        joined = await service.JoinRoom(ids[1], room.Code, default);
        Check(joined.Members.Count == 2, "Retry duplicated room membership.");
        var match = await service.StartRoom(ids[0], room.RoomId, default);
        Check(!match.Rated, "Custom rooms must be unrated.");
        await service.Decline(ids[0], match.MatchId, default);
        room = await service.CreateRoom(ids[0], new CreateRoomRequest { RequestId = "create-again", Settings = new GameSettings { Mode = mode } }, default);
        await service.JoinRoom(ids[1], room.Code, default);
        await service.LeaveRoom(ids[1], room.RoomId, default);
        await service.DeleteRoom(ids[0], room.RoomId, default);
        Console.WriteLine("PASS " + mode + " create/join/retry/start/release/rejoin room");
    }
    foreach (string staleKind in new[] { "Room", "Ticket", "Match", "SameRoom" })
    {
        var ids = await Users();
        var room = await service.CreateRoom(ids[0], new CreateRoomRequest { RequestId = "recovery" }, default);
        string reference = ObjectId.GenerateNewId().ToString();
        if (staleKind == "Room") await store.Rooms.InsertOneAsync(new OnlineRoom { Id = reference, Code = reference, Status = "Closed", CreatorId = ids[1], RequestId = "stale" });
        if (staleKind == "Ticket") await store.Tickets.InsertOneAsync(new OnlineTicket { Id = reference, UserId = ids[1], Status = "Cancelled", RequestId = "stale" });
        if (staleKind == "Match") await store.Matches.InsertOneAsync(new OnlineMatch { Id = reference, Status = "Finished" });
        await store.Seats.InsertOneAsync(new OnlineSeat { UserId = ids[1], Kind = staleKind == "SameRoom" ? "Room" : staleKind, ReferenceId = staleKind == "SameRoom" ? room.RoomId : reference });
        var attempts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => service.JoinRoom(ids[1], room.Code, default)));
        Check(attempts.All(r => r.Members.Count == 2), "Concurrent join was not idempotent.");
        Check(await store.Seats.CountDocumentsAsync(s => s.ReferenceId == room.RoomId) == 2, "Room has invalid seat count after recovery.");
        var extra = await Users();
        try { await service.JoinRoom(extra[0], room.Code, default); throw new Exception("A third player joined a full room."); }
        catch (OnlineException error) when (error.Code == "RoomFull" && error.Status == 409) { }
        Console.WriteLine("PASS concurrent join, full-room rejection and stale " + staleKind + " reservation recovery");
    }
    foreach (string mode in new[] { "Classic", "Chaos" }) foreach (bool rated in new[] { false, true })
    {
        var ids = await Users();
        var match = await Start(ids, mode, rated);
        Check(match.Status == "InProgress", "Match did not start.");
        // Durable participation makes the rating assertions meaningful, rather
        // than relying on the separate no-participation exclusion.
        await store.Moves.InsertManyAsync(ids.Select((id, i) => new OnlineMove { MatchId = match.MatchId, UserId = id, Kind = "Move", Sequence = 100 + i, CommandId = "participation-" + i }));
        var before = await store.Users.Find(u => u.Id == ObjectId.Parse(ids[0])).FirstAsync();
        var ack = await service.Resign(ids[0], match.MatchId, "resign", default);
        Check(ack.Accepted, "Resignation rejected.");
        match = await service.State(ids[0], match.MatchId, default);
        var after = await store.Users.Find(u => u.Id == before.Id).FirstAsync();
        Check(match.Result!.RatingApplied == rated, "Settlement applied the wrong rating policy.");
        Check(after.Ratings.For(mode).RatedGames == before.Ratings.For(mode).RatedGames + (rated ? 1 : 0), "Rated game count changed incorrectly.");
        if (!rated) Check(OnlineJson.Write(before.Ratings) == OnlineJson.Write(after.Ratings), "Normal play changed rank state.");
        var next = await service.Queue(ids[0], new QueueRequest { RequestId = "next", Settings = new GameSettings { Mode = mode }, Rated = rated }, default);
        Check(next.Status == "Queued", "Finished match retained a player seat.");
        await service.CancelTicket(ids[0], next.TicketId, default);
        Console.WriteLine($"PASS {mode} {(rated ? "ranked" : "normal")} pairing/start/settlement/next queue");
    }
    // Run the production outbox loop against the isolated database, with only
    // SignalR delivery replaced by a recorder. No player socket is required.
    await store.Events.UpdateManyAsync(Builders<OnlineEvent>.Filter.Empty, Builders<OnlineEvent>.Update.Set(e => e.Published, true));
    var eventUsers = await Users();
    var delivery = new RecordingHub();
    var worker = new OnlineWorker(store, service, lease, new LiveConnections(), delivery, new ProbeLifetime(), NullLogger<OnlineWorker>.Instance);
    using var publishCancel = new CancellationTokenSource();
    var publishing = (Task)typeof(OnlineWorker).GetMethod("PublishLoop", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(worker, new object[] { publishCancel.Token })!;
    try
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        await service.CreateRoom(eventUsers[0], new CreateRoomRequest { RequestId = "delivery" }, default);
        Check(await Task.WhenAny(delivery.RoomUpdated.Task, Task.Delay(3000)) == delivery.RoomUpdated.Task, "Room event delivery did not wake after commit.");
        Console.WriteLine($"PASS durable RoomUpdated delivery after commit ({timer.ElapsedMilliseconds} ms)");
    }
    finally { publishCancel.Cancel(); await publishing; }
}
finally
{
    heartbeatCancel.Cancel(); await heartbeat;
    await lease.Release(default);
    // Retain the fresh fixture database for inspection; the owned server is
    // stopped by the invoking verification script after the probe exits.
}

sealed class RecordingHub : IHubContext<GameHub>, IHubClients, IClientProxy
{
    public readonly TaskCompletionSource<bool> RoomUpdated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    IHubClients IHubContext<GameHub>.Clients => this;
    IGroupManager IHubContext<GameHub>.Groups => null!;
    public IClientProxy All => this;
    public IClientProxy AllExcept(IReadOnlyList<string> ids) => this;
    public IClientProxy Client(string id) => this;
    public IClientProxy Clients(IReadOnlyList<string> ids) => this;
    public IClientProxy Group(string id) => this;
    public IClientProxy GroupExcept(string id, IReadOnlyList<string> except) => this;
    public IClientProxy Groups(IReadOnlyList<string> ids) => this;
    public IClientProxy User(string id) => this;
    public IClientProxy Users(IReadOnlyList<string> ids) => this;
    public Task SendCoreAsync(string method, object?[] args, CancellationToken ct = default)
    { if (method == "RoomUpdated") RoomUpdated.TrySetResult(true); return Task.CompletedTask; }
}
sealed class ProbeLifetime : IHostApplicationLifetime
{
    public CancellationToken ApplicationStarted => default;
    public CancellationToken ApplicationStopping => default;
    public CancellationToken ApplicationStopped => default;
    public void StopApplication() => throw new Exception("The test worker unexpectedly stopped the host.");
}
