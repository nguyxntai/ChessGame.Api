namespace ChessGame.Api.Online;

public sealed record TicketSnapshot(string TicketId, string Status, GameSettings Settings, DateTime CreatedAt,
    DateTime ExpiresAt, string? MatchId);
