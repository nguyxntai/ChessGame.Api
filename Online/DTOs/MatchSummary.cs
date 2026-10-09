namespace ChessGame.Api.Online;

public sealed record MatchSummary(string MatchId, string Status, GameSettings Settings, List<PlayerSnapshot> Players,
    DateTime CreatedAt, DateTime? StartedAt, DateTime? FinishedAt, OfficialResult? Result, bool Rated = false);
