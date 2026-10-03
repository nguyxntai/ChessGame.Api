using System.Collections.Generic;

namespace ChessButWeird.Domain
{
    public readonly struct Move
    {
        public readonly Square From, To;
        public readonly PieceKind? Promotion;
        public Move(Square from, Square to, PieceKind? promotion = null) { From = from; To = to; Promotion = promotion; }
    }

    public sealed class MatchState
    {
        public BoardState Board { get; private set; } = new BoardState();
        public Team Turn { get; set; }
        public Square EnPassantTarget { get; set; } = new Square(-1, -1);
        public int HalfMoveClock { get; set; }
        public int FullMoveNumber { get; set; } = 1;
        public MatchState Clone() => new MatchState {
            Board = Board.Clone(), Turn = Turn, EnPassantTarget = EnPassantTarget,
            HalfMoveClock = HalfMoveClock, FullMoveNumber = FullMoveNumber
        };
    }

    public enum BoardChangeKind { Move, Capture, Promotion }
    public readonly struct BoardChange
    {
        public readonly BoardChangeKind Kind;
        public readonly int PieceId;
        public readonly Square From, To;
        public readonly PieceKind PieceKind;
        public BoardChange(BoardChangeKind kind, PieceState piece, Square from, Square to)
        { Kind = kind; PieceId = piece.Id; From = from; To = to; PieceKind = piece.Kind; }
        public BoardChange(BoardChangeKind kind, int pieceId, PieceKind pieceKind, Square from, Square to)
        { Kind = kind; PieceId = pieceId; From = from; To = to; PieceKind = pieceKind; }
    }

    public sealed class MoveResult
    {
        public MatchState State { get; }
        public IReadOnlyList<BoardChange> Changes { get; }
        internal MoveResult(MatchState state, List<BoardChange> changes)
        { State = state; Changes = changes.AsReadOnly(); }
    }
}
