namespace ChessGame.Api.Online;

public sealed class ChaosDeployment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Team { get; set; } = "";
    public string Kind { get; set; } = "Pawn";
    public string? Center { get; set; }
    public string Action { get; set; } = "Deploy";
    public int Ranks { get; set; } = 4;
    public int? PieceId { get; set; }
    public int Remaining { get; set; } = 1;
    public ChaosPieceEffect? Traits { get; set; }
    public int? VisualBuffId { get; set; }
    public string? VisualFrom { get; set; }
    public string? VisualOutcome { get; set; }
}
