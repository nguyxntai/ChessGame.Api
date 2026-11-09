using ChessGame.Api.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ChessGame.Api.Services.Mailbox;

public sealed class MailboxService
{
    private readonly IMongoClient client;
    private readonly IMongoCollection<User> users;
    private readonly IMongoCollection<MailboxMessage> mail;
    private readonly IMongoCollection<CurrencyTransaction> ledger;
    private readonly WelcomeGiftOptions gift;
    public MailboxService(IMongoDatabase db, IMongoClient client, IOptions<WelcomeGiftOptions> options)
    {
        this.client = client; gift = options.Value;
        users = db.GetCollection<User>("users"); mail = db.GetCollection<MailboxMessage>("mailbox_messages").WithWriteConcern(WriteConcern.WMajority);
        ledger = db.GetCollection<CurrencyTransaction>("currency_transactions");
    }
    public async Task EnsureIndexesAsync()
    {
        await mail.Indexes.CreateManyAsync(new[] {
            new CreateIndexModel<MailboxMessage>(Builders<MailboxMessage>.IndexKeys.Ascending(m => m.UserId).Ascending(m => m.CampaignId),
                new CreateIndexOptions<MailboxMessage> { Name = "mail_user_campaign_unique", Unique = true,
                    PartialFilterExpression = Builders<MailboxMessage>.Filter.Type(m => m.CampaignId, BsonType.String) }),
            new CreateIndexModel<MailboxMessage>(Builders<MailboxMessage>.IndexKeys.Ascending(m => m.UserId).Descending(m => m.SentAtUtc).Descending(m => m.MailId),
                new CreateIndexOptions { Name = "mail_user_latest" }) });
        await ledger.Indexes.CreateOneAsync(new CreateIndexModel<CurrencyTransaction>(
            Builders<CurrencyTransaction>.IndexKeys.Ascending(t => t.UserId).Ascending(t => t.Source).Ascending(t => t.RequestId).Ascending(t => t.EntryIndex),
            new CreateIndexOptions<CurrencyTransaction> { Name = "mail_ledger_unique", Unique = true,
                PartialFilterExpression = Builders<CurrencyTransaction>.Filter.Eq(t => t.Source, "MAILBOX") }));
    }
    private async Task RequireUser(ObjectId id, CancellationToken ct)
    {
        var user = await users.Find(u => u.Id == id).FirstOrDefaultAsync(ct);
        if (user == null) throw new MailboxException("USER_NOT_FOUND");
        if (!user.IsActive) throw new MailboxException("ACCOUNT_DISABLED");
    }
    public async Task<MailEnrollment> EnrollAsync(ObjectId id, CancellationToken ct = default)
    {
        await RequireUser(id, ct);
        if (!gift.Enabled) return new(false, null);
        if (!gift.IsValid()) throw new MailboxException("CAMPAIGN_CONFIGURATION_INVALID");
        var existing = await mail.Find(m => m.UserId == id && m.MailId == MailboxMessage.WelcomeId(id)).FirstOrDefaultAsync(ct);
        if (existing != null) return new(false, MailResponse.From(existing));
        var message = new MailboxMessage { MailId = MailboxMessage.WelcomeId(id), UserId = id, CampaignId = WelcomeGiftOptions.Campaign,
            Sender = gift.Sender, Title = gift.Title, Preview = gift.Preview, Body = gift.Body, SentAtUtc = DateTime.UtcNow,
            Attachments = new() { new() { Currency = "GOLDS", Amount = gift.Golds }, new() { Currency = "DIAMONDS", Amount = gift.Diamonds }, new() { Currency = "TICKETS", Amount = gift.Tickets } } };
        try
        {
            // Deterministic _id and unique user/campaign protect simultaneous first logins.
            await mail.InsertOneAsync(message, cancellationToken: ct);
            return new(true, MailResponse.From(message));
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        { return new(false, await GetAsync(id, message.MailId, ct)); }
    }
    public async Task<MailPage> ListAsync(ObjectId id, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        long skip = ((long)page - 1) * pageSize;
        if (page < 1 || pageSize is < 1 or > 50 || skip > int.MaxValue) throw new MailboxException("INVALID_PAGE");
        await RequireUser(id, ct);
        var own = Builders<MailboxMessage>.Filter.Eq(m => m.UserId, id);
        var now = DateTime.UtcNow;
        var total = await mail.CountDocumentsAsync(own, cancellationToken: ct);
        var unread = await mail.CountDocumentsAsync(own & Builders<MailboxMessage>.Filter.Eq(m => m.ReadAtUtc, null), cancellationToken: ct);
        var claimable = own & Builders<MailboxMessage>.Filter.Eq(m => m.ClaimedAtUtc, null) &
            (Builders<MailboxMessage>.Filter.Eq(m => m.ExpiresAtUtc, null) | Builders<MailboxMessage>.Filter.Gt(m => m.ExpiresAtUtc, now)) &
            Builders<MailboxMessage>.Filter.SizeGt(m => m.Attachments, 0);
        var unclaimed = await mail.CountDocumentsAsync(claimable, cancellationToken: ct);
        var items = await mail.Find(own).SortByDescending(m => m.SentAtUtc).ThenByDescending(m => m.MailId).Skip((int)skip).Limit(pageSize).ToListAsync(ct);
        return new(page, pageSize, total, unread, unclaimed, items.Select(MailResponse.From).ToList());
    }
    public async Task<MailResponse> GetAsync(ObjectId id, string mailId, CancellationToken ct = default)
    {
        await RequireUser(id, ct);
        return MailResponse.From(await mail.Find(m => m.UserId == id && m.MailId == mailId).FirstOrDefaultAsync(ct) ?? throw new MailboxException("MAIL_NOT_FOUND"));
    }
    public async Task<MailResponse> ReadAsync(ObjectId id, string mailId, CancellationToken ct = default)
    {
        await RequireUser(id, ct);
        await mail.UpdateOneAsync(m => m.UserId == id && m.MailId == mailId && m.ReadAtUtc == null,
            Builders<MailboxMessage>.Update.Set(m => m.ReadAtUtc, DateTime.UtcNow), cancellationToken: ct);
        return await GetAsync(id, mailId, ct);
    }
    public async Task<MailClaimReceipt> ClaimAsync(ObjectId id, string mailId, CancellationToken ct = default)
    {
        using var session = await client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync(async (s, token) =>
        {
            var user = await users.Find(s, u => u.Id == id).FirstOrDefaultAsync(token);
            if (user == null) throw new MailboxException("USER_NOT_FOUND");
            if (!user.IsActive) throw new MailboxException("ACCOUNT_DISABLED");
            var message = await mail.Find(s, m => m.UserId == id && m.MailId == mailId).FirstOrDefaultAsync(token) ?? throw new MailboxException("MAIL_NOT_FOUND");
            if (message.ClaimedAtUtc.HasValue) return Receipt(message);
            if (message.ExpiresAtUtc <= DateTime.UtcNow) throw new MailboxException("MAIL_EXPIRED");
            if (message.Attachments.Count == 0) throw new MailboxException("NO_REWARDS");
            if (message.Attachments.Any(a => a.Amount <= 0 || a.Amount > 1_000_000 || a.Currency is not ("GOLDS" or "DIAMONDS" or "TICKETS")) ||
                message.Attachments.GroupBy(a => a.Currency).Any(g => g.Count() > 1)) throw new MailboxException("INVALID_REWARDS");
            long Amount(string currency) => message.Attachments.Where(a => a.Currency == currency).Sum(a => a.Amount);
            // Reject overflow before Mongo's numeric increment can promote/corrupt wallet values.
            checked { _ = user.Wallet.Golds + Amount("GOLDS"); _ = user.Wallet.Diamonds + Amount("DIAMONDS"); _ = user.Wallet.Tickets + Amount("TICKETS"); }
            // BSON dates have millisecond precision; first response and durable replays must match exactly.
            var now = DateTime.UnixEpoch.AddMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var after = await users.FindOneAndUpdateAsync(s, Builders<User>.Filter.Eq(u => u.Id, id) & Builders<User>.Filter.Eq(u => u.IsActive, true),
                Builders<User>.Update.Inc(u => u.Wallet.Golds, Amount("GOLDS")).Inc(u => u.Wallet.Diamonds, Amount("DIAMONDS"))
                    .Inc(u => u.Wallet.Tickets, Amount("TICKETS")).Set(u => u.UpdatedAt, now),
                new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After }, token) ?? throw new MailboxException("ACCOUNT_DISABLED");
            message.ClaimedAtUtc = now; message.WalletAfter = after.Wallet;
            await mail.ReplaceOneAsync(s, m => m.UserId == id && m.MailId == mailId, message, cancellationToken: token);
            await ledger.InsertManyAsync(s, message.Attachments.Select((a, index) => new CurrencyTransaction {
                UserId = id, Currency = a.Currency, Amount = a.Amount, Source = "MAILBOX", RequestId = "mail:" + mailId, EntryIndex = index,
                BalanceAfter = a.Currency == "GOLDS" ? after.Wallet.Golds : a.Currency == "DIAMONDS" ? after.Wallet.Diamonds : after.Wallet.Tickets, CreatedAt = now }), cancellationToken: token);
            return Receipt(message);
        }, new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), ct);
    }
    private static MailClaimReceipt Receipt(MailboxMessage m) => new("mail:" + m.MailId, m.MailId,
        m.ClaimedAtUtc ?? throw new MailboxException("CLAIM_RECEIPT_INVALID"), m.WalletAfter ?? throw new MailboxException("CLAIM_RECEIPT_INVALID"));
}
