namespace ChessGame.Api.Online;

public sealed record GameSettings
{
    private readonly string mode = "Classic";
    public string Mode { get => mode; init => mode = ModeNormalization.Normalize(value); }
    public string Region { get; init; } = "VN";
    public int InitialSeconds { get; init; } = 600;
    public int IncrementSeconds { get; init; } = 5;
    public int ProtocolVersion { get; init; } = 1;
}
