namespace ChessGame.Api.Online;

public sealed class OnlineOptions
{
    public int ProtocolVersion { get; set; } = 1;
    public int QueueSeconds { get; set; } = 180;
    public int ReadySeconds { get; set; } = 180;
    public int ReconnectSeconds { get; set; } = 60;
    public int DrawOfferSeconds { get; set; } = 30;
    public int RoomSeconds { get; set; } = 1800;
    public int InitialRatingRange { get; set; } = 150;
    public int RatingRangePerSecond { get; set; } = 10;
    public int EloK { get; set; } = 32;
}
