using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ChessGame.Api.Controllers;
using ChessGame.Api.DTOs.Gacha;
using ChessGame.Api.Models;
using ChessGame.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace ChessGame.Api.Tests;

// Actual production controller/service in TestServer; all driver interfaces are strict fakes.
// These tests certify HTTP/orchestration only. Real MongoDB tests live in PhaseB3Fix1/Lab.
public sealed class GachaUnknownHttpTests
{
    private static readonly Guid Original = Guid.Parse("aabbccdd-1122-3344-5566-778899aabbcc");
    private static readonly ObjectId Other = ObjectId.Parse("222222222222222222222222");
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static MongoCommandException Unknown()
    {
        var e = new MongoCommandException(new MongoDB.Driver.Core.Connections.ConnectionId(
            new MongoDB.Driver.Core.Servers.ServerId(new MongoDB.Driver.Core.Clusters.ClusterId(), new System.Net.DnsEndPoint("synthetic.invalid", 1))),
            "PRIVATE_DATABASE_MUST_NOT_LEAK", new BsonDocument("commitTransaction", 1),
            new BsonDocument { { "ok", 0 }, { "code", 91 }, { "errmsg", "PRIVATE_DATABASE_MUST_NOT_LEAK" },
                { "errorLabels", new BsonArray { "UnknownTransactionCommitResult" } } });
        return e;
    }

    [Theory][InlineData(1)][InlineData(10)]
    public async Task UnknownIsExactCanonicalSanitizedNonCacheable_NoPostOrBodyRetry(int count)
    {
        await using var f = await UnknownHttpFixture.Create(); f.Store.CommitError = Unknown();
        f.Http.DefaultRequestHeaders.Add("X-UserId", Other.ToString());
        var response = await f.Http.PostAsJsonAsync("/api/gacha/roll?userId=" + Other,
            new { bannerCode = " test ", count, requestId = Original.ToString("D").ToUpperInvariant(), userId = Other.ToString() });
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.True(response.Headers.CacheControl!.NoStore); Assert.True(response.Headers.CacheControl.NoCache);
        Assert.Contains("no-cache", response.Headers.Pragma.Select(x => x.Name)); Assert.Null(response.Headers.RetryAfter);
        using var doc = JsonDocument.Parse(body); var json = doc.RootElement;
        Assert.Equal(new[] { "code", "outcome", "recovery", "requestId" }, json.EnumerateObject().Select(x => x.Name).OrderBy(x => x));
        Assert.Equal("COMMIT_OUTCOME_UNKNOWN", json.GetProperty("code").GetString());
        Assert.Equal("UNKNOWN", json.GetProperty("outcome").GetString());
        Assert.Equal(Original.ToString("D"), json.GetProperty("requestId").GetString());
        Assert.Equal("RECOVER_ORIGINAL_REQUEST_ID", json.GetProperty("recovery").GetString());
        Assert.DoesNotContain(f.Store.User.Id.ToString(), body); Assert.DoesNotContain(Other.ToString(), body);
        foreach (var forbidden in new[] { "PRIVATE_DATABASE", "synthetic.invalid", "stackTrace", "innerException", "session", "txnNumber", "results", "wallet" })
            Assert.DoesNotContain(forbidden, body);
        Assert.Equal(1, f.Posts); Assert.Equal(1, f.Store.Calls.Count(x => x == "session"));
        Assert.Equal(1, f.Store.Calls.Count(x => x == "begin")); Assert.Equal(1, f.Store.Calls.Count(x => x == "roll.insert"));
        Assert.Equal(8, f.Store.Calls.Count(x => x == "commit")); Assert.DoesNotContain("abort", f.Store.Calls);
        Assert.Equal(1, f.Store.Calls.Count(x => x == "dispose"));
        Assert.Equal(Original.ToString("D"), f.Store.Candidate!.RequestId);
        Assert.Empty(f.Log.Messages);
        var before = f.Store.Calls.Count(x => x == "begin");
        for (var i = 0; i < 2; i++)
        {
            var recovery = await f.Http.GetAsync("/api/gacha/rolls/by-request/" + Original);
            Assert.Equal(HttpStatusCode.NotFound, recovery.StatusCode);
            Assert.Equal("{\"code\":\"NOT_OBSERVED\"}", await recovery.Content.ReadAsStringAsync());
        }
        Assert.Equal(before, f.Store.Calls.Count(x => x == "begin")); Assert.Equal(1, f.Posts);
        Save("unknown-x" + count + ".json", new { status = 503, body, originalRequestId = Original, postInvocations = f.Posts, calls = f.Store.Calls });
    }

