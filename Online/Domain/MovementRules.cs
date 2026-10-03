using System;

namespace ChessButWeird.Domain
{
    /// <summary>Ordinary movement only; castling, en passant and king safety belong to the match rules.</summary>
    public static class MovementRules
    {
        public static bool IsLegalPattern<TBoard>(TBoard board, PieceState piece, Square from, Square to)
            where TBoard : IReadOnlyBoard
        {
            if (piece.IsEmpty || !from.IsValid || !to.IsValid || from == to) return false;
            PieceState target = board.GetPiece(to);
            if (!target.IsEmpty && target.Team == piece.Team) return false;
            int x = Math.Abs(to.File - from.File), y = Math.Abs(to.Rank - from.Rank);
            switch (piece.Kind)
            {
                case PieceKind.King: return x <= 1 && y <= 1;
                case PieceKind.Knight: return IsKnightPattern(from, to);
                case PieceKind.Bishop: return x == y && IsPathClear(board, from, to);
                case PieceKind.Rook: return (x == 0 || y == 0) && IsPathClear(board, from, to);
                case PieceKind.Queen: return (x == y || x == 0 || y == 0) && IsPathClear(board, from, to);
                case PieceKind.Pawn:
                    int delta = to.Rank - from.Rank;
                    if (x == 1 && delta == piece.Forward) return !target.IsEmpty;
                    if (x != 0 || !target.IsEmpty) return false;
                    if (delta == piece.Forward) return true;
                    return !piece.HasMoved && from.Rank == (piece.Forward > 0 ? 1 : 6) &&
                        delta == 2 * piece.Forward && board.GetPiece(new Square(from.File, from.Rank + piece.Forward)).IsEmpty;
                default: return false;
            }
        }

        public static bool Attacks<TBoard>(TBoard board, PieceState piece, Square from, Square to)
            where TBoard : IReadOnlyBoard
        {
            if (piece.IsEmpty || !from.IsValid || !to.IsValid || from == to) return false;
            if (piece.Kind == PieceKind.Pawn)
                return Math.Abs(to.File - from.File) == 1 && to.Rank - from.Rank == piece.Forward;
            // Attack maps include squares occupied by friendly pieces.
            int x = Math.Abs(to.File - from.File), y = Math.Abs(to.Rank - from.Rank);
            switch (piece.Kind)
            {
                case PieceKind.King: return x <= 1 && y <= 1;
                case PieceKind.Knight: return IsKnightPattern(from, to);
                case PieceKind.Bishop: return x == y && IsPathClear(board, from, to);
                case PieceKind.Rook: return (x == 0 || y == 0) && IsPathClear(board, from, to);
                case PieceKind.Queen: return (x == y || x == 0 || y == 0) && IsPathClear(board, from, to);
                default: return false;
            }
        }

        public static bool IsKnightPattern(Square from, Square to)
        {
            int x = Math.Abs(to.File - from.File), y = Math.Abs(to.Rank - from.Rank);
            return (x == 1 && y == 2) || (x == 2 && y == 1);
        }

        public static bool IsPathClear<TBoard>(TBoard board, Square from, Square to) where TBoard : IReadOnlyBoard
        {
            if (!from.IsValid || !to.IsValid || from == to) return false;
            int dx = to.File - from.File, dy = to.Rank - from.Rank;
            if (dx != 0 && dy != 0 && Math.Abs(dx) != Math.Abs(dy)) return false;
            int sx = Math.Sign(dx), sy = Math.Sign(dy);
            for (Square current = new Square(from.File + sx, from.Rank + sy); current != to;
                current = new Square(current.File + sx, current.Rank + sy))
                if (!board.GetPiece(current).IsEmpty) return false;
            return true;
        }
    }
}
