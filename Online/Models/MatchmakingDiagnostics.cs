namespace ChessGame.Api.Online;

public sealed class MatchmakingDiagnostics
{
    public string Policy { get; set; } = "bounded-blossom-v1";
    public int RatingGap { get; set; }
    public double MaximumWaitSeconds { get; set; }
    public bool BothProvisional { get; set; }
    public bool MixedPlacement { get; set; }
    public double? WhiteRoundTripMilliseconds { get; set; }
    public double? BlackRoundTripMilliseconds { get; set; }
}
