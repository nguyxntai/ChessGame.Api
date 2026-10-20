using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using ChessGame.Api.Controllers;
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

// All MongoDB interfaces are strict fakes. No MongoClient/server/Program.cs/production config.
internal sealed class GachaRecoveryFixture : IAsyncDisposable
{
    internal static readonly ObjectId Owner = ObjectId.Parse("111111111111111111111111");
    internal static readonly ObjectId Other = ObjectId.Parse("222222222222222222222222");
    internal static readonly Guid RequestId = Guid.Parse("aabbccdd-1122-3344-5566-778899aabbcc");
    private const string SigningKey = "B2_TEST_ONLY_HMAC_KEY_SYNTHETIC_NOT_PRODUCTION_0123456789_ABCDEF";
    internal readonly ConcurrentQueue<string> Calls = new();
    internal readonly ConcurrentQueue<BsonDocument> RollFilters = new();
    internal readonly List<BsonDocument> Rows = new();
    internal readonly Dictionary<ObjectId, User> Users = new();
    internal readonly Mock<IMongoClient> Client = new(MockBehavior.Strict);
    internal readonly Mock<IMongoDatabase> Database = new(MockBehavior.Strict);
    internal readonly Mock<IMongoCollection<GachaRoll>> Rolls;
    internal readonly Mock<IMongoCollection<GachaRoll>> ReadView = new(MockBehavior.Strict);
    internal readonly GachaService Service;
    internal Exception? RollReadError;
    internal Exception? UserReadError;
    internal CancellationToken LastReadToken;
    internal HttpClient Http = null!;
    private WebApplication? app;
    private readonly List<Mock> unusedCollections = new();

