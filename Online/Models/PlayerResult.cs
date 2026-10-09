namespace ChessGame.Api.Online;

public sealed class PlayerResult
{
    public string UserId { get; set; } = "";
    public int RatingBefore { get; set; }
    public int RatingAfter { get; set; }
    public int RatingChange { get; set; }
    public double? RatingDeviationAfter { get; set; }
    public int RatedGamesAfter { get; set; }
    public long Golds { get; set; }
    public long Diamonds { get; set; }
    public long Tickets { get; set; }
}
