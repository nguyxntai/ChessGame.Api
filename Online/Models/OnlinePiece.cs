namespace ChessGame.Api.Online;

public sealed class OnlinePiece
{
    public int Id { get; set; }
    public string Kind { get; set; } = "Pawn";
    public string Team { get; set; } = "White";
    public string Square { get; set; } = "a1";
    public bool HasMoved { get; set; }
    public int Forward { get; set; }
}
