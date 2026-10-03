namespace ChessGame.Api.Online;

public sealed class PieceHitbox
{
    public string Kind { get; set; } = "";
    public double HalfWidth { get; set; }
    public double Height { get; set; }
    public double HalfDepth { get; set; }
}
