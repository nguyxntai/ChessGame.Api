namespace ChessGame.Api.Online;

public sealed record RoomSnapshot(string RoomId, string Code, string OwnerId, List<string> Members,
    GameSettings Settings, string Status, string? MatchId, DateTime ExpiresAt);
