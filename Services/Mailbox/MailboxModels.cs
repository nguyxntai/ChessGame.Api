using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using ChessGame.Api.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ChessGame.Api.Services.Mailbox;

public sealed class WelcomeGiftOptions
{
    public const string Campaign = "WELCOME_2026_V1";
    public bool Enabled { get; set; } = true;
    public long Golds { get; set; } = 2000;
    public long Diamonds { get; set; } = 100;
    public long Tickets { get; set; } = 3;
    public string Sender { get; set; } = "Chess But Weird Team";
    public string Title { get; set; } = "Welcome to Chess But Weird!";
    public string Preview { get; set; } = "A small gift from the team to help you begin your journey.";
    public string Body { get; set; } = "";
    public bool IsValid() => Golds > 0 && Golds <= 1_000_000 && Diamonds > 0 && Diamonds <= 1_000_000 && Tickets > 0 && Tickets <= 1_000_000 && !string.IsNullOrWhiteSpace(Body);
}

public sealed class MailAttachment
{
    public string Currency { get; set; } = "";
    public long Amount { get; set; }
}

[BsonIgnoreExtraElements]
public sealed class MailboxMessage
{
    [BsonId] public string MailId { get; set; } = "";
    public ObjectId UserId { get; set; }
    public string? CampaignId { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public string Category { get; set; } = "Reward";
    public string Sender { get; set; } = "";
    public string Title { get; set; } = "";
    public string Preview { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime SentAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public DateTime? ClaimedAtUtc { get; set; }
    public List<MailAttachment> Attachments { get; set; } = new();
    public PlayerWallet? WalletAfter { get; set; }

    public static string WelcomeId(ObjectId userId) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}:{WelcomeGiftOptions.Campaign}"))).ToLowerInvariant();
}

public sealed record MailResponse(string MailId, string UserId, string? CampaignId, int SchemaVersion,
    string Category, string Sender, string Title, string Preview, string Body, DateTime SentAtUtc,
    DateTime? ExpiresAtUtc, DateTime? ReadAtUtc, DateTime? ClaimedAtUtc, string Status, List<MailAttachment> Attachments)
{
    public static MailResponse From(MailboxMessage m) => new(m.MailId, m.UserId.ToString(), m.CampaignId, m.SchemaVersion,
        m.Category, m.Sender, m.Title, m.Preview, m.Body, m.SentAtUtc, m.ExpiresAtUtc, m.ReadAtUtc, m.ClaimedAtUtc,
        m.ClaimedAtUtc.HasValue ? "Claimed" : m.ExpiresAtUtc <= DateTime.UtcNow ? "Expired" : m.Attachments.Count > 0 ? "Claimable" : "Message", m.Attachments);
}
public sealed record MailPage(int Page, int PageSize, long Total, long UnreadCount, long UnclaimedCount, List<MailResponse> Items);
public sealed record MailEnrollment(bool Created, MailResponse? Mail);
public sealed record MailClaimReceipt(string ReceiptId, string MailId, DateTime ClaimedAtUtc, PlayerWallet Wallet);
public sealed record MailClaimAllReceipt(int ClaimedCount, List<MailAttachment> Rewards, PlayerWallet Wallet);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class MailClaimRequest { }
public sealed class MailboxException(string code) : Exception(code) { public string Code { get; } = code; }
