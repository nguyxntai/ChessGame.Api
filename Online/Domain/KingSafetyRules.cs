namespace ChessButWeird.Domain
{
    public interface IBuffContextProvider
    {
        AramPieceContext GetContext(PieceState piece);
    }

    public readonly struct NoBuffs : IBuffContextProvider
    {
        public AramPieceContext GetContext(PieceState piece) => default;
    }

    /// <summary>A read-only hypothetical move. No temporary writes to the live board or allocations.</summary>
    public readonly struct MoveBoardView<TBoard> : IReadOnlyBoard where TBoard : IReadOnlyBoard
    {
        private readonly TBoard board;
        private readonly Square from, to, capture, rookFrom, rookTo;
        private readonly PieceState moved, rook;
        private readonly bool explosion;
        private readonly bool legacyExplosion;
        private readonly bool removeMoving;
        public MoveBoardView(TBoard board, Square from, Square to, Square capture, Square rookFrom, Square rookTo, bool explosion, bool legacyExplosion = false, bool removeMoving = false)
        {
            this.board = board; this.from = from; this.to = to; this.capture = capture;
            this.rookFrom = rookFrom; this.rookTo = rookTo; this.explosion = explosion;
            this.legacyExplosion = legacyExplosion;
            this.removeMoving = removeMoving;
            moved = board.GetPiece(from).Moved();
            PieceState originalRook = board.GetPiece(rookFrom);
            rook = originalRook.IsEmpty ? default : originalRook.Moved();
        }
        public PieceState GetPiece(Square square)
        {
            if (!square.IsValid) return default;
            if (square == to) return removeMoving || (explosion && SuicideBomberBuff.IsVictim(moved, moved.Id, to, to, legacyExplosion)) ? default : moved;
            if (rookTo.IsValid && square == rookTo) return rook;
            if (square == from || square == capture || (rookFrom.IsValid && square == rookFrom)) return default;
            PieceState piece = board.GetPiece(square);
            return explosion && SuicideBomberBuff.IsVictim(piece, moved.Id, to, square, legacyExplosion) ? default : piece;
        }
    }

    public static class KingSafetyRules
    {
        public static bool IsInCheck<TBoard, TBuffs>(TBoard board, Team team, TBuffs buffs)
            where TBoard : IReadOnlyBoard where TBuffs : IBuffContextProvider
            => IsInCheck(board, team, buffs, AramRules.BuiltIn);

        public static bool IsInCheck<TBoard, TBuffs>(TBoard board, Team team, TBuffs buffs, AramRules rules)
            where TBoard : IReadOnlyBoard where TBuffs : IBuffContextProvider
        {
            if (rules == null) throw new System.ArgumentNullException(nameof(rules));
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            {
                Square square = new Square(x, y);
                PieceState piece = board.GetPiece(square);
                if (!piece.IsEmpty && piece.Team == team && piece.Kind == PieceKind.King)
                    return IsAttacked(board, square, ClassicRules.Opponent(team), buffs, rules);
            }
            return true;
        }

        public static bool IsAttacked<TBoard, TBuffs>(TBoard board, Square square, Team attacker, TBuffs buffs)
            where TBoard : IReadOnlyBoard where TBuffs : IBuffContextProvider
            => IsAttacked(board, square, attacker, buffs, AramRules.BuiltIn);

        public static bool IsAttacked<TBoard, TBuffs>(TBoard board, Square square, Team attacker, TBuffs buffs, AramRules rules)
            where TBoard : IReadOnlyBoard where TBuffs : IBuffContextProvider
        {
            if (rules == null) throw new System.ArgumentNullException(nameof(rules));
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            {
                Square from = new Square(x, y);
                PieceState piece = board.GetPiece(from);
                if (piece.IsEmpty || piece.Team != attacker) continue;
                AramPieceContext context = buffs.GetContext(piece);
                if ((!AramRules.SuppressesStandardMovement(context) && MovementRules.Attacks(board, piece, from, square)) ||
                    rules.Allows(board, piece, from, square, context, attack: true)) return true;
            }
            return false;
        }
    }
}
