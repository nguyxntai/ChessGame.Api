namespace ChessGame.Api.Online;

public sealed record AbilityCommand
{
    public string Kind { get; init; } = "";
    public int? BuffId { get; init; }
    public int? PieceId { get; init; }
    public int? TargetPieceId { get; init; }
    public string? Target { get; init; }
    public List<int> PieceIds { get; init; } = new();
    public List<FormationPlacement> Formation { get; init; } = new();
    public double? Yaw { get; init; }
    public double? Pitch { get; init; }
}