    [Theory][InlineData("account")][InlineData("request")][InlineData("noncanonical")][InlineData("banner")][InlineData("count")]
    public async Task ForgedExceptionCorrelationFailsClosedAndLogsNoIdentifiers(string mismatch)
    {
        await using var f = await UnknownHttpFixture.Create(); f.Store.CommitError = Unknown();
        f.Store.MutateCandidate = candidate =>
        {
            switch (mismatch)
            {
                case "account": candidate.UserId = Other; break;
                case "request": candidate.RequestId = "11111111-2222-3333-4444-555555555555"; break;
                case "noncanonical": candidate.RequestId = Original.ToString("N"); break;
                case "banner": candidate.BannerCode = "OTHER"; break;
                case "count": candidate.Count = 10; break;
            }
        };
        var response = await f.Http.PostAsJsonAsync("/api/gacha/roll", Request());
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("{\"code\":\"GACHA_OUTCOME_UNAVAILABLE\"}", body);
        Assert.True(response.Headers.CacheControl!.NoStore); Assert.Null(response.Headers.RetryAfter);
        Assert.Equal(new[] { "3201:Gacha UNKNOWN outcome correlation validation failed." }, f.Log.Messages);
        Assert.Equal(1, f.Posts); Assert.Equal(1, f.Store.Calls.Count(x => x == "begin"));
        Assert.DoesNotContain("abort", f.Store.Calls);
    }

    [Theory][InlineData(1, false)][InlineData(10, false)][InlineData(1, true)][InlineData(10, true)]
    public async Task AcknowledgedOrReconciledCommitKeepsOriginal200Schema(int count, bool reconciliation)
    {
        await using var f = await UnknownHttpFixture.Create();
        if (reconciliation) { f.Store.CommitError = Unknown(); f.Store.ReconcileCommitted = true; }
        var response = await f.Http.PostAsJsonAsync("/api/gacha/roll", Request(count));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var actual = JsonSerializer.Deserialize<GachaRollResponse>(json, WebJson)!;
        Assert.Equal(Original.ToString("D"), actual.RequestId); Assert.Equal(count, actual.Results.Count);
        Assert.Equal(count, actual.PresentationSnapshot!.Rewards.Count);
        Assert.Equal(new[] { "bannerCode", "count", "createdAt", "currency", "pity", "presentationSnapshot", "requestId", "results", "totalCost", "wallet" },
            JsonDocument.Parse(json).RootElement.EnumerateObject().Select(x => x.Name).OrderBy(x => x));
        Assert.Single(f.Store.Calls, x => x == "begin"); Assert.Single(f.Store.Calls, x => x == "commit");
        var before = f.Store.Stored!.ToBson();
        var recovery = await f.Http.GetAsync("/api/gacha/rolls/by-request/" + Original);
        Assert.Equal(HttpStatusCode.OK, recovery.StatusCode);
        var recovered = await recovery.Content.ReadFromJsonAsync<GachaRollResponse>();
        Assert.Equal(actual.Results.Select(x => x.ItemId), recovered!.Results.Select(x => x.ItemId));
        Assert.Equal(before, f.Store.Stored.ToBson()); Assert.Equal(1, f.Posts);
        Save($"committed-x{count}-{reconciliation}.json", new { status = 200, json, reconciliation, postInvocations = f.Posts });
    }

