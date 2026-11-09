namespace ChessGame.Api.Online;

public sealed class ChaosState
{
    public int RulesVersion { get; set; } = 2;
    public int CombinedPlies { get; set; }
    public int CollapseSecondAt { get; set; } = -1;
    public string? LastPositionKey { get; set; }
    public int LastRecordedPly { get; set; } = -1;
    [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
    public List<ChaosLegalMove>? LegalMoves { get; set; }
    // Added by the snapshot projection, never stored with the match.
    [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
    public IReadOnlyList<ChaosBuffDefinition> BuffDefinitions { get; set; } = Array.Empty<ChaosBuffDefinition>();
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
    public long VisualSequence { get; set; }
    // Bounded durable tail: snapshots/replays can recover confirmed cues without a second transport.
    public List<ChaosVisualCue> VisualCues { get; set; } = new();
    [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
    public List<ChaosVisualStatus> VisualStatuses { get; set; } = new();
    [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
    public List<ChaosBoardVisual> VisualBoard { get; set; } = new();
    [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
    public List<ChaosVisualHud> VisualHud { get; set; } = new();
    [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
    public string? LegalMoveTeam { get; set; }
}

public sealed record ChaosLegalMove(string From, string To, string? Promotion = null);
