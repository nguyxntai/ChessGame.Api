using System;

namespace ChessButWeird.Domain
{
    [Flags]
    public enum AramBuffs
    {
        None = 0, CommandantPawn = 1, StrongFortress = 2, FreestyleLeap = 4,
        Doppelganger = 8, SuicideBomber = 16, FlyingThunderGod = 32,
        NobleSacrifice = 1 << 6, AbsoluteSniper = 1 << 7, GamblingLeadsToMisery = 1 << 8,
        LootBox = 1 << 9, PeaceTShirt = 1 << 10, RiseOfPawn = 1 << 11,
        MobileFortress = 1 << 12, HidingKing = 1 << 13, GachaBanner = 1 << 14,
        SubstituteNinjutsu = 1 << 15, HighTechEra = 1 << 16, QueensBetrayal = 1 << 17,
        DefinitionOfAram = 1 << 18, RngFiesta = 1 << 19, IFrameRoll = 1 << 20,
        GhostArmy = 1 << 21, PlagueTown = 1 << 22, CustomizeArmy = 1 << 23,
        PawnsRevolution = 1 << 24, OneManArmy = 1 << 25
    }

    /// <summary>Per-piece projection of match-owned buff state. No scene object references.</summary>
    public readonly struct AramPieceContext
    {
        public readonly AramBuffs Buffs;
        public readonly bool IsCommandantPawn, IsSwappedKnight, IsSwappedBishop, IsOriginalQueen, CanTeleport;
        public readonly bool Bloodthirsty, CannonReady, IsDecoy;
        public AramPieceContext(AramBuffs buffs, bool commandant = false, bool swappedKnight = false,
            bool swappedBishop = false, bool originalQueen = false, bool canTeleport = false,
            bool bloodthirsty = false, bool cannonReady = false, bool decoy = false)
        { Buffs = buffs; IsCommandantPawn = commandant; IsSwappedKnight = swappedKnight;
          IsSwappedBishop = swappedBishop; IsOriginalQueen = originalQueen; CanTeleport = canTeleport;
          Bloodthirsty = bloodthirsty; CannonReady = cannonReady; IsDecoy = decoy; }
        public bool Has(AramBuffs buff) => (Buffs & buff) != 0;
    }

    public interface IAramMovementBuff
    {
        bool Allows<TBoard>(TBoard board, PieceState piece, Square from, Square to, AramPieceContext context, bool attack)
            where TBoard : IReadOnlyBoard;
    }

    public sealed class CommandantPawnBuff : IAramMovementBuff
    {
        public bool Allows<TBoard>(TBoard board, PieceState piece, Square from, Square to, AramPieceContext c, bool attack)
            where TBoard : IReadOnlyBoard => !attack && c.Has(AramBuffs.CommandantPawn) && c.IsCommandantPawn &&
                piece.Kind == PieceKind.Pawn && to.File == from.File && to.Rank - from.Rank == piece.Forward * 2 &&
                board.GetPiece(new Square(from.File, from.Rank + piece.Forward)).IsEmpty && board.GetPiece(to).IsEmpty;
    }

    public sealed class FreestyleLeapBuff : IAramMovementBuff
    {
        public bool Allows<TBoard>(TBoard board, PieceState piece, Square from, Square to, AramPieceContext c, bool attack)
            where TBoard : IReadOnlyBoard => c.Has(AramBuffs.FreestyleLeap) && piece.Kind == PieceKind.Knight &&
                Math.Abs(to.File - from.File) <= 2 && Math.Abs(to.Rank - from.Rank) <= 2 &&
                !(Math.Abs(to.File - from.File) == 2 && Math.Abs(to.Rank - from.Rank) == 2);
    }

    public sealed class DoppelgangerBuff : IAramMovementBuff
    {
        public bool Allows<TBoard>(TBoard board, PieceState piece, Square from, Square to, AramPieceContext c, bool attack)
            where TBoard : IReadOnlyBoard
        {
            if (!c.Has(AramBuffs.Doppelganger)) return false;
            if (c.IsSwappedKnight) return Math.Abs(to.File - from.File) == Math.Abs(to.Rank - from.Rank) && MovementRules.IsPathClear(board, from, to);
            return c.IsSwappedBishop && MovementRules.IsKnightPattern(from, to);
        }
    }

    public sealed class FlyingThunderGodBuff : IAramMovementBuff
    {
        // ARAM-V1 wire contract. Keep these values aligned with the current Spring backend.
        public const int MaximumUses = 5;
        public const int CooldownTurns = 5;
        public bool Allows<TBoard>(TBoard board, PieceState piece, Square from, Square to, AramPieceContext c, bool attack)
            where TBoard : IReadOnlyBoard => !attack && c.Has(AramBuffs.FlyingThunderGod) && c.IsOriginalQueen &&
                c.CanTeleport && board.GetPiece(to).IsEmpty;
    }

