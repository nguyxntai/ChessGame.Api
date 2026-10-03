namespace ChessGame.Api.Online;

public sealed record GameSettings
{
    public string Mode { get; init; } = "Classic";
    public string Region { get; init; } = "VN";
    public int InitialSeconds { get; init; } = 600;
    public int IncrementSeconds { get; init; } = 5;
    public int ProtocolVersion { get; init; } = 1;
}
