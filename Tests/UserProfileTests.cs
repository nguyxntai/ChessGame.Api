using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using ChessGame.Api.Controllers;
using ChessGame.Api.DTOs.User;
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

public sealed class UserProfileTests
{
    [Fact]
    public async Task PatchThenGetReturnsUnitySnapshotWithoutChangingProtectedData()
    {
        await using var api = await ProfileApi.StartAsync();
        var before = api.User!.ToBsonDocument();
        var response = await api.PatchAsync("""{"displayName":"  TaiChess  ","avatarId":"avatar_01"}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var snapshot = await response.Content.ReadFromJsonAsync<UserMeResponse>();
        Assert.Equal("TaiChess", snapshot!.Profile.DisplayName);
        Assert.Equal("avatar_01", snapshot.Profile.AvatarId);
        Assert.Equal(api.User!.Id.ToString(), snapshot.UserId);
        Assert.Equal(api.User.Username, snapshot.Username);
        Assert.Equal(api.User.Email, snapshot.Email);
        Assert.Equal(api.User.Wallet.Golds, snapshot.Wallet.Golds);
        Assert.Equal(api.User.Stats.Elo, snapshot.Stats.Elo);
        Assert.Equal(api.User.Equipped.ChessSkinId.ToString(), snapshot.Equipped.ChessSkinId);

        var after = api.User.ToBsonDocument();
        foreach (var field in before.Names.Where(field => field is not ("profile" or "updatedAt")))
            Assert.Equal(before[field], after[field]);
        Assert.True(api.User.UpdatedAt > before["updatedAt"].ToUniversalTime());
        Assert.Equal(new[] { "profile.avatarId", "profile.displayName", "updatedAt" },
            api.LastUpdate!["$set"].AsBsonDocument.Names.OrderBy(name => name));
        Assert.Equal(ReturnDocument.After, api.ReturnDocument);

        var get = await api.Client.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(await response.Content.ReadAsStringAsync(), await get.Content.ReadAsStringAsync());
        var json = await get.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", json);
        Assert.DoesNotContain("normalizedEmail", json);
        Assert.DoesNotContain("isActive", json);
    }

    [Theory]
    [InlineData("""{"displayName":"Hải"}""", "Hải", "avatar_02", "profile.displayName")]
    [InlineData("""{"avatarId":"avatar_05"}""", "Original", "avatar_05", "profile.avatarId")]
    [InlineData("""{"avatarId":null}""", "Original", null, "profile.avatarId")]
    public async Task PatchOnlyChangesSuppliedFields(string json, string name, string? avatar, string field)
    {
        await using var api = await ProfileApi.StartAsync();
        Assert.Equal(HttpStatusCode.OK, (await api.PatchAsync(json)).StatusCode);
        Assert.Equal(name, api.User!.Profile.DisplayName);
        Assert.Equal(avatar, api.User.Profile.AvatarId);
        Assert.Equal(new[] { field, "updatedAt" }, api.LastUpdate!["$set"].AsBsonDocument.Names.OrderBy(n => n));
    }

    [Theory]
    [InlineData("avatar_00")]
    [InlineData("avatar_01")]
    [InlineData("avatar_02")]
    [InlineData("avatar_03")]
    [InlineData("avatar_04")]
    [InlineData("avatar_05")]
    public async Task AcceptsAllSixUnityAvatars(string avatar)
    {
        await using var api = await ProfileApi.StartAsync();
        Assert.Equal(HttpStatusCode.OK, (await api.PatchAsync($$"""{"avatarId":"{{avatar}}"}""")).StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("""{"displayName":null}""")]
    [InlineData("""{"displayName":""}""")]
    [InlineData("""{"displayName":"   "}""")]
    [InlineData("""{"displayName":"1234567890123456789"}""")]
    [InlineData("""{"displayName":"Name\nInjected"}""")]
    [InlineData("""{"displayName":123}""")]
    [InlineData("""{"avatarId":"avatar_06"}""")]
    [InlineData("""{"avatarId":"avatar_1"}""")]
    [InlineData("""{"avatarId":""}""")]
    [InlineData("""{"avatarId":1}""")]
    [InlineData("""{"displayName":"TaiChess","elo":99999}""")]
    [InlineData("""{"displayName":"TaiChess","wallet":{"golds":99999}}""")]
    [InlineData("""{"displayName":"TaiChess","stats":{"elo":99999}}""")]
    [InlineData("""{"displayName":"TaiChess","inventory":[]}""")]
    [InlineData("""{"displayName":"TaiChess","equipped":{"chessSkinId":"fake"}}""")]
    [InlineData("""{"displayName":"TaiChess","userId":"someone-else"}""")]
    [InlineData("""{"displayName":"TaiChess","isActive":true}""")]
    [InlineData("""{"displayName":"TaiChess","hasAvatarId":true}""")]
    [InlineData("""{"profile":{"displayName":"TaiChess"}}""")]
    public async Task InvalidOrProtectedFieldsReturn400WithoutWriting(string json)
    {
        await using var api = await ProfileApi.StartAsync();
        var before = api.User!.ToBsonDocument();
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PatchAsync(json)).StatusCode);
        Assert.Null(api.LastUpdate);
        Assert.Equal(before, api.User.ToBsonDocument());
    }

    [Theory]
    [InlineData("GET", "missing")]
    [InlineData("PATCH", "missing")]
    [InlineData("GET", "invalid")]
    [InlineData("PATCH", "invalid")]
    [InlineData("GET", "no-sub")]
    [InlineData("PATCH", "no-sub")]
    [InlineData("GET", "bad-sub")]
    [InlineData("PATCH", "bad-sub")]
    [InlineData("GET", "expired")]
    [InlineData("PATCH", "expired")]
    public async Task RequiresValidAuthenticatedUserId(string method, string tokenKind)
    {
        await using var api = await ProfileApi.StartAsync();
        api.Client.DefaultRequestHeaders.Authorization = tokenKind switch
        {
            "missing" => null,
            "invalid" => new AuthenticationHeaderValue("Bearer", "invalid-token"),
            "no-sub" => new AuthenticationHeaderValue("Bearer", ProfileApi.Token(null)),
            "bad-sub" => new AuthenticationHeaderValue("Bearer", ProfileApi.Token("invalid-id")),
            _ => new AuthenticationHeaderValue("Bearer", ProfileApi.Token(api.User!.Id.ToString(), expired: true))
        };
        var response = method == "GET" ? await api.Client.GetAsync("/api/users/me")
            : await api.PatchAsync("""{"displayName":"TaiChess"}""");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(api.LastUpdate);
    }

    [Theory]
    [InlineData("GET", false, HttpStatusCode.NotFound)]
    [InlineData("PATCH", false, HttpStatusCode.NotFound)]
    [InlineData("GET", true, HttpStatusCode.Forbidden)]
    [InlineData("PATCH", true, HttpStatusCode.Forbidden)]
    public async Task MissingOrDisabledAccountCannotReadOrWrite(string method, bool disabled, HttpStatusCode expected)
    {
        await using var api = await ProfileApi.StartAsync();
        if (disabled) api.User!.IsActive = false;
        else api.User = null;
        var response = method == "GET" ? await api.Client.GetAsync("/api/users/me")
            : await api.PatchAsync("""{"displayName":"TaiChess"}""");
        Assert.Equal(expected, response.StatusCode);
        Assert.Null(api.LastUpdate);
    }

    [Fact]
    public async Task TokenForAnotherAccountCannotEditThisUser()
    {
        await using var api = await ProfileApi.StartAsync();
        api.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ProfileApi.Token(ObjectId.GenerateNewId().ToString()));
        Assert.Equal(HttpStatusCode.NotFound, (await api.PatchAsync("""{"displayName":"TaiChess"}""")).StatusCode);
        Assert.Null(api.LastUpdate);
        Assert.Equal("Original", api.User!.Profile.DisplayName);
    }

    [Theory]
    [InlineData(false, HttpStatusCode.NotFound)]
    [InlineData(true, HttpStatusCode.Forbidden)]
    public async Task AccountRemovedOrDisabledBetweenReadAndWriteIsNotUpdated(bool disabled, HttpStatusCode expected)
    {
        await using var api = await ProfileApi.StartAsync();
        api.BeforeUpdate = () => { if (disabled) api.User!.IsActive = false; else api.User = null; };
        Assert.Equal(expected, (await api.PatchAsync("""{"displayName":"TaiChess"}""")).StatusCode);
        Assert.True(api.LastFilter!["isActive"].AsBoolean);
        if (disabled) Assert.Equal("Original", api.User!.Profile.DisplayName);
    }

    // Exercise HTTP routing, JWT authorization, JSON binding and the real UserService.
    // Mock only the MongoDB boundary, inspect the generated atomic update and apply it
    // to a local document so a subsequent GET observes the persisted profile.
    private sealed class ProfileApi : IAsyncDisposable
    {
        private const string Key = "profile-tests-only-signing-key-at-least-32-bytes";
        private readonly WebApplication _app;
        public HttpClient Client { get; }
        public User? User { get; set; } = new()
        {
            Username = "hai", Email = "hai@example.test", PasswordHash = "private-hash",
            Profile = new UserProfile { DisplayName = "Original", AvatarId = "avatar_02" },
            Wallet = new PlayerWallet { Golds = 456, Diamonds = 23, Tickets = 7 },
            Stats = new PlayerStats { Elo = 1510, Wins = 5, Losses = 2, Draws = 1, GamesPlayed = 8 },
            Equipped = new EquippedSkins { ChessSkinId = ObjectId.GenerateNewId(), BoardSkinId = ObjectId.GenerateNewId() },
            UpdatedAt = DateTime.UtcNow.AddDays(-1)
        };
        public BsonDocument? LastUpdate { get; private set; }
        public BsonDocument? LastFilter { get; private set; }
        public ReturnDocument ReturnDocument { get; private set; }
        public Action? BeforeUpdate { get; set; }

        private ProfileApi(WebApplication app)
        {
            _app = app;
            Client = app.GetTestClient();
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(User!.Id.ToString()));
        }

        public static async Task<ProfileApi> StartAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddControllers().AddApplicationPart(typeof(UsersController).Assembly);
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = "profile-tests",
                    ValidateAudience = true, ValidAudience = "unity-tests",
                    ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key))
                });
            builder.Services.AddAuthorization();
            var collection = new Mock<IMongoCollection<User>>();
            var database = new Mock<IMongoDatabase>();
            database.Setup(db => db.GetCollection<User>("users", It.IsAny<MongoCollectionSettings>())).Returns(collection.Object);
            builder.Services.AddSingleton(database.Object);
            builder.Services.AddScoped<UserService>();
            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
            await app.StartAsync();
            var api = new ProfileApi(app);

            collection.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<User>>(), It.IsAny<FindOptions<User, User>>(), It.IsAny<CancellationToken>()))
                .Returns((FilterDefinition<User> filter, FindOptions<User, User> _, CancellationToken _) =>
                {
                    var matches = api.User is not null && Render(filter)["_id"].AsObjectId == api.User.Id;
                    var cursor = new Mock<IAsyncCursor<User>>();
                    cursor.SetupGet(c => c.Current).Returns(matches ? new[] { api.User! } : Array.Empty<User>());
                    cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
                    return Task.FromResult(cursor.Object);
                });

            collection.Setup(c => c.FindOneAndUpdateAsync(It.IsAny<FilterDefinition<User>>(), It.IsAny<UpdateDefinition<User>>(), It.IsAny<FindOneAndUpdateOptions<User, User>>(), It.IsAny<CancellationToken>()))
                .Returns((FilterDefinition<User> filter, UpdateDefinition<User> update, FindOneAndUpdateOptions<User, User> options, CancellationToken _) =>
                {
                    api.LastFilter = Render(filter);
                    api.LastUpdate = update.Render(new RenderArgs<User>(BsonSerializer.LookupSerializer<User>(), BsonSerializer.SerializerRegistry)).AsBsonDocument;
                    api.ReturnDocument = options.ReturnDocument;
                    api.BeforeUpdate?.Invoke();
                    if (api.User is null || api.User.Id != api.LastFilter["_id"].AsObjectId || !api.User.IsActive)
                        return Task.FromResult<User>(null!);
                    foreach (var field in api.LastUpdate["$set"].AsBsonDocument)
                    {
                        switch (field.Name)
                        {
                            case "profile.displayName": api.User.Profile.DisplayName = field.Value.AsString; break;
                            case "profile.avatarId": api.User.Profile.AvatarId = field.Value.IsBsonNull ? null : field.Value.AsString; break;
                            case "updatedAt": api.User.UpdatedAt = field.Value.ToUniversalTime(); break;
                            default: throw new InvalidOperationException("Unexpected profile update: " + field.Name);
                        }
                    }
                    return Task.FromResult(api.User);
                });
            return api;
        }

        private static BsonDocument Render(FilterDefinition<User> filter) =>
            filter.Render(new RenderArgs<User>(BsonSerializer.LookupSerializer<User>(), BsonSerializer.SerializerRegistry));

        public static string Token(string? userId, bool expired = false)
        {
            var claims = userId is null ? Array.Empty<Claim>() : new[] { new Claim(JwtRegisteredClaimNames.Sub, userId) };
            var jwt = new JwtSecurityToken("profile-tests", "unity-tests", claims,
                expires: expired ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }

        public Task<HttpResponseMessage> PatchAsync(string json) =>
            Client.PatchAsync("/api/users/me/profile", new StringContent(json, Encoding.UTF8, "application/json"));

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }
    }
}
