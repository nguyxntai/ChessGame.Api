namespace ChessGame.Api.Online;

public sealed class DrawOffer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
}
