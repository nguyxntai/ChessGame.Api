using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ChessGame.Api.Controllers;
using ChessGame.Api.DTOs.Wallet;
using ChessGame.Api.Models;
using ChessGame.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace ChessGame.Api.Tests;

public sealed class WalletTests
{
    [Fact]
    public async Task WalletReadsPersistedBalancesAndCannotSelectAnotherUser()
    {
        await using var api = await WalletApi.StartAsync();
        var response = await api.Client.GetAsync($"/api/wallet?userId={ObjectId.GenerateNewId()}&golds=999999");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "diamonds", "golds", "tickets" },
            json.RootElement.EnumerateObject().Select(x => x.Name).OrderBy(x => x));
        Assert.Equal(456, json.RootElement.GetProperty("golds").GetInt64());
        Assert.Equal(23, json.RootElement.GetProperty("diamonds").GetInt64());
        Assert.Equal(7, json.RootElement.GetProperty("tickets").GetInt64());
        Assert.Equal(456, api.User!.Wallet.Golds);
    }

    [Fact]
    public async Task HistoryIsScopedToTokenAndPreservesSignedAmountsWithStablePagination()
    {
        await using var api = await WalletApi.StartAsync();
        var at = DateTime.UtcNow;
        for (var i = 1; i <= 3; i++)
            api.Transactions.Add(new CurrencyTransaction
            {
                Id = ObjectId.Parse($"00000000000000000000000{i}"), UserId = api.User!.Id,
                Currency = "TICKETS", Amount = i == 3 ? -1 : 5, BalanceAfter = 10 + i,
                Source = "GACHA", RequestId = "roll-1", CreatedAt = at
            });
        api.Transactions.Add(new CurrencyTransaction { UserId = ObjectId.GenerateNewId(), Amount = 9999 });

        var response = await api.Client.GetAsync("/api/wallet/transactions?pageSize=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var first = (await response.Content.ReadFromJsonAsync<WalletTransactionsResponse>())!;
        Assert.Equal(3, first.Total);
        Assert.Equal(1, first.Page);
        Assert.Equal(2, first.PageSize);
        Assert.Equal(new[] { api.Transactions[2].Id.ToString(), api.Transactions[1].Id.ToString() },
            first.Items.Select(x => x.TransactionId));
        Assert.Equal(-1, first.Items[0].Amount);
        Assert.Equal(13, first.Items[0].BalanceAfter);
        Assert.Equal("TICKETS", first.Items[0].Currency);
        Assert.Equal("GACHA", first.Items[0].Source);
        Assert.Equal("roll-1", first.Items[0].RequestId);
        Assert.DoesNotContain("userId", await response.Content.ReadAsStringAsync());

        var second = await api.Client.GetFromJsonAsync<WalletTransactionsResponse>("/api/wallet/transactions?page=2&pageSize=2");
        Assert.Equal(api.Transactions[0].Id.ToString(), Assert.Single(second!.Items).TransactionId);
        var beyond = await api.Client.GetFromJsonAsync<WalletTransactionsResponse>("/api/wallet/transactions?page=3&pageSize=2");
        Assert.Empty(beyond!.Items);
        Assert.Equal(3, beyond.Total);
    }

    [Fact]
    public async Task EmptyHistoryReturnsDefaultPage()
    {
        await using var api = await WalletApi.StartAsync();
        var history = await api.Client.GetFromJsonAsync<WalletTransactionsResponse>("/api/wallet/transactions");
        Assert.Equal(1, history!.Page);
        Assert.Equal(20, history.PageSize);
        Assert.Equal(0, history.Total);
        Assert.Empty(history.Items);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("pageSize=-1")]
    [InlineData("page=2147483647&pageSize=100")]
    [InlineData("page=abc")]
    public async Task InvalidPaginationReturns400WithoutReadingLedger(string query)
    {
        await using var api = await WalletApi.StartAsync();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await api.Client.GetAsync("/api/wallet/transactions?" + query)).StatusCode);
        Assert.Equal(0, api.LedgerReads);
    }

    [Theory]
    [InlineData("missing", HttpStatusCode.Unauthorized)]
    [InlineData("invalid", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("no-sub", HttpStatusCode.Unauthorized)]
    [InlineData("bad-sub", HttpStatusCode.Unauthorized)]
    [InlineData("other-user", HttpStatusCode.NotFound)]
    [InlineData("deleted", HttpStatusCode.NotFound)]
    [InlineData("disabled", HttpStatusCode.Forbidden)]
    public async Task BothEndpointsRequireExistingActiveAuthenticatedAccount(string kind, HttpStatusCode expected)
    {
        await using var api = await WalletApi.StartAsync();
        api.Client.DefaultRequestHeaders.Authorization = kind switch
        {
            "missing" => null,
            "invalid" => new AuthenticationHeaderValue("Bearer", "invalid-token"),
            "expired" => new AuthenticationHeaderValue("Bearer", WalletApi.Token(api.User!.Id.ToString(), true)),
            "no-sub" => new AuthenticationHeaderValue("Bearer", WalletApi.Token(null)),
            "bad-sub" => new AuthenticationHeaderValue("Bearer", WalletApi.Token("bad-id")),
            "other-user" => new AuthenticationHeaderValue("Bearer", WalletApi.Token(ObjectId.GenerateNewId().ToString())),
            _ => api.Client.DefaultRequestHeaders.Authorization
        };
        if (kind == "deleted") api.User = null;
        if (kind == "disabled") api.User!.IsActive = false;
        Assert.Equal(expected, (await api.Client.GetAsync("/api/wallet")).StatusCode);
        Assert.Equal(expected, (await api.Client.GetAsync("/api/wallet/transactions")).StatusCode);
        Assert.Equal(0, api.LedgerReads);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task WalletDoesNotExposeMutationRoutes(string method)
    {
        await using var api = await WalletApi.StartAsync();
        foreach (var path in new[] { "/api/wallet", "/api/wallet/transactions" })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path)
            { Content = new StringContent("""{"golds":999999,"amount":999999}""", Encoding.UTF8, "application/json") };
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await api.Client.SendAsync(request)).StatusCode);
        }
        Assert.Equal(456, api.User!.Wallet.Golds);
        Assert.Empty(api.Transactions);
    }

    // Run real HTTP routing, JWT and WalletService; mock only MongoDB and evaluate
    // the generated user filters and pagination against documents from two users.
    private sealed class WalletApi : IAsyncDisposable
    {
        private const string Key = "wallet-tests-only-signing-key-at-least-32-bytes";
        private readonly WebApplication _app;
        public HttpClient Client { get; }
        public User? User { get; set; } = new() { Wallet = new PlayerWallet { Golds = 456, Diamonds = 23, Tickets = 7 } };
        public List<CurrencyTransaction> Transactions { get; } = new();
        public int LedgerReads { get; private set; }

        private WalletApi(WebApplication app)
        {
            _app = app;
            Client = app.GetTestClient();
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(User!.Id.ToString()));
        }

        public static async Task<WalletApi> StartAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddControllers().AddApplicationPart(typeof(WalletController).Assembly);
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = "wallet-tests",
                    ValidateAudience = true, ValidAudience = "wallet-client",
                    ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key))
                });
            builder.Services.AddAuthorization();
            var users = new Mock<IMongoCollection<User>>(MockBehavior.Strict);
            var ledger = new Mock<IMongoCollection<CurrencyTransaction>>(MockBehavior.Strict);
            var database = new Mock<IMongoDatabase>();
            database.Setup(x => x.GetCollection<User>("users", It.IsAny<MongoCollectionSettings>())).Returns(users.Object);
            database.Setup(x => x.GetCollection<CurrencyTransaction>("currency_transactions", It.IsAny<MongoCollectionSettings>())).Returns(ledger.Object);
            builder.Services.AddSingleton(database.Object);
            builder.Services.AddScoped<WalletService>();
            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
            await app.StartAsync();
            var api = new WalletApi(app);

            users.Setup(x => x.FindAsync(It.IsAny<FilterDefinition<User>>(), It.IsAny<FindOptions<User, User>>(), It.IsAny<CancellationToken>()))
                .Returns((FilterDefinition<User> filter, FindOptions<User, User> _, CancellationToken _) =>
                    Task.FromResult(Cursor(api.User is not null && Render(filter)["_id"].AsObjectId == api.User.Id
                        ? new[] { api.User! } : Array.Empty<User>())));
            ledger.Setup(x => x.CountDocumentsAsync(It.IsAny<FilterDefinition<CurrencyTransaction>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
                .Returns((FilterDefinition<CurrencyTransaction> filter, CountOptions _, CancellationToken _) =>
                {
                    api.LedgerReads++;
                    return Task.FromResult(api.Transactions.LongCount(x => x.UserId == Render(filter)["userId"].AsObjectId));
                });
            ledger.Setup(x => x.FindAsync(It.IsAny<FilterDefinition<CurrencyTransaction>>(), It.IsAny<FindOptions<CurrencyTransaction, CurrencyTransaction>>(), It.IsAny<CancellationToken>()))
                .Returns((FilterDefinition<CurrencyTransaction> filter, FindOptions<CurrencyTransaction, CurrencyTransaction> options, CancellationToken _) =>
                {
                    api.LedgerReads++;
                    var sort = options.Sort.Render(new RenderArgs<CurrencyTransaction>(BsonSerializer.LookupSerializer<CurrencyTransaction>(), BsonSerializer.SerializerRegistry));
                    Assert.Equal(new BsonDocument { { "createdAt", -1 }, { "_id", -1 } }, sort);
                    var rows = api.Transactions.Where(x => x.UserId == Render(filter)["userId"].AsObjectId)
                        .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                        .Skip(options.Skip ?? 0).Take(options.Limit ?? int.MaxValue).ToArray();
                    return Task.FromResult(Cursor(rows));
                });
            return api;
        }

        private static BsonDocument Render<T>(FilterDefinition<T> filter) =>
            filter.Render(new RenderArgs<T>(BsonSerializer.LookupSerializer<T>(), BsonSerializer.SerializerRegistry));

        private static IAsyncCursor<T> Cursor<T>(IEnumerable<T> rows)
        {
            var cursor = new Mock<IAsyncCursor<T>>();
            cursor.SetupGet(x => x.Current).Returns(rows);
            cursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
            return cursor.Object;
        }

        public static string Token(string? id, bool expired = false) => new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken("wallet-tests", "wallet-client",
                id is null ? Array.Empty<Claim>() : new[] { new Claim(JwtRegisteredClaimNames.Sub, id) },
                expires: expired ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256)));

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }
    }
}
