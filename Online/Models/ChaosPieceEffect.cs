namespace ChessGame.Api.Online;

public sealed class ChaosPieceEffect
{
    public int PieceId { get; set; }
    public bool Bloodthirsty { get; set; }
    public bool Decoy { get; set; }
    public bool Unstable { get; set; }
    public int? CannonUntil { get; set; }
    public int? SniperUntil { get; set; }
    public int? InfectedUntil { get; set; }
    public bool Passenger { get; set; }
    public int PassengerReady { get; set; }
    public ChaosPieceEffect? PassengerTraits { get; set; }
    public bool Commandant { get; set; }
    public bool QueenMastery { get; set; }
    public bool TrapPawn { get; set; }
    public bool PromotedQueen { get; set; }
    public int BetrayalChecks { get; set; }
}