    [Theory]
    [InlineData("count", 400, "INVALID_ROLL_REQUEST")][InlineData("guid", 400, "INVALID_ROLL_REQUEST")]
    [InlineData("banner", 400, "FRAMEWORK_VALIDATION")][InlineData("missing", 404, "BANNER_NOT_FOUND")]
    [InlineData("disabled", 403, "ACCOUNT_DISABLED")][InlineData("currency", 409, "INSUFFICIENT_CURRENCY")]
    [InlineData("pool", 503, "BANNER_MISCONFIGURED")]
    public async Task StandardErrorsKeepTheirExistingMapping(string scenario, int status, string code)
    {
        await using var f = await UnknownHttpFixture.Create(); var request = Request();
        switch (scenario)
        {
            case "count": request.Count = 2; break;
            case "guid": request.RequestId = Guid.Empty; break;
            case "banner": request.BannerCode = " "; break;
            case "missing": f.Store.BannerMissing = true; break;
            case "disabled": f.Store.User.IsActive = false; break;
            case "currency": f.Store.User.Wallet.Tickets = 0; break;
            case "pool": f.Store.Items.Clear(); break;
        }
        var response = await f.Http.PostAsJsonAsync("/api/gacha/roll", request);
        Assert.Equal(status, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        if (scenario == "banner") {
            using var validation = JsonDocument.Parse(body);
            Assert.True(validation.RootElement.GetProperty("errors").TryGetProperty("BannerCode", out _));
            Assert.DoesNotContain("COMMIT_OUTCOME_UNKNOWN", body); Assert.Empty(f.Store.Calls);
        } else Assert.Equal(JsonSerializer.Serialize(new { code }), body);
        Assert.DoesNotContain("commit", f.Store.Calls);
    }

    [Theory][InlineData("anonymous")][InlineData("bad-signature")][InlineData("expired")][InlineData("no-sub")][InlineData("invalid-sub")]
    public async Task InvalidOrAnonymousJwtNeverReachesTheService(string scenario)
    {
        await using var f = await UnknownHttpFixture.Create(); f.Auth(scenario);
        var r = await f.Http.PostAsJsonAsync("/api/gacha/roll", Request());
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode); Assert.Empty(f.Store.Calls);
    }

    [Theory][InlineData("banner")][InlineData("count")]
    public async Task ExistingReceiptPayloadConflictDoesNotStartANewTransaction(string kind)
    {
        await using var f = await UnknownHttpFixture.Create();
        Assert.Equal(HttpStatusCode.OK, (await f.Http.PostAsJsonAsync("/api/gacha/roll", Request())).StatusCode);
        var request = Request(kind == "count" ? 10 : 1); if (kind == "banner") request.BannerCode = "OTHER";
        var r = await f.Http.PostAsJsonAsync("/api/gacha/roll", request);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); Assert.Equal("{\"code\":\"REQUEST_ID_CONFLICT\"}", await r.Content.ReadAsStringAsync());
        Assert.Single(f.Store.Calls, x => x == "begin");
        r = await f.Http.GetAsync($"/api/gacha/rolls/by-request/{Original}?bannerCode={request.BannerCode}&count={request.Count}");
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); Assert.Single(f.Store.Calls, x => x == "begin");
    }

    [Fact]
    public async Task OtherAccountCannotReadOriginalReceipt_AndRecoveryOutageIsNotAbsence()
    {
        await using var f = await UnknownHttpFixture.Create();
        Assert.Equal(HttpStatusCode.OK, (await f.Http.PostAsJsonAsync("/api/gacha/roll", Request())).StatusCode);
        var bytes = f.Store.Stored!.ToBson(); f.Auth("other");
        var r = await f.Http.GetAsync("/api/gacha/rolls/by-request/" + Original);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode); Assert.Equal("{\"code\":\"NOT_OBSERVED\"}", await r.Content.ReadAsStringAsync());
        f.Auth("owner"); f.Store.RecoveryReadUnavailable = true;
        r = await f.Http.GetAsync("/api/gacha/rolls/by-request/" + Original);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode); Assert.Equal("{\"code\":\"RECOVERY_UNAVAILABLE\"}", await r.Content.ReadAsStringAsync());
        Assert.Equal(bytes, f.Store.Stored.ToBson()); Assert.Single(f.Store.Calls, x => x == "begin");
    }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task UnrelatedExceptionsAreNotReclassifiedAsCommitUnknown(bool afterDispatch)
    {
        await using var f = await UnknownHttpFixture.Create();
        if (afterDispatch) f.Store.CommitError = new InvalidOperationException("synthetic unrelated");
        else f.Store.RollReadError = new MongoException("synthetic pre-commit");
        var exception = await Record.ExceptionAsync(() => f.Http.PostAsJsonAsync("/api/gacha/roll", Request()));
        Assert.NotNull(exception); Assert.NotEqual("CommitOutcomeUnknownException", exception.GetType().Name);
    }

    private static GachaRollRequest Request(int count = 1) => new() { BannerCode = " test ", Count = count, RequestId = Original };
    private static void Save(string name, object data)
    {
        var dir = Environment.GetEnvironmentVariable("B3FIX1_HTTP_EVIDENCE");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, name), JsonSerializer.Serialize(data, WebJson));
    }
}

