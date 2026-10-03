namespace ChessGame.Api.Online;

public sealed record EventReplay(long LatestSequence, List<PublicEvent> Events);
