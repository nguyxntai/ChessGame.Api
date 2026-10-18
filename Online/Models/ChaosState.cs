namespace ChessGame.Api.Online;

public sealed class ChaosState
{
    public int RulesVersion { get; set; } = 2;
    public string Phase { get; set; } = "Draft";
    public DateTime SetupDeadline { get; set; }
    public List<ChaosSide> Sides { get; set; } = new();
    public int CollapsedFiles { get; set; }
    public int NextPieceId { get; set; } = 100;
    public List<ChaosMarker> Mines { get; set; } = new();
    public List<ChaosMarker> Crates { get; set; } = new();
    public List<int> LootRounds { get; set; } = new();
    public List<ChaosDeployment> Deployments { get; set; } = new();
    public int? ForcedPawn { get; set; }
    public string? EscapeTeam { get; set; }
    public RifleSession? Rifle { get; set; }
    // Geometry uses a1=(0,0,0), file=+X, rank=+Z, one tile=1 unit.
    public List<PieceHitbox> Hitboxes { get; set; } = new();
}
