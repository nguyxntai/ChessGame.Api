namespace ChessGame.Api.Online;

public sealed record SubscriptionSnapshot(MatchSnapshot State, long ResumeAfterSequence);
