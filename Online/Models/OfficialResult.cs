namespace ChessGame.Api.Online;

public sealed class OfficialResult
{
    public string Outcome { get; set; } = "Draw";
    public string? WinnerId { get; set; }
    public string Reason { get; set; } = "";
    private string ratingMode = "";
    public string RatingMode { get => ratingMode; set => ratingMode = ModeNormalization.Normalize(value); }
    public string RatingPolicy { get; set; } = "";
    public bool RatingApplied { get; set; }
    public bool RewardsApplied { get; set; }
    public bool StatsApplied { get; set; }
    public string RatingDecision { get; set; } = "Legacy";
    public string RewardDecision { get; set; } = "Legacy";
    public DateTime FinishedAt { get; set; }
    public List<PlayerResult> Players { get; set; } = new();
}