    internal GachaRecoveryFixture(int count = 1)
    {
        Users[Owner] = new User { Id = Owner, Wallet = new PlayerWallet { Golds = 42, Tickets = 87 } };
        Users[Other] = new User { Id = Other };
        Rows.Add(Receipt(count).ToBsonDocument());
        Rolls = Collection<GachaRoll>("gacha_rolls");
        var users = Collection<User>("users");
        unusedCollections.Add(Collection<GachaBanner>("gacha_banners"));
        unusedCollections.Add(Collection<GachaPity>("player_gacha_pity"));
        unusedCollections.Add(Collection<CurrencyTransaction>("currency_transactions"));
        unusedCollections.Add(Collection<Item>("items"));
        unusedCollections.Add(Collection<PlayerItem>("player_items"));
        users.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<User>>(), It.IsAny<FindOptions<User, User>>(), It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<User> filter, FindOptions<User, User> _, CancellationToken token) =>
            {
                Calls.Enqueue("user.read"); token.ThrowIfCancellationRequested();
                if (UserReadError is not null) throw UserReadError;
                var id = Render(filter)["_id"].AsObjectId;
                return Task.FromResult(Cursor(Users.TryGetValue(id, out var user) ? new[] { user } : Array.Empty<User>()));
            });
        Rolls.Setup(c => c.WithReadConcern(It.Is<ReadConcern>(v => v.Level == ReadConcernLevel.Majority)))
            .Returns(() => { Calls.Enqueue("readConcern.majority"); return ReadView.Object; });
        ReadView.Setup(c => c.WithReadPreference(It.Is<ReadPreference>(v => v.ReadPreferenceMode == ReadPreferenceMode.Primary)))
            .Returns(() => { Calls.Enqueue("readPreference.primary"); return ReadView.Object; });
        ReadView.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<GachaRoll>>(), It.IsAny<FindOptions<GachaRoll, GachaRoll>>(), It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<GachaRoll> filter, FindOptions<GachaRoll, GachaRoll> _, CancellationToken token) =>
            {
                Calls.Enqueue("roll.read"); LastReadToken = token; token.ThrowIfCancellationRequested();
                if (RollReadError is not null) throw RollReadError;
                return Task.FromResult(Cursor(Matching(filter)));
            });
        // Only for explicit comparisons with existing replay/history. Recovery tests assert
        // their trace never takes this legacy-default-consistency path.
        Rolls.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<GachaRoll>>(), It.IsAny<FindOptions<GachaRoll, GachaRoll>>(), It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<GachaRoll> filter, FindOptions<GachaRoll, GachaRoll> _, CancellationToken _) =>
            { Calls.Enqueue("legacy.roll.read"); return Task.FromResult(Cursor(Matching(filter))); });
        Rolls.Setup(c => c.CountDocumentsAsync(It.IsAny<FilterDefinition<GachaRoll>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
            .Returns((FilterDefinition<GachaRoll> filter, CountOptions _, CancellationToken _) =>
            { Calls.Enqueue("legacy.roll.count"); return Task.FromResult((long)Matching(filter).Count); });
        Service = new GachaService(Database.Object, Client.Object, new InventoryService(Database.Object));
    }

    internal static GachaRoll Receipt(int count = 1, ObjectId? owner = null) => new()
    {
        Id = ObjectId.Parse(owner == Other ? "555555555555555555555555" : "444444444444444444444444"), UserId = owner ?? Owner,
        RequestId = RequestId.ToString("D"), BannerCode = "TEST_BANNER", PityGroup = "TEST_PITY",
        Count = count, Currency = "TICKETS", TotalCost = count,
        WalletAfter = new GachaWalletSnapshot { Golds = long.MaxValue, Diamonds = 123, Tickets = 50 },
        PityAfter = new GachaPitySnapshot { Epic = 2, Legendary = 7 },
        PityLimit = new GachaPityLimits { Epic = 3, Legendary = 9 },
        CreatedAt = new DateTime(2026, 10, 19, 1, 2, 3, 456, DateTimeKind.Utc),
        Results = Enumerable.Range(0, count).Select(i => new GachaRollReward
        {
            ItemId = ObjectId.Parse("333333333333333333333333"), ItemCode = "original-code", ItemName = "Original fixture reward",
            Rarity = "LEGENDARY", IsDuplicate = true,
            DuplicateReward = new GachaDuplicateReward { Currency = "GOLDS", Amount = long.MaxValue }
        }).ToList(),
        PresentationSnapshot = new GachaPresentationSnapshot(Enumerable.Range(0, count).Select(i =>
            new GachaRewardMetadataSnapshot(i, ObjectId.Parse("333333333333333333333333"), "original-code", "UNAPPROVED_RAW_VALUE", "original/key")))
    };

    internal async Task StartApi()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "B2OfflineTests" });
        builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(GachaController).Assembly);
        builder.Services.AddSingleton(Service);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = "b2-offline-tests",
                ValidateAudience = true, ValidAudience = "b2-test-client",
                ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey))
            });
        builder.Services.AddAuthorization();
        app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync(); Http = app.GetTestClient();
        SetSubject(Owner.ToString());
    }

    internal void SetSubject(string? subject, bool invalidSignature = false, bool expired = false)
    {
        var jwt = new JwtSecurityToken("b2-offline-tests", "b2-test-client",
            subject is null ? Array.Empty<Claim>() : new[] { new Claim(JwtRegisteredClaimNames.Sub, subject) },
            expires: DateTime.UtcNow.AddMinutes(expired ? -5 : 5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                invalidSignature ? SigningKey + "WRONG_KEY" : SigningKey)), SecurityAlgorithms.HmacSha256));
        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
    }

    internal static string PathFor(string? id = null) => "/api/gacha/rolls/by-request/" + (id ?? RequestId.ToString("D"));
    internal string State() => string.Join("\n", Rows.Select(r => r.ToJson()).Concat(Users.OrderBy(p => p.Key).Select(p => p.Value.ToJson())));
    internal void AssertRecoveryOnly()
    {
        Assert.Empty(Client.Invocations);
        foreach (var collection in unusedCollections) Assert.Empty(collection.Invocations);
        Assert.All(Calls, call => Assert.Contains(call, new[] { "user.read", "readConcern.majority", "readPreference.primary", "roll.read" }));
        Assert.All(RollFilters, filter => Assert.Equal(new[] { "requestId", "userId" }, filter.Names.OrderBy(n => n)));
    }

    private Mock<IMongoCollection<T>> Collection<T>(string name)
    {
        var c = new Mock<IMongoCollection<T>>(MockBehavior.Strict);
        Database.Setup(d => d.GetCollection<T>(name, It.IsAny<MongoCollectionSettings>())).Returns(c.Object);
        return c;
    }
    private List<GachaRoll> Matching(FilterDefinition<GachaRoll> filter)
    {
        var doc = Render(filter); RollFilters.Enqueue(doc);
        var id = doc["userId"].AsObjectId;
        var hasRequest = doc.TryGetValue("requestId", out var request);
        return Rows.Where(r => r["userId"].AsObjectId == id && (!hasRequest || r["requestId"].AsString == request!.AsString))
            .Select(r => BsonSerializer.Deserialize<GachaRoll>(r.DeepClone().AsBsonDocument)).ToList();
    }
    private static BsonDocument Render<T>(FilterDefinition<T> filter) =>
        filter.Render(new RenderArgs<T>(BsonSerializer.LookupSerializer<T>(), BsonSerializer.SerializerRegistry));
    private static IAsyncCursor<T> Cursor<T>(IEnumerable<T> values)
    {
        var cursor = new Mock<IAsyncCursor<T>>(MockBehavior.Strict);
        cursor.SetupGet(c => c.Current).Returns(values);
        cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
        cursor.Setup(c => c.Dispose()); return cursor.Object;
    }
    public async ValueTask DisposeAsync()
    {
        Http?.Dispose(); if (app is not null) await app.DisposeAsync();
    }
}
