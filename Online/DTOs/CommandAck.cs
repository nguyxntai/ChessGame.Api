namespace ChessGame.Api.Online;

public sealed record CommandAck(string CommandId, bool Accepted, string? ErrorCode, long StateVersion,
    long EventSequence, DateTime ServerTime, bool Replayed = false);
