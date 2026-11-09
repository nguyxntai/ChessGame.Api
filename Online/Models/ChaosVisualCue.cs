namespace ChessGame.Api.Online;

// Confirmed presentation facts only. No legal-move, damage, random seed or hidden target data.
public sealed class ChaosVisualCue
{
    public long Sequence { get; set; }
    public int BuffId { get; set; }
    public string Phase { get; set; } = "Resolve";
    public string Outcome { get; set; } = "Resolved";
    public int ActorId { get; set; }
    public string Team { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public List<string> Tiles { get; set; } = new();
    public bool OwnerOnly { get; set; }
    public int? Amount { get; set; }
    public List<ChaosVisualChange> Changes { get; set; } = new();
    // Optional resolved rifle ray, in canonical board coordinates, never client supplied.
    public double[]? Origin { get; set; }
    public double[]? Impact { get; set; }
}

public sealed record ChaosVisualStatus(int BuffId, int ActorId, string Team, int? Remaining = null, int? Percent = null);
public sealed record ChaosBoardVisual(int BuffId,string Square,string Team,string Kind,int? Remaining=null);
public sealed record ChaosVisualChange(int ActorId,string Team,string From,string To,string? FromKind,string? ToKind);
public sealed record ChaosVisualHud(int BuffId,string Team,string Text,int Current=0,int Target=0,int Mask=0,string Unit="");
