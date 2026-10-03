namespace ChessGame.Api.Online;

public sealed record MatchSnapshot(string MatchId, string Status, GameSettings Settings, long StateVersion,
    long EventSequence, DateTime ServerTime, string Turn, List<PieceSnapshot> Board, string? Fen,
    string CastlingRights, string EnPassantTarget, int HalfMoveClock, int FullMoveNumber,
    ClockSnapshot Clocks, List<PlayerSnapshot> Players, DateTime ReadyDeadline, DrawOffer? DrawOffer,
    AramState? Aram, OfficialResult? Result, string? RematchId);
