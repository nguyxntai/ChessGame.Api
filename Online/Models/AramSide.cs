namespace ChessGame.Api.Online;

public sealed class AramSide
{
    public string Team { get; set; } = "";
    public List<int> DraftOptions { get; set; } = new();
    public int? BuffId { get; set; }
    public bool SetupComplete { get; set; }
    public bool FormationComplete { get; set; }
    public List<FormationPlacement> OriginalFormation { get; set; } = new();
    public DateTime? FormationDeadline { get; set; }
    public List<int> CommandantPawns { get; set; } = new();
    public int? SwappedKnight { get; set; }
    public int? SwappedBishop { get; set; }
    public int? OriginalQueen { get; set; }
    public bool BomberUsed { get; set; }
    public int TeleportUses { get; set; }
    public int TeleportReady { get; set; }
    public int CompletedTurns { get; set; }
    public int Tickets { get; set; }
    public int RollsThisTurn { get; set; }
    public int ExtraUses { get; set; }
    public int EscapeUses { get; set; }
    public int SwapReady { get; set; } = 5;
    public bool SwapActive { get; set; } = true;
    public int HidingReady { get; set; }
    public int RifleReady { get; set; }
    public bool PeaceFailed { get; set; }
    public bool PeaceUsed { get; set; }
    public bool SubstituteUsed { get; set; }
    public List<AramPieceEffect> Effects { get; set; } = new();
}
