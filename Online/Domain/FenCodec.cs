using System;
using System.Globalization;
using System.Text;

namespace ChessButWeird.Domain
{
    public static class FenCodec
    {
        public const string InitialPosition = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
        public static MatchState Parse(string fen)
        {
            if (string.IsNullOrWhiteSpace(fen)) throw new FormatException("Missing FEN.");
            string[] fields = fen.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 6 || (fields[1] != "w" && fields[1] != "b")) throw new FormatException("Invalid FEN fields.");
            string[] ranks = fields[0].Split('/');
            if (ranks.Length != 8) throw new FormatException("Expected eight ranks.");
            if (fields[2] != "-")
            {
                var seen = new System.Collections.Generic.HashSet<char>();
                foreach (char right in fields[2])
                    if ("KQkq".IndexOf(right) < 0 || !seen.Add(right)) throw new FormatException("Invalid castling rights.");
            }
            var state = new MatchState { Turn = fields[1] == "w" ? Team.White : Team.Black };
            int id = 1;
            for (int row = 0; row < 8; row++)
            {
                int file = 0, rank = 7 - row;
                foreach (char symbol in ranks[row])
                {
                    if (symbol >= '1' && symbol <= '8') { file += symbol - '0'; continue; }
                    if (file >= 8) throw new FormatException("Too many files.");
                    int index = "kqrbnp".IndexOf(char.ToLowerInvariant(symbol));
                    if (index < 0) throw new FormatException("Invalid piece.");
                    Team team = char.IsUpper(symbol) ? Team.White : Team.Black;
                    PieceKind kind = (PieceKind)index;
                    bool hasMoved = true;
                    int home = team == Team.White ? 0 : 7;
                    string rights = fields[2];
                    if (kind == PieceKind.Pawn) hasMoved = rank != (team == Team.White ? 1 : 6);
                    if (kind == PieceKind.King && file == 4 && rank == home)
                        hasMoved = rights.IndexOf(team == Team.White ? 'K' : 'k') < 0 && rights.IndexOf(team == Team.White ? 'Q' : 'q') < 0;
                    if (kind == PieceKind.Rook && rank == home && (file == 0 || file == 7))
                        hasMoved = rights.IndexOf(team == Team.White ? (file == 7 ? 'K' : 'Q') : (file == 7 ? 'k' : 'q')) < 0;
                    state.Board.SetPiece(new Square(file++, rank), new PieceState(id++, kind, team, hasMoved));
                }
                if (file != 8) throw new FormatException("Expected eight files.");
            }
            state.EnPassantTarget = fields[3] == "-" ? new Square(-1, -1) : ParseSquare(fields[3]);
            if (state.EnPassantTarget.IsValid && state.EnPassantTarget.Rank != (state.Turn == Team.White ? 5 : 2))
                throw new FormatException("Invalid en passant rank.");
            if (!int.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out int half) ||
                !int.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out int full) || full < 1)
                throw new FormatException("Invalid move counters.");
            state.HalfMoveClock = half; state.FullMoveNumber = full;
            return state;
        }

        public static Square ParseSquare(string value)
        {
            if (value == null || value.Length != 2) throw new FormatException("Invalid square.");
            var square = new Square(value[0] - 'a', value[1] - '1');
            if (!square.IsValid) throw new FormatException("Invalid square.");
            return square;
        }

        public static string Write(MatchState state)
        {
            var builder = new StringBuilder(90);
            for (int y = 7; y >= 0; y--)
            {
                int empty = 0;
                for (int x = 0; x < 8; x++)
                {
                    PieceState piece = state.Board.GetPiece(new Square(x, y));
                    if (piece.IsEmpty) { empty++; continue; }
                    if (empty > 0) { builder.Append(empty); empty = 0; }
                    char symbol = "kqrbnp"[(int)piece.Kind];
                    builder.Append(piece.Team == Team.White ? char.ToUpperInvariant(symbol) : symbol);
                }
                if (empty > 0) builder.Append(empty);
                if (y > 0) builder.Append('/');
            }
            builder.Append(state.Turn == Team.White ? " w " : " b ");
            int before = builder.Length;
            AppendRight(builder, state.Board, Team.White, 7, 'K');
            AppendRight(builder, state.Board, Team.White, 0, 'Q');
            AppendRight(builder, state.Board, Team.Black, 7, 'k');
            AppendRight(builder, state.Board, Team.Black, 0, 'q');
            if (before == builder.Length) builder.Append('-');
            builder.Append(' ').Append(state.EnPassantTarget.ToString()).Append(' ')
                .Append(state.HalfMoveClock.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(state.FullMoveNumber.ToString(CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static void AppendRight(StringBuilder builder, BoardState board, Team team, int file, char symbol)
        {
            int rank = team == Team.White ? 0 : 7;
            PieceState king = board.GetPiece(new Square(4, rank)), rook = board.GetPiece(new Square(file, rank));
            if (!king.IsEmpty && king.Kind == PieceKind.King && king.Team == team && !king.HasMoved &&
                !rook.IsEmpty && rook.Kind == PieceKind.Rook && rook.Team == team && !rook.HasMoved) builder.Append(symbol);
        }
    }
}
