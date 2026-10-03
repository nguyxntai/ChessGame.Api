using System;
using System.Collections.Generic;

namespace ChessButWeird.Domain
{
    /// <summary>Deterministic classic rules. No side effects on the supplied state.</summary>
    public static class ClassicRules
    {
        private static readonly PieceKind[] Promotions = { PieceKind.Queen, PieceKind.Rook, PieceKind.Bishop, PieceKind.Knight };
        public static Team Opponent(Team team) => team == Team.White ? Team.Black : Team.White;

        public static bool TryApply(MatchState state, Move move, out MoveResult result)
        {
            result = null!;
            if (state == null || !move.From.IsValid || !move.To.IsValid || move.From == move.To) return false;
            PieceState piece = state.Board.GetPiece(move.From), target = state.Board.GetPiece(move.To);
            if (piece.IsEmpty || piece.Team != state.Turn || (!target.IsEmpty &&
                (target.Team == piece.Team || target.Kind == PieceKind.King))) return false;
            bool promotes = piece.Kind == PieceKind.Pawn && move.To.Rank == (piece.Forward > 0 ? 7 : 0);
            if (promotes != move.Promotion.HasValue) return false;
            if (move.Promotion.HasValue && (move.Promotion == PieceKind.King || move.Promotion == PieceKind.Pawn ||
                !Enum.IsDefined(typeof(PieceKind), move.Promotion.Value))) return false;
            bool castle = CanCastle(state, piece, move);
            bool enPassant = CanEnPassant(state, piece, move);
            if (!castle && !enPassant && !MovementRules.IsLegalPattern(state.Board, piece, move.From, move.To)) return false;

            MatchState next = state.Clone();
            var changes = new List<BoardChange>(4);
            Square captureSquare = enPassant ? new Square(move.To.File, move.From.Rank) : move.To;
            PieceState captured = next.Board.GetPiece(captureSquare);
            if (!captured.IsEmpty)
            {
                changes.Add(new BoardChange(BoardChangeKind.Capture, captured, captureSquare, captureSquare));
                next.Board.SetPiece(captureSquare, default);
            }
            next.Board.SetPiece(move.From, default);
            PieceState moved = piece.Moved(move.Promotion);
            next.Board.SetPiece(move.To, moved);
            changes.Add(new BoardChange(BoardChangeKind.Move, piece, move.From, move.To));
            if (promotes) changes.Add(new BoardChange(BoardChangeKind.Promotion, moved, move.To, move.To));
            if (castle)
            {
                Square rookFrom = new Square(move.To.File > move.From.File ? 7 : 0, move.From.Rank);
                Square rookTo = new Square(move.To.File > move.From.File ? 5 : 3, move.From.Rank);
                PieceState rook = next.Board.GetPiece(rookFrom);
                next.Board.SetPiece(rookFrom, default);
                next.Board.SetPiece(rookTo, rook.Moved());
                changes.Add(new BoardChange(BoardChangeKind.Move, rook, rookFrom, rookTo));
            }
            if (IsInCheck(next.Board, piece.Team)) return false;
            next.EnPassantTarget = piece.Kind == PieceKind.Pawn && Math.Abs(move.To.Rank - move.From.Rank) == 2
                ? new Square(move.From.File, (move.From.Rank + move.To.Rank) / 2) : new Square(-1, -1);
            next.HalfMoveClock = piece.Kind == PieceKind.Pawn || !captured.IsEmpty ? 0 : state.HalfMoveClock + 1;
            if (state.Turn == Team.Black) next.FullMoveNumber++;
            next.Turn = Opponent(state.Turn);
            result = new MoveResult(next, changes);
            return true;
        }

        public static List<Move> LegalMoves(MatchState state)
        {
            var result = new List<Move>(48);
            for (int rank = 0; rank < 8; rank++) for (int file = 0; file < 8; file++)
                AppendLegalMovesFrom(state, new Square(file, rank), result);
            return result;
        }

        public static List<Move> LegalMovesFrom(MatchState state, Square from)
        {
            var result = new List<Move>(8);
            AppendLegalMovesFrom(state, from, result);
            return result;
        }

        private static void AppendLegalMovesFrom(MatchState state, Square from, List<Move> result)
        {
            if (state == null || result == null || !from.IsValid)
                return;

            PieceState piece = state.Board.GetPiece(from);
            if (piece.IsEmpty || piece.Team != state.Turn)
                return;

            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            {
                Square to = new Square(x, y);
                if (piece.Kind == PieceKind.Pawn && y == (piece.Forward > 0 ? 7 : 0))
                {
                    foreach (PieceKind promotion in Promotions)
                    {
                        Move move = new Move(from, to, promotion);
                        if (TryApply(state, move, out _)) result.Add(move);
                    }
                }
                else
                {
                    Move move = new Move(from, to);
                    if (TryApply(state, move, out _)) result.Add(move);
                }
            }
        }

        public static bool IsInCheck(BoardState board, Team team) =>
            KingSafetyRules.IsInCheck(board, team, new NoBuffs());

        public static bool IsAttacked(BoardState board, Square to, Team attacker) =>
            KingSafetyRules.IsAttacked(board, to, attacker, new NoBuffs());

        private static bool CanEnPassant(MatchState state, PieceState piece, Move move)
        {
            if (piece.Kind != PieceKind.Pawn || move.To != state.EnPassantTarget ||
                !state.Board.GetPiece(move.To).IsEmpty || Math.Abs(move.To.File - move.From.File) != 1 ||
                move.To.Rank - move.From.Rank != piece.Forward || move.From.Rank != (piece.Forward > 0 ? 4 : 3)) return false;
            PieceState captured = state.Board.GetPiece(new Square(move.To.File, move.From.Rank));
            return !captured.IsEmpty && captured.Kind == PieceKind.Pawn && captured.Team != piece.Team;
        }

        private static bool CanCastle(MatchState state, PieceState king, Move move)
        {
            int rank = king.Team == Team.White ? 0 : 7;
            if (king.Kind != PieceKind.King || king.HasMoved || move.From != new Square(4, rank) ||
                move.To.Rank != rank || (move.To.File != 2 && move.To.File != 6)) return false;
            Square rookSquare = new Square(move.To.File == 6 ? 7 : 0, rank);
            PieceState rook = state.Board.GetPiece(rookSquare);
            if (rook.IsEmpty || rook.Kind != PieceKind.Rook || rook.Team != king.Team || rook.HasMoved ||
                !MovementRules.IsPathClear(state.Board, move.From, rookSquare) || IsInCheck(state.Board, king.Team)) return false;
            BoardState transit = state.Board.Clone();
            transit.SetPiece(move.From, default);
            Square middle = new Square(move.To.File == 6 ? 5 : 3, rank);
            transit.SetPiece(middle, king);
            return !IsAttacked(transit, middle, Opponent(king.Team));
        }
    }
}
