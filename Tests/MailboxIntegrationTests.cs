using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using ChessGame.Api.Controllers;
using ChessGame.Api.Models;
using ChessGame.Api.Services.Mailbox;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace ChessGame.Api.Tests;

public sealed class MongoMailboxFactAttribute : FactAttribute
{
    public MongoMailboxFactAttribute() { if (Environment.GetEnvironmentVariable("MAILBOX_TEST_MONGO_URI") == null) Skip = "Set MAILBOX_TEST_MONGO_URI to an isolated localhost replica set."; }
}

public sealed class MailboxIntegrationTests : IAsyncLifetime
{
    private IMongoDatabase db = null!;
    private IMongoClient client = null!;
    private MailboxService service = null!;
    private string databaseName = "";
    private readonly WelcomeGiftOptions options = new() { Body = "Dear Tactician,\n\nYour welcome gift never expires." };
    public async Task InitializeAsync()
    {
        var uri = Environment.GetEnvironmentVariable("MAILBOX_TEST_MONGO_URI");
        if (uri == null) return;
        var url = new MongoUrl(uri);
        if (url.Servers.Any(s => s.Host is not ("127.0.0.1" or "localhost"))) throw new InvalidOperationException("Tests only accept loopback MongoDB.");
        client = new MongoClient(uri);
        var admin = client.GetDatabase("admin");
        try { await admin.RunCommandAsync<BsonDocument>(new BsonDocument("replSetInitiate", new BsonDocument { { "_id", "mailbox_test" }, { "members", new BsonArray { new BsonDocument { { "_id", 0 }, { "host", "127.0.0.1:27029" } } } } })); }
        catch (MongoCommandException ex) when (ex.Code == 23) { }
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var state = await admin.RunCommandAsync<BsonDocument>(new BsonDocument("isMaster", 1));
            if (state.GetValue("ismaster", false).ToBoolean()) break;
            await Task.Delay(100);
        }
        databaseName = "mailbox_test_" + Guid.NewGuid().ToString("N");
        db = client.GetDatabase(databaseName); service = NewService(); await service.EnsureIndexesAsync();
    }
    public async Task DisposeAsync() { if (client != null && databaseName.StartsWith("mailbox_test_")) await client.DropDatabaseAsync(databaseName); }
    private MailboxService NewService() => new(db, client, Options.Create(options));
    private async Task<User> User(bool active = true)
    {
        var user = new User { Id = ObjectId.GenerateNewId(), Username = "test", IsActive = active, CreatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        await db.GetCollection<User>("users").InsertOneAsync(user); return user;
    }
    private Task<User> Reload(ObjectId id) => db.GetCollection<User>("users").Find(u => u.Id == id).SingleAsync();

    [MongoMailboxFact] public async Task ConcurrentEnrollmentAndClaimsGrantExactlyOnceAndSurviveServiceRecreation()
    {
        var user = await User();
        var enrollments = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => NewService().EnrollAsync(user.Id)));
        Assert.Single(enrollments, e => e.Created);
        var message = enrollments[0].Mail!;
        Assert.Null(message.ExpiresAtUtc); Assert.Null(message.ClaimedAtUtc);
        Assert.Equal(1000, (await Reload(user.Id)).Wallet.Golds);
        var receipts = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => NewService().ClaimAsync(user.Id, message.MailId)));
        Assert.Single(receipts.Select(r => r.ReceiptId).Distinct()); Assert.Single(receipts.Select(r => r.ClaimedAtUtc).Distinct());
        var after = await Reload(user.Id);
        Assert.Equal(3000, after.Wallet.Golds); Assert.Equal(100, after.Wallet.Diamonds); Assert.Equal(13, after.Wallet.Tickets);
        Assert.Equal(3, await db.GetCollection<CurrencyTransaction>("currency_transactions").CountDocumentsAsync(t => t.UserId == user.Id));
        var replay = await NewService().ClaimAsync(user.Id, message.MailId);
        Assert.Equal(receipts[0].ReceiptId, replay.ReceiptId); Assert.Equal(receipts[0].ClaimedAtUtc, replay.ClaimedAtUtc);
        Assert.Equal(3000, (await Reload(user.Id)).Wallet.Golds);
        Assert.Equal(message.Body, (await service.GetAsync(user.Id, message.MailId)).Body);
        var reenrolled = await NewService().EnrollAsync(user.Id);
        Assert.False(reenrolled.Created); Assert.NotNull(reenrolled.Mail!.ClaimedAtUtc);
    }
    [MongoMailboxFact] public async Task ReadingAndPagingAreSeparateFromClaimingAndStatesPersist()
    {
        var user = await User(); var m = (await service.EnrollAsync(user.Id)).Mail!;
        var before = await service.ListAsync(user.Id);
        Assert.Equal(1, before.UnreadCount); Assert.Equal(1, before.UnclaimedCount);
        var read = await service.ReadAsync(user.Id, m.MailId); Assert.NotNull(read.ReadAtUtc); Assert.Null(read.ClaimedAtUtc);
        Assert.Equal(read.ReadAtUtc, (await NewService().ReadAsync(user.Id, m.MailId)).ReadAtUtc);
        var after = await service.ListAsync(user.Id); Assert.Equal(0, after.UnreadCount); Assert.Equal(1, after.UnclaimedCount);
        Assert.Equal(1000, (await Reload(user.Id)).Wallet.Golds);
        await service.ClaimAsync(user.Id, m.MailId);
        Assert.Equal(0, (await NewService().ListAsync(user.Id)).UnclaimedCount);
        Assert.Empty((await service.ListAsync(user.Id, 2, 20)).Items);
        Assert.Equal("INVALID_PAGE", (await Assert.ThrowsAsync<MailboxException>(() => service.ListAsync(user.Id, int.MaxValue, 50))).Code);
    }
    [MongoMailboxFact] public async Task FailureAfterWalletWriteRollsBackWalletMailAndLedger()
    {
        var user = await User(); var m = (await service.EnrollAsync(user.Id)).Mail!;
        // Deliberately collide with the last insert, after all wallet/mail writes inside the transaction.
        await db.GetCollection<CurrencyTransaction>("currency_transactions").InsertOneAsync(new CurrencyTransaction {
            UserId = user.Id, Source = "MAILBOX", RequestId = "mail:" + m.MailId, EntryIndex = 2, Currency = "TICKETS" });
        await Assert.ThrowsAnyAsync<MongoException>(() => service.ClaimAsync(user.Id, m.MailId));
        Assert.Equal(1000, (await Reload(user.Id)).Wallet.Golds);
        Assert.Null((await service.GetAsync(user.Id, m.MailId)).ClaimedAtUtc);
        Assert.Equal(1, await db.GetCollection<CurrencyTransaction>("currency_transactions").CountDocumentsAsync(t => t.UserId == user.Id));
    }
    [MongoMailboxFact] public async Task DisabledMissingForeignAndExpiredAccountsCannotReceiveRewards()
    {
        var user = await User(); var other = await User(); var disabled = await User(false);
        Assert.Equal("ACCOUNT_DISABLED", (await Assert.ThrowsAsync<MailboxException>(() => service.EnrollAsync(disabled.Id))).Code);
        Assert.Equal("USER_NOT_FOUND", (await Assert.ThrowsAsync<MailboxException>(() => service.EnrollAsync(ObjectId.GenerateNewId()))).Code);
        var m = (await service.EnrollAsync(user.Id)).Mail!;
        foreach (var action in new Func<Task>[] { async () => await service.GetAsync(other.Id, m.MailId), async () => await service.ReadAsync(other.Id, m.MailId), async () => await service.ClaimAsync(other.Id, m.MailId) })
            Assert.Equal("MAIL_NOT_FOUND", (await Assert.ThrowsAsync<MailboxException>(action)).Code);
        await db.GetCollection<MailboxMessage>("mailbox_messages").UpdateOneAsync(x => x.MailId == m.MailId, Builders<MailboxMessage>.Update.Set(x => x.ExpiresAtUtc, DateTime.UtcNow.AddDays(-1)));
        Assert.Equal("MAIL_EXPIRED", (await Assert.ThrowsAsync<MailboxException>(() => service.ClaimAsync(user.Id, m.MailId))).Code);
        Assert.Equal(0, (await service.ListAsync(user.Id)).UnclaimedCount);
    }
    [MongoMailboxFact] public async Task ChangedCampaignConfigurationDoesNotRewriteExistingAttachmentsOrCreateAnotherWelcome()
    {
        var user = await User(); var first = (await service.EnrollAsync(user.Id)).Mail!;
        options.Golds = 7000;
        var second = await NewService().EnrollAsync(user.Id); Assert.False(second.Created);
        Assert.Equal(first.MailId, second.Mail!.MailId); Assert.Equal(2000, second.Mail.Attachments.Single(a => a.Currency == "GOLDS").Amount);
        await service.ClaimAsync(user.Id, first.MailId); Assert.Equal(3000, (await Reload(user.Id)).Wallet.Golds);
        var newUser = await User(); Assert.Equal(7000, (await service.EnrollAsync(newUser.Id)).Mail!.Attachments.Single(a => a.Currency == "GOLDS").Amount);
    }
    [MongoMailboxFact] public async Task AmbiguousCommitConnectionLossRetriesWithoutDoubleGrant()
    {
        var user = await User(); var m = (await service.EnrollAsync(user.Id)).Mail!;
        await client.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument {
            { "configureFailPoint", "failCommand" }, { "mode", new BsonDocument("times", 1) },
            { "data", new BsonDocument { { "failCommands", new BsonArray { "commitTransaction" } }, { "closeConnection", true } } } });
        var result = await service.ClaimAsync(user.Id, m.MailId);
        Assert.Equal(result.ReceiptId, (await NewService().ClaimAsync(user.Id, m.MailId)).ReceiptId);
        Assert.Equal(3000, (await Reload(user.Id)).Wallet.Golds);
        Assert.Equal(3, await db.GetCollection<CurrencyTransaction>("currency_transactions").CountDocumentsAsync(t => t.UserId == user.Id));
    }
    [MongoMailboxFact] public async Task HttpRequiresRealJwtRejectsGuestForeignMailAndForgedAmounts()
    {
        var user = await User(); var other = await User(); var m = (await service.EnrollAsync(user.Id)).Mail!;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("mailbox-integration-test-key-32-bytes-minimum"));
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(service);
        builder.Services.AddControllers().AddApplicationPart(typeof(MailboxController).Assembly);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters {
            ValidateIssuer = false, ValidateAudience = false, ValidateLifetime = true, IssuerSigningKey = key });
        builder.Services.AddAuthorization();
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        using var http = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/mailbox")).StatusCode);
        void Authenticate(ObjectId id, bool guest = false) {
            var jwt = new JwtSecurityToken(claims: new[] { new Claim(JwtRegisteredClaimNames.Sub, id.ToString()), new Claim("guest", guest ? "true" : "false") },
                expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
        }
        Authenticate(user.Id, true); Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/mailbox/enroll", new { })).StatusCode);
        Authenticate(other.Id); Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/mailbox/" + m.MailId)).StatusCode);
        Authenticate(user.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync($"/api/mailbox/{m.MailId}/claim", new { golds = 999999, userId = other.Id.ToString() })).StatusCode);
        Assert.Equal(1000, (await Reload(user.Id)).Wallet.Golds);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync($"/api/mailbox/{m.MailId}/claim", new { })).StatusCode);
        var repeated = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => http.PostAsJsonAsync($"/api/mailbox/{m.MailId}/claim", new { })));
        Assert.All(repeated, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var receipts = await Task.WhenAll(repeated.Select(async response => (await response.Content.ReadFromJsonAsync<MailClaimReceipt>())!.ReceiptId));
        Assert.Single(receipts.Distinct());
        Assert.Equal(3000, (await Reload(user.Id)).Wallet.Golds);
        Assert.Equal(3, await db.GetCollection<CurrencyTransaction>("currency_transactions").CountDocumentsAsync(t => t.UserId == user.Id));
    }
}
