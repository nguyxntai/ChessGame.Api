namespace ChessGame.Api.Online;

public sealed record RoomListingSnapshot(string RoomId, string Code, GameSettings Settings,
    int MemberCount, int Capacity, DateTime ExpiresAt);