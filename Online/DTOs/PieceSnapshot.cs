namespace ChessGame.Api.Online;

public sealed record PieceSnapshot(int Id, string Kind, string Team, string Square, bool HasMoved, int Forward);
