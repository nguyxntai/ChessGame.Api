namespace ChessGame.Api.Online;

/// <summary>Enemy pieces directly captured, with their kind/team at capture time.</summary>
public sealed class CapturedPiece
{
    public int PieceId { get; set; }
    public string Kind { get; set; } = "";
    public string Team { get; set; } = "";
    public string CapturedBy { get; set; } = "";
}