using System;

namespace ChessButWeird.Domain.Chaos35
{
    /// <summary>October 2026 document rules. A ply is one committed move by either side;
    /// setup, purchases, deployments and rifle shots do not advance either clock.</summary>
    public static class AramBalanceRules
    {
        public const int SniperLifetime = 4, PlagueLifetime = 4, CannonLifetime = 10;
        public const int DoppelgangerCooldown = 4, HidingKingCooldown = 10, PeaceGoal = 14;
        public const int GamblingMaximumUses = 3, PoorKingDeadline = 25, QueenRefundTurn = 10;
        public const float GamblingChance = .25f;
        public static int ExpiryAfterCapture(int beforePly, int nextPlies) => checked(beforePly + 1 + nextPlies);
        public static float BetrayalChance(int completedChecks) => Math.Min(1f, .1f + .05f * Math.Max(0, completedChecks));
        public static int CapturePoints(PieceKind kind) => kind == PieceKind.Pawn ? 1 :
            kind == PieceKind.Knight || kind == PieceKind.Bishop ? 3 : kind == PieceKind.Rook ? 5 : kind == PieceKind.Queen ? 9 : 0;
        public static int PurchasePrice(PieceKind kind) => kind == PieceKind.Pawn ? 2 :
            kind == PieceKind.Knight || kind == PieceKind.Bishop ? 4 : kind == PieceKind.Rook ? 5 : kind == PieceKind.Queen ? 10 : 0;
        // The document explicitly blocks purchases while in debt: a solvent player
        // may make one purchase on credit, then must earn enough points to buy again.
        public static bool CanPurchase(int balance, bool boughtThisTurn, PieceKind kind) =>
            balance >= 0 && !boughtThisTurn && PurchasePrice(kind) > 0;
    }
}
