namespace ChessGame.Api.Online;

public sealed record PublicEvent(string EventId, string Type, string? MatchId, long Sequence,
    long StateVersion, DateTime ServerTime, object? Payload);
