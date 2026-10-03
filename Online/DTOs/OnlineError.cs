namespace ChessGame.Api.Online;

public sealed record OnlineError(string Code, string Message, long? StateVersion = null);
