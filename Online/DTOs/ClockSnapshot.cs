namespace ChessGame.Api.Online;

public sealed record ClockSnapshot(double WhiteMilliseconds, double BlackMilliseconds, string? RunningColor,
    DateTime ServerTime);
