namespace ChessGame.Api.DTOs.Leaderboard;

public sealed record LeaderboardEntry(long? Rank, string UserId, string Username,
    string DisplayName, string? AvatarId, int Elo, string Mode = "Classic", int RatedGames = 0, double? Deviation = null, bool Provisional = false);