internal sealed class UnknownHttpFixture : IAsyncDisposable
{
    private const string Key = "B3FIX1_TEST_ONLY_SYNTHETIC_SIGNING_KEY_0123456789_ABCDEFGHIJKLMNOP";
    internal readonly UnknownHttpStore Store = new();
    internal readonly UnknownControllerLog Log = new();
    internal HttpClient Http = null!;
    internal int Posts;
    private WebApplication app = null!;
    internal static async Task<UnknownHttpFixture> Create()
    {
        var f = new UnknownHttpFixture(); f.Store.User.Id = ObjectId.Parse("111111111111111111111111");
        var b = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "B3Fix1Offline" });
        b.WebHost.UseTestServer(); b.Logging.ClearProviders(); b.Logging.AddProvider(f.Log);
        b.Services.AddControllers().AddApplicationPart(typeof(GachaController).Assembly); b.Services.AddSingleton(f.Store.Service);
        b.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters {
            ValidateIssuer = true, ValidIssuer = "b3fix1-test", ValidateAudience = true, ValidAudience = "b3fix1-client",
            ValidateLifetime = true, ClockSkew = TimeSpan.Zero, ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)) });
        b.Services.AddAuthorization(); f.app = b.Build();
        f.app.Use(async (context, next) => { if (context.Request.Method == "POST") f.Posts++; await next(); });
        f.app.UseAuthentication(); f.app.UseAuthorization(); f.app.MapControllers(); await f.app.StartAsync();
        f.Http = f.app.GetTestClient(); f.Auth("owner"); return f;
    }
    internal void Auth(string scenario)
    {
        if (scenario == "anonymous") { Http.DefaultRequestHeaders.Authorization = null; return; }
        var subject = scenario == "other" ? "222222222222222222222222" : scenario == "invalid-sub" ? "bad" : Store.User.Id.ToString();
        var jwt = new JwtSecurityToken("b3fix1-test", "b3fix1-client", scenario == "no-sub" ? Array.Empty<Claim>() : new[] { new Claim("sub", subject) },
            expires: DateTime.UtcNow.AddMinutes(scenario == "expired" ? -5 : 5), signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(scenario == "bad-signature" ? Key + "WRONG" : Key)), SecurityAlgorithms.HmacSha256));
        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
    }
    public async ValueTask DisposeAsync() { Http.Dispose(); await app.DisposeAsync(); }
}
internal sealed class UnknownControllerLog : ILoggerProvider, ILogger
{
    internal readonly List<string> Messages = new();
    public ILogger CreateLogger(string category) => category == typeof(GachaController).FullName ? this : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    public void Dispose() { }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => true;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    { Assert.Null(exception); Messages.Add(id.Id + ":" + formatter(state, exception)); }
}

