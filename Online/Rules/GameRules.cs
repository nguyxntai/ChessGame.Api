using System.Security.Cryptography;
using System.Text;
using ChessButWeird.Domain;

namespace ChessGame.Api.Online;

public sealed class GameRules(AramEngine aram)
{
    public static MatchState Read(OnlineMatch match)
    {
        var state = new MatchState
        {
            Turn = Enum.Parse<Team>(match.Turn),
            EnPassantTarget = match.EnPassantTarget == "-" ? new Square(-1, -1) : Square(match.EnPassantTarget),
            HalfMoveClock = match.HalfMoveClock,
            FullMoveNumber = match.FullMoveNumber
        };
        foreach (var p in match.Board)
            state.Board.SetPiece(Square(p.Square), new PieceState(p.Id, Enum.Parse<PieceKind>(p.Kind),
                Enum.Parse<Team>(p.Team), p.HasMoved, p.Forward));
        return state;
    }
    public static void Write(OnlineMatch match, MatchState state)
    {
        var board = new List<OnlinePiece>();
        for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
        {
            var square = new Square(x, y);
            var p = state.Board.GetPiece(square);
            if (!p.IsEmpty) board.Add(new OnlinePiece { Id = p.Id, Kind = p.Kind.ToString(),
                Team = p.Team.ToString(), Square = square.ToString(), HasMoved = p.HasMoved, Forward = p.Forward });
        }
        match.Board = board;
        match.Turn = state.Turn.ToString();
        match.EnPassantTarget = state.EnPassantTarget.ToString();
        match.HalfMoveClock = state.HalfMoveClock;
        match.FullMoveNumber = state.FullMoveNumber;
    }
    public static Square Square(string? value)
    {
        try { return FenCodec.ParseSquare(value!); }
        catch (FormatException) { throw new OnlineException("InvalidMove", 400); }
    }
    public static string Other(string team) => team == "White" ? "Black" : "White";

    public void ApplyMove(OnlineMatch match, string team, MoveCommand command)
    {
        if (team != match.Turn) throw new OnlineException("NotYourTurn");
        PieceKind? promotion = null;
        if (command.Promotion is not null)
        {
            if (!Enum.TryParse<PieceKind>(command.Promotion, true, out var kind) ||
                kind is not (PieceKind.Queen or PieceKind.Rook or PieceKind.Bishop or PieceKind.Knight))
                throw new OnlineException("InvalidMove", 400);
            promotion = kind;
        }
        var move = new Move(Square(command.From), Square(command.To), promotion);
        if (match.Aram is not null) aram.ApplyMove(match, move);
        else
        {
            if (!ClassicRules.TryApply(Read(match), move, out var result)) throw new OnlineException("InvalidMove");
            Write(match, result.State);
            RecordPosition(match);
        }
        match.DrawOffer = null;
    }
    public void ApplyAbility(OnlineMatch match, string team, AbilityCommand command, DateTime now)
    {
        if (match.Aram is null) throw new OnlineException("AbilityNotSupported");
        aram.ApplyAbility(match, team, command, now);
    }
    public (string? WinnerTeam, string Reason)? Outcome(OnlineMatch match)
    {
        if (match.Aram is not null) return aram.Outcome(match);
        var state = Read(match);
        if (ClassicRules.LegalMoves(state).Count == 0)
            return ClassicRules.IsInCheck(state.Board, state.Turn)
                ? (Other(match.Turn), "Checkmate") : (null, "Stalemate");
        if (IsDeadPosition(match.Board)) return (null, "InsufficientMaterial");
        if (match.HalfMoveClock >= 150) return (null, "SeventyFiveMoveRule");
        if (match.Repetitions.GetValueOrDefault(PositionKey(match)) >= 5) return (null, "FivefoldRepetition");
        return null;
    }
    public string ClaimDraw(OnlineMatch match, string team)
    {
        if (match.Turn != team) throw new OnlineException("NotYourTurn");
        if (match.Aram is not null) throw new OnlineException("DrawClaimNotSupported");
        if (match.HalfMoveClock >= 100) return "FiftyMoveRule";
        if (match.Repetitions.GetValueOrDefault(PositionKey(match)) >= 3) return "ThreefoldRepetition";
        throw new OnlineException("DrawNotClaimable");
    }
    public static bool IsDeadPosition(List<OnlinePiece> board)
    {
        var material = board.Where(p => p.Kind != "King").ToList();
        if (material.Count == 0) return true;
        if (material.Count == 1 && material[0].Kind is "Bishop" or "Knight") return true;
        return material.All(p => p.Kind == "Bishop") &&
            material.Select(p => { var s = Square(p.Square); return (s.File + s.Rank) % 2; }).Distinct().Count() == 1;
    }
    public static bool CanPossiblyMate(OnlineMatch match, string team)
    {
        if (match.Aram is not null) return match.Board.Any(p => p.Team == team && p.Kind == "King");
        var own = match.Board.Where(p => p.Team == team && p.Kind != "King").ToList();
        if (own.Count == 0 || IsDeadPosition(match.Board)) return false;
        if (own.Count == 1 && own[0].Kind is "Bishop" or "Knight" &&
            match.Board.All(p => p.Team == team || p.Kind == "King")) return false;
        return true;
    }
    public static void RecordPosition(OnlineMatch match)
    {
        var key = PositionKey(match);
        match.Repetitions[key] = match.Repetitions.GetValueOrDefault(key) + 1;
    }
    private static string PositionKey(OnlineMatch match)
    {
        var state = Read(match);
        var fields = FenCodec.Write(state).Split(' ');
        // A nominal en-passant target affects repetition only when a legal capture exists.
        if (state.EnPassantTarget.IsValid && !ClassicRules.LegalMoves(state).Any(m =>
            m.To == state.EnPassantTarget && m.From.File != m.To.File && state.Board.GetPiece(m.From).Kind == PieceKind.Pawn))
            fields[3] = "-";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(' ', fields.Take(4)))));
    }
}