    /// <summary>Movement extension registry. Simulation and live queries call the same policies.</summary>
    public sealed class AramRules
    {
        public static readonly AramRules BuiltIn = new AramRules(new CommandantPawnBuff(), new FreestyleLeapBuff(),
            new DoppelgangerBuff(), new FlyingThunderGodBuff(), new ExtendedAramMovement());
        public static readonly AramRules LegacyV1 = new AramRules(new CommandantPawnBuff(), new LegacyFreestyleLeapBuff(),
            new DoppelgangerBuff(), new FlyingThunderGodBuff());
        private readonly IAramMovementBuff[] movement;
        public AramRules(params IAramMovementBuff[] movement)
        {
            if (movement == null) throw new ArgumentNullException(nameof(movement));
            this.movement = (IAramMovementBuff[])movement.Clone();
            foreach (var buff in this.movement) if (buff == null) throw new ArgumentException("Null buff.", nameof(movement));
        }
        public bool Allows<TBoard>(TBoard board, PieceState piece, Square from, Square to, AramPieceContext context, bool attack = false)
            where TBoard : IReadOnlyBoard
        {
            if (piece.IsEmpty || !from.IsValid || !to.IsValid || from == to) return false;
            PieceState target = board.GetPiece(to);
            if (!attack && !target.IsEmpty && target.Team == piece.Team &&
                !(context.Has(AramBuffs.NobleSacrifice) && piece.Kind == PieceKind.Pawn && target.Kind == PieceKind.Pawn)) return false;
            foreach (var buff in movement) if (buff.Allows(board, piece, from, to, context, attack)) return true;
            return false;
        }
        public static bool SuppressesStandardMovement(AramPieceContext context) =>
            context.IsDecoy || (context.Has(AramBuffs.Doppelganger) && (context.IsSwappedKnight || context.IsSwappedBishop));
    }

    public static class StrongFortressBuff
    {
        public static bool WaivesCastleAttackChecks(AramBuffs buffs) => (buffs & AramBuffs.StrongFortress) != 0;
    }

    public static class SuicideBomberBuff
    {
        public static bool IsVictim(PieceState victim, int capturingPieceId, Square center, Square square, bool legacy = false) =>
            !victim.IsEmpty && victim.Kind != PieceKind.King && (!legacy || (victim.Id != capturingPieceId && center != square)) &&
            Math.Abs(center.File - square.File) <= 1 && Math.Abs(center.Rank - square.Rank) <= 1;
    }

    public sealed class LegacyFreestyleLeapBuff : IAramMovementBuff
    {
        public bool Allows<TBoard>(TBoard board, PieceState piece, Square from, Square to, AramPieceContext c, bool attack)
            where TBoard : IReadOnlyBoard => c.Has(AramBuffs.FreestyleLeap) && piece.Kind == PieceKind.Knight &&
                (MovementRules.IsKnightPattern(from, to) || (Math.Abs(to.File-from.File)==2 && Math.Abs(to.Rank-from.Rank)==2));
    }

    public sealed class ExtendedAramMovement : IAramMovementBuff
    {
        public bool Allows<TBoard>(TBoard board, PieceState piece, Square from, Square to, AramPieceContext c, bool attack)
            where TBoard : IReadOnlyBoard
        {
            int dx = to.File-from.File, dy = to.Rank-from.Rank;
            var target = board.GetPiece(to);
            if(c.IsDecoy)return Math.Abs(dx)<=1 && Math.Abs(dy)<=1;
            if (piece.Kind == PieceKind.Pawn)
            {
                if (c.Bloodthirsty && dy == piece.Forward && Math.Abs(dx)<=1) return true;
                if (!attack && (c.Bloodthirsty || c.Has(AramBuffs.RiseOfPawn)) && dx==0 && dy==-piece.Forward && target.IsEmpty) return true;
                if (!attack && c.Has(AramBuffs.NobleSacrifice) && target.Kind==PieceKind.Pawn && !target.IsEmpty &&
                    target.Team==piece.Team && Math.Abs(dx)==1 && dy==piece.Forward) return true;
            }
            if (c.Has(AramBuffs.OneManArmy) && piece.Kind==PieceKind.King &&
                ((Math.Abs(dx)==2 && dy==0)||(dx==0 && Math.Abs(dy)==2)))
                return MovementRules.IsPathClear(board, from, to);
            bool straight = dx==0 || dy==0, diagonal = Math.Abs(dx)==Math.Abs(dy);
            if (c.CannonReady && piece.Kind==PieceKind.Rook && straight && (!target.IsEmpty || attack))
            {
                int blockers=0;
                for (var p=new Square(from.File+Math.Sign(dx),from.Rank+Math.Sign(dy)); p!=to;
                     p=new Square(p.File+Math.Sign(dx),p.Rank+Math.Sign(dy)))
                    if (!board.GetPiece(p).IsEmpty) blockers++;
                if (blockers==1) return true;
            }
            // Phasing is a movement/capture extension, never a check through a blocker.
            if (!attack && c.Has(AramBuffs.GhostArmy) &&
                ((piece.Kind==PieceKind.Rook && straight)||(piece.Kind==PieceKind.Bishop && diagonal)||
                 (piece.Kind==PieceKind.Queen && (straight||diagonal)) ||
                 (piece.Kind==PieceKind.Pawn && dx==0 && dy==piece.Forward*2 && !piece.HasMoved && target.IsEmpty)))
            {
                for (var p=new Square(from.File+Math.Sign(dx),from.Rank+Math.Sign(dy)); p!=to;
                     p=new Square(p.File+Math.Sign(dx),p.Rank+Math.Sign(dy)))
                {
                    var blocker=board.GetPiece(p);
                    if (!blocker.IsEmpty && blocker.Team!=piece.Team) return false;
                }
                return true;
            }
            return false;
        }
    }
}
