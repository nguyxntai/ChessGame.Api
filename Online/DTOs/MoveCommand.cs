namespace ChessGame.Api.Online;

public sealed record MoveCommand(string From, string To, string? Promotion = null);
