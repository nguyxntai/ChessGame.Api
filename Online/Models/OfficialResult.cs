namespace ChessGame.Api.Online;

public sealed class OfficialResult
{
    public string Outcome { get; set; } = "Draw";
    public string? WinnerId { get; set; }
    public string Reason { get; set; } = "";
    public DateTime FinishedAt { get; set; }
    public List<PlayerResult> Players { get; set; } = new();
}
