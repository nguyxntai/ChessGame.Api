namespace ChessGame.Api.Online;

public sealed class RifleSession
{
    public string Team { get; set; } = "";
    public DateTime Deadline { get; set; }
    public double OriginX { get; set; }
    public double OriginY { get; set; }
    public double OriginZ { get; set; }
}
