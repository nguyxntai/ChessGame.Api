namespace ChessGame.Api.Online;

public static class SettlementRules
{
    public static bool HasRewardParticipation(long firstMoves, long secondMoves, DateTime? startedAt, DateTime finishedAt) =>
        firstMoves >= 5 && secondMoves >= 5 && startedAt is { } start && finishedAt - start >= TimeSpan.FromSeconds(60);
}
