using System;

namespace ChessButWeird.Domain
{
    public enum Team { White, Black }
    public enum PieceKind { King, Queen, Rook, Bishop, Knight, Pawn }

    public readonly struct Square : IEquatable<Square>
    {
        public readonly int File;
        public readonly int Rank;
        public Square(int file, int rank) { File = file; Rank = rank; }
        public bool IsValid => File >= 0 && File < 8 && Rank >= 0 && Rank < 8;
        public bool Equals(Square other) => File == other.File && Rank == other.Rank;
        public override bool Equals(object? obj) => obj is Square other && Equals(other);
        public override int GetHashCode() => File * 397 ^ Rank;
        public static bool operator ==(Square a, Square b) => a.Equals(b);
        public static bool operator !=(Square a, Square b) => !a.Equals(b);
        public override string ToString() => IsValid ? $"{(char)('a' + File)}{Rank + 1}" : "-";
    }

    public readonly struct PieceState
    {
        public readonly int Id;
        public readonly PieceKind Kind;
        public readonly Team Team;
        public readonly bool HasMoved;
        public readonly int Forward;
        public bool IsEmpty => Id == 0;

        public PieceState(int id, PieceKind kind, Team team, bool hasMoved = false, int forward = 0)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            Id = id; Kind = kind; Team = team; HasMoved = hasMoved;
            Forward = forward == 0 ? (team == Team.White ? 1 : -1) : Math.Sign(forward);
        }
        public PieceState Moved(PieceKind? promotion = null) => new PieceState(Id, promotion ?? Kind, Team, true, Forward);
    }

    public interface IReadOnlyBoard
    {
        PieceState GetPiece(Square square);
    }

    /// <summary>Value pieces, indexed by square. Cloning never shares mutable board storage.</summary>
    public sealed class BoardState : IReadOnlyBoard
    {
        private readonly PieceState[] pieces;
        public BoardState() { pieces = new PieceState[64]; }
        private BoardState(PieceState[] pieces) { this.pieces = pieces; }
        public PieceState GetPiece(Square square) => square.IsValid ? pieces[square.Rank * 8 + square.File] : default;
        public void SetPiece(Square square, PieceState piece)
        {
            if (!square.IsValid) throw new ArgumentOutOfRangeException(nameof(square));
            pieces[square.Rank * 8 + square.File] = piece;
        }
        public BoardState Clone() => new BoardState((PieceState[])pieces.Clone());
    }
}