internal sealed class UnknownHttpStore
{
    internal const long Bonus = 5_000_000_000;
    internal readonly List<string> Calls = new();
    internal readonly User User = new() { Wallet = new PlayerWallet { Golds = 0, Tickets = 100 } };
    internal readonly Item Legendary = new() { Id = ObjectId.GenerateNewId(), Code = "mock-legend", Name = "Mock", Rarity = "LEGENDARY", Type = "RAW:unapproved", UnityAssetKey = "same/key", IsActive = true };
    internal readonly List<Item> Items;
    internal GachaRoll? Stored;
    internal List<CurrencyTransaction> Ledger = new();
    internal BsonDocument? LastRollFilter;
    internal readonly GachaService Service;
    private GachaRoll? pending;
    internal readonly User OtherUser = new() { Id = ObjectId.Parse("222222222222222222222222"), IsActive = true };
    internal GachaRoll? Candidate;
    internal Exception? CommitError;
    internal Exception? RollReadError;
    internal bool ReconcileCommitted;
    internal bool RecoveryReadUnavailable;
    internal bool BannerMissing;
    internal Action<GachaRoll>? MutateCandidate;


    internal GachaRollRequest Request(int count) => new() { BannerCode = " test ", Count = count, RequestId = Guid.NewGuid() };

