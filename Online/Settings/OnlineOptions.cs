namespace ChessGame.Api.Online;

public sealed class OnlineOptions
{
    public int ProtocolVersion { get; set; } = 1;
    public int QueueSeconds { get; set; } = 180;
    public int ReadySeconds { get; set; } = 180;
    public int AcceptSeconds { get; set; } = 30;
    public int ReconnectSeconds { get; set; } = 60;
    public int DrawOfferSeconds { get; set; } = 30;
    public int RoomSeconds { get; set; } = 1800;
    public int InitialRatingRange { get; set; } = 100;
    public int RatingRangePerSecond { get; set; } = 2;
    public int MaximumRatingRange { get; set; } = 300;
    public int MaximumPairsPerSweep { get; set; } = 16;
    public int PoolsPerSweep { get; set; } = 8;
    public int CandidatesPerPool { get; set; } = 128;
    public int RecentOpponentSeconds { get; set; } = 600;
    public int RecentOpponentRelaxSeconds { get; set; } = 60;
    public int MaximumLatencyDifferenceMilliseconds { get; set; } = 150;
}
