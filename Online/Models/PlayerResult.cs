namespace ChessGame.Api.Online;

public sealed class PlayerResult
{
    public string UserId { get; set; } = "";
    public int RatingBefore { get; set; }
    public int RatingAfter { get; set; }
    public int RatingChange { get; set; }
    public double? RatingDeviationAfter { get; set; }
    public int RatedGamesAfter { get; set; }
    // Nullable for pre-rank history: never synthesize historical badges from today's config.
    public string? TierBefore { get; set; }
    public string? TierAfter { get; set; }
    public bool? ProvisionalBefore { get; set; }
    public bool? ProvisionalAfter { get; set; }
    public string? RankChangeType { get; set; }
    public string? RankConfigVersion { get; set; }
    public bool? RatingApplied { get; set; }
    public string? RatingDecision { get; set; }
    public long Golds { get; set; }
    public long Diamonds { get; set; }
    public long Tickets { get; set; }
}