    internal UnknownHttpStore(bool alreadyOwned = false)
    {
        Items = new() { Legendary, new Item { Id = ObjectId.GenerateNewId(), Code = "mock-epic", Rarity = "EPIC", IsActive = true } };
        var banner = new GachaBanner
        {
            Code = "TEST", IsActive = true, PityGroup = "TEST", Pity = new GachaPityLimits { Epic = 1, Legendary = 1 },
            Pool = Items.Select(i => new GachaPoolEntry { ItemId = i.Id, Weight = 1 }).ToList(),
            Costs = new() { new GachaCost { RollCount = 1, Currency = "TICKETS", Amount = 1 }, new GachaCost { RollCount = 10, Currency = "TICKETS", Amount = 10 } },
            DuplicateRewards = new[] { "COMMON", "RARE", "EPIC", "LEGENDARY" }.ToDictionary(r => r, r => new GachaDuplicateReward { Currency = "GOLDS", Amount = Bonus })
        };
        var db = new Mock<IMongoDatabase>(MockBehavior.Strict);
        var client = new Mock<IMongoClient>(MockBehavior.Strict);
        var session = new Mock<IClientSessionHandle>(MockBehavior.Strict);
        bool active = false, owned = alreadyOwned;
        session.Setup(s => s.StartTransaction(It.IsAny<TransactionOptions>())).Callback(() => { active = true; Calls.Add("begin"); });
        session.SetupGet(s => s.IsInTransaction).Returns(() => active);
        session.Setup(s => s.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns(() => {
            Calls.Add("commit"); active = false;
            if (CommitError is not null) { if (ReconcileCommitted) Stored = pending; return Task.FromException(CommitError); }
            Stored = pending; return Task.CompletedTask;
        });
        session.Setup(s => s.AbortTransactionAsync(It.IsAny<CancellationToken>())).Returns(() => { Calls.Add("abort"); active = false; return Task.CompletedTask; });
        session.Setup(s => s.Dispose()).Callback(() => Calls.Add("dispose"));
        client.Setup(c => c.StartSessionAsync(It.IsAny<ClientSessionOptions>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Calls.Add("session"); return Task.FromResult(session.Object); });
        var rolls = Collection<GachaRoll>(db, "gacha_rolls");
        var readView = new Mock<IMongoCollection<GachaRoll>>(MockBehavior.Strict);
        rolls.Setup(c => c.WithReadConcern(It.Is<ReadConcern>(r => r.Level == ReadConcernLevel.Majority))).Returns(readView.Object);
        readView.Setup(c => c.WithReadPreference(It.Is<ReadPreference>(r => r.ReadPreferenceMode == ReadPreferenceMode.Primary))).Returns(readView.Object);
        readView.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<GachaRoll>>(), It.IsAny<FindOptions<GachaRoll, GachaRoll>>(), It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<GachaRoll> filter, FindOptions<GachaRoll, GachaRoll> _, CancellationToken token) => {
                Calls.Add("majority.primary.roll.find"); token.ThrowIfCancellationRequested();
                if (RecoveryReadUnavailable) throw new MongoException("PRIVATE_DATABASE_MUST_NOT_LEAK");
                var f = Render(filter);
                var match = Stored is not null && f["userId"].AsObjectId == Stored.UserId && f["requestId"].AsString == Stored.RequestId;
                return Task.FromResult(Cursor(match ? new[] { Stored! } : Array.Empty<GachaRoll>()));
            });
        var users = Collection<User>(db, "users");
        var items = Collection<Item>(db, "items");
        var banners = Collection<GachaBanner>(db, "gacha_banners");
        var pity = Collection<GachaPity>(db, "player_gacha_pity");
        var inventory = Collection<PlayerItem>(db, "player_items");
        var ledger = Collection<CurrencyTransaction>(db, "currency_transactions");
        rolls.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<GachaRoll>>(), It.IsAny<FindOptions<GachaRoll, GachaRoll>>(), It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<GachaRoll> filter, FindOptions<GachaRoll, GachaRoll> _, CancellationToken _) =>
            { Calls.Add("roll.find"); if (RollReadError is not null) throw RollReadError; LastRollFilter = Render(filter);
                var matches = Stored is not null && LastRollFilter["userId"].AsObjectId == Stored.UserId
                    && (!LastRollFilter.TryGetValue("requestId", out var requestId) || requestId.AsString == Stored.RequestId);
                return Task.FromResult(Cursor(matches ? new[] { Stored! } : Array.Empty<GachaRoll>())); });
        rolls.Setup(c => c.FindAsync(session.Object, It.IsAny<FilterDefinition<GachaRoll>>(), It.IsAny<FindOptions<GachaRoll, GachaRoll>>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Calls.Add("roll.find.session"); return Task.FromResult(Cursor(Array.Empty<GachaRoll>())); });
        rolls.Setup(c => c.CountDocumentsAsync(It.IsAny<FilterDefinition<GachaRoll>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Calls.Add("roll.count"); return Task.FromResult(Stored is null ? 0L : 1L); });
        rolls.Setup(c => c.InsertOneAsync(session.Object, It.IsAny<GachaRoll>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Returns((IClientSessionHandle _, GachaRoll roll, InsertOneOptions _, CancellationToken _) =>
            { Calls.Add("roll.insert"); Candidate = roll; MutateCandidate?.Invoke(roll); pending = BsonSerializer.Deserialize<GachaRoll>(roll.ToBson()); return Task.CompletedTask; });
        users.Setup(c => c.FindAsync(session.Object, It.IsAny<FilterDefinition<User>>(), It.IsAny<FindOptions<User, User>>(), It.IsAny<CancellationToken>()))
            .Returns((IClientSessionHandle _, FilterDefinition<User> filter, FindOptions<User, User> _, CancellationToken _) =>
            { Calls.Add("user.find.session"); return Task.FromResult(Cursor(Render(filter)["_id"].AsObjectId == User.Id ? new[] { User } : Render(filter)["_id"].AsObjectId == OtherUser.Id ? new[] { OtherUser } : Array.Empty<User>())); });
        users.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<User>>(), It.IsAny<FindOptions<User, User>>(), It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<User> filter, FindOptions<User, User> _, CancellationToken _) =>
            { Calls.Add("user.find"); return Task.FromResult(Cursor(Render(filter)["_id"].AsObjectId == User.Id ? new[] { User } : Render(filter)["_id"].AsObjectId == OtherUser.Id ? new[] { OtherUser } : Array.Empty<User>())); });
        users.Setup(c => c.FindOneAndUpdateAsync(session.Object, It.IsAny<FilterDefinition<User>>(), It.IsAny<UpdateDefinition<User>>(), It.IsAny<FindOneAndUpdateOptions<User, User>>(), It.IsAny<CancellationToken>()))
            .Returns((IClientSessionHandle _, FilterDefinition<User> _, UpdateDefinition<User> update, FindOneAndUpdateOptions<User, User> _, CancellationToken _) =>
            { Calls.Add("wallet.debit"); var inc = update.Render(new RenderArgs<User>(BsonSerializer.LookupSerializer<User>(), BsonSerializer.SerializerRegistry)); User.Wallet.Tickets += inc["$inc"]["wallet.tickets"].ToInt64(); return Task.FromResult(User); });
        users.Setup(c => c.UpdateOneAsync(session.Object, It.IsAny<FilterDefinition<User>>(), It.IsAny<UpdateDefinition<User>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Calls.Add("wallet.credit"); return Task.FromResult<UpdateResult>(new UpdateResult.Acknowledged(1, 1, null)); });
        items.Setup(c => c.FindAsync(session.Object, It.IsAny<FilterDefinition<Item>>(), It.IsAny<FindOptions<Item, Item>>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Calls.Add("items.find.session"); return Task.FromResult(Cursor(Items)); });
        banners.Setup(c => c.FindAsync(session.Object, It.IsAny<FilterDefinition<GachaBanner>>(), It.IsAny<FindOptions<GachaBanner, GachaBanner>>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Calls.Add("banner.find.session"); return Task.FromResult(Cursor(BannerMissing ? Array.Empty<GachaBanner>() : new[] { banner })); });
        pity.Setup(c => c.FindAsync(session.Object, It.IsAny<FilterDefinition<GachaPity>>(), It.IsAny<FindOptions<GachaPity, GachaPity>>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Calls.Add("pity.find.session"); return Task.FromResult(Cursor(Array.Empty<GachaPity>())); });
        pity.Setup(c => c.InsertOneAsync(session.Object, It.IsAny<GachaPity>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Calls.Add("pity.insert"); return Task.CompletedTask; });
        inventory.Setup(c => c.FindAsync(session.Object, It.IsAny<FilterDefinition<PlayerItem>>(), It.IsAny<FindOptions<PlayerItem, BsonDocument>>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Calls.Add("inventory.find"); return Task.FromResult(Cursor(owned ? new[] { new BsonDocument("_id", ObjectId.GenerateNewId()) } : Array.Empty<BsonDocument>())); });
        inventory.Setup(c => c.InsertOneAsync(session.Object, It.IsAny<PlayerItem>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Calls.Add("inventory.insert"); owned = true; return Task.CompletedTask; });
        ledger.Setup(c => c.InsertManyAsync(session.Object, It.IsAny<IEnumerable<CurrencyTransaction>>(), It.IsAny<InsertManyOptions>(), It.IsAny<CancellationToken>()))
            .Returns((IClientSessionHandle _, IEnumerable<CurrencyTransaction> rows, InsertManyOptions _, CancellationToken _) =>
            { Calls.Add("ledger.insert"); Ledger = rows.ToList(); return Task.CompletedTask; });
        Service = new GachaService(db.Object, client.Object, new InventoryService(db.Object));
    }

    private static Mock<IMongoCollection<T>> Collection<T>(Mock<IMongoDatabase> db, string name)
    {
        var collection = new Mock<IMongoCollection<T>>(MockBehavior.Strict);
        db.Setup(d => d.GetCollection<T>(name, It.IsAny<MongoCollectionSettings>())).Returns(collection.Object);
        return collection;
    }
    private static BsonDocument Render<T>(FilterDefinition<T> filter) =>
        filter.Render(new RenderArgs<T>(BsonSerializer.LookupSerializer<T>(), BsonSerializer.SerializerRegistry));
    private static IAsyncCursor<T> Cursor<T>(IEnumerable<T> rows)
    {
        var cursor = new Mock<IAsyncCursor<T>>(); cursor.SetupGet(c => c.Current).Returns(rows);
        cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
        cursor.Setup(c => c.Dispose()); return cursor.Object;
    }
}
