namespace ChessGame.Api.Online;

public sealed class AramPieceEffect
{
    public int PieceId { get; set; }
    public bool Bloodthirsty { get; set; }
    public bool Decoy { get; set; }
    public bool Unstable { get; set; }
    public int? CannonUntil { get; set; }
    public int? SniperUntil { get; set; }
    public int? InfectedUntil { get; set; }
    public bool Passenger { get; set; }
}
