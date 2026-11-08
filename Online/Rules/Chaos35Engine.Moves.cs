using ChessButWeird.Domain;
using ChessButWeird.Domain.Chaos35;

namespace ChessGame.Api.Online;

internal sealed partial class Chaos35Engine
{
    public void ApplyMove(OnlineMatch m, Move move)
    {
        Require(TryMove(m, move, out var next), "InvalidMove");
        Commit(m, next);
    }
    private bool TryMove(OnlineMatch original, Move move, out OnlineMatch next, bool simulate = false)
    {
        next = null!;
        var a = original.Chaos!;
        if (a.Phase != "Playing" || a.Rifle is not null || a.EscapeTeam is not null || a.Deployments.Count > 0 ||
            !Available(original, move.From) || !Available(original, move.To) || move.From == move.To) return false;
        var source = At(original, move.From); var victim = At(original, move.To);
        if (source is null || source.Team != original.Turn || a.ForcedPawn.HasValue && source.Id != a.ForcedPawn) return false;
        var owner = Side(original, source.Team); var sourceEffect = ReadEffect(original, source);
        if (victim?.Kind == "King" && !ReadEffect(original, victim).Decoy) return false;
        bool allied = victim?.Team == source.Team;
        if (allied && !(source.Kind == "Pawn" && victim!.Kind == "Pawn" && Has(owner, 6) && !sourceEffect.Bloodthirsty && !sourceEffect.TrapPawn)) return false;
        bool promotion = source.Kind == "Pawn" && !sourceEffect.TrapPawn && !Has(Side(original, GameRules.Other(source.Team)), 33) && move.To.Rank == (source.Forward > 0 ? 7 : 0);
        if (promotion != move.Promotion.HasValue || move.Promotion is PieceKind.King or PieceKind.Pawn) return false;
        var board = GameRules.Read(original).Board; var pieceState = board.GetPiece(move.From); var context = Context(original, source);
        bool castle = CanCastle(original, source, move);
        bool ep = source.Kind == "Pawn" && !sourceEffect.TrapPawn && move.To.ToString() == original.EnPassantTarget && victim is null &&
            Math.Abs(move.To.File - move.From.File) == 1 && move.To.Rank - move.From.Rank == source.Forward && move.From.Rank == (source.Forward > 0 ? 4 : 3);
        if (ep)
        {
            victim = At(original, new(move.To.File, move.From.Rank));
            if (victim is null || victim.Team == source.Team || victim.Kind != "Pawn") return false;
        }
        bool pattern = !AramRules.SuppressesStandardMovement(context) && !allied && MovementRules.IsLegalPattern(board, pieceState, move.From, move.To);
        bool extension = AramRules.BuiltIn.Allows(board, pieceState, move.From, move.To, context);
        if (!pattern && !extension && !castle && !ep) return false;
        next = OnlineJson.Clone(original);
        var m = next; a = m.Chaos!;
        var moving = Piece(m, source.Id); var s = Side(m, moving.Team); var e = Effect(m, moving);
        var captured = victim is null ? null : Piece(m, victim.Id);
        ObserveChecks(m);
        bool snipe = Has(s, 7) && moving.Kind == "Bishop" && e.SniperUntil > a.CombinedPlies && captured is not null && !allied;
        bool teleport = Has(s, 5) && moving.Id == s.OriginalQueen && !pattern;
        bool extra = false;
        if (captured is not null)
        {
            CaptureEffects(m, moving, captured, move.From, move.To);
            extra = !simulate && !allied && moving.Kind == "Pawn" && Has(s, 8) && s.ExtraUses < 3 && Random(100) < 25;
            Remove(m, captured, true, false);
        }
        if (!snipe) { moving.Square = move.To.ToString(); moving.HasMoved = true; }
        else e.SniperUntil = null;
        if (teleport) { s.TeleportUses++; s.TeleportReady = s.CompletedTurns + 5; }
        if (castle)
        {
            var rook = At(m, new(move.To.File == 6 ? 7 : 0, move.From.Rank))!;
            rook.Square = new Square(move.To.File == 6 ? 5 : 3, move.From.Rank).ToString(); rook.HasMoved = true;
            s.CastledWings |= move.To.File == 6 ? 1 : 2;
        }
        if (captured is not null) Detonate(m, captured, move.To);
        if (e.Decoy && captured is not null && !allied && m.Board.Contains(moving)) { e.Decoy = false; moving.Kind = captured.Kind; }
        if (promotion && m.Board.Contains(moving)) { moving.Kind = move.Promotion!.Value.ToString(); Promoted(m, moving); }
        if (moving.Kind == "Pawn" && m.Board.Contains(moving))
        {
            if (Has(Side(m, GameRules.Other(moving.Team)), 33) && move.To.Rank == (moving.Forward > 0 ? 7 : 0)) e.TrapPawn = true;
            if (Has(s, 11) && move.To.Rank == Back(moving.Team) && move.To.Rank - move.From.Rank == -moving.Forward)
            { Remove(m, moving, false); a.Mines.Add(new() { Team = moving.Team, Square = move.To.ToString() }); }
        }
        if (!snipe) Land(m, moving, move.To);
        if (InCheck(m, moving.Team)) return false;
        m.EnPassantTarget = !snipe && pieceState.Kind == PieceKind.Pawn && !sourceEffect.TrapPawn && Math.Abs(move.To.Rank - move.From.Rank) == 2
            ? new Square(move.From.File, (move.From.Rank + move.To.Rank) / 2).ToString() : "-";
        m.HalfMoveClock = pieceState.Kind == PieceKind.Pawn || captured is not null ? 0 : m.HalfMoveClock + 1;
        a.ForcedPawn = null;
        if (simulate) { m.Turn = GameRules.Other(moving.Team); return true; }
        if (extra && moving.Kind == "Pawn" && m.Board.Contains(moving) && move.To.Rank != (moving.Forward > 0 ? 7 : 0))
        {
            a.ForcedPawn = moving.Id;
            if (HasLegalMove(m))
            {
                AdvancePly(m);
                if (m.Board.Contains(moving)) { s.ExtraUses++; RecordPosition(m); return true; }
            }
            a.ForcedPawn = null;
        }
        s.CompletedTurns++;
        if (moving.Team == "Black") m.FullMoveNumber++;
        if (!extra || a.CombinedPlies == original.Chaos!.CombinedPlies) AdvancePly(m);
        else CheckCollapse(m);
        m.Turn = GameRules.Other(moving.Team); BeginTurn(m); RecordPosition(m);
        return true;
    }
    private static bool CanCastle(OnlineMatch m, OnlinePiece king, Move move)
    {
        var s = Side(m, king.Team); bool fortress = Has(s, 1);
        if (king.Kind != "King" || ReadEffect(m, king).Decoy || move.From.Rank != Back(king.Team) ||
            !fortress && move.From.File != 4 || move.To.Rank != move.From.Rank || move.To.File is not (2 or 6)) return false;
        int wing = move.To.File == 6 ? 1 : 2;
        if (fortress && (s.CastledWings & wing) != 0) return false;
        var rookSquare = new Square(move.To.File == 6 ? 7 : 0, move.From.Rank); var rook = At(m, rookSquare);
        if (rook is null || rook.Team != king.Team || rook.Kind != "Rook" || rook.HasMoved ||
            !Available(m, rookSquare) || !MovementRules.IsPathClear(GameRules.Read(m).Board, move.From, rookSquare)) return false;
        var rookTo = new Square(move.To.File == 6 ? 5 : 3, move.From.Rank);
        if (At(m, move.To) is not null || At(m, rookTo) is { } occupied && occupied.Id != king.Id) return false;
        if (fortress) return true;
        return !king.HasMoved && !InCheck(m, king.Team) && SafeRelocate(m, king, rookTo);
    }
    private static ChaosPieceEffect ReadEffect(OnlineMatch m, OnlinePiece p) => Side(m, p.Team).Effects.FirstOrDefault(e => e.PieceId == p.Id) ?? new() { PieceId = p.Id };
    private static IEnumerable<Move> Candidates(OnlineMatch m)
    {
        foreach (var p in m.Board.Where(p => p.Team == m.Turn).ToList())
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            {
                var to = new Square(x, y);
                bool promote = p.Kind == "Pawn" && !ReadEffect(m, p).TrapPawn && !Has(Side(m, GameRules.Other(p.Team)), 33) && y == (p.Forward > 0 ? 7 : 0);
                if (promote) foreach (var kind in new[] { PieceKind.Queen, PieceKind.Rook, PieceKind.Bishop, PieceKind.Knight }) yield return new(GameRules.Square(p.Square), to, kind);
                else yield return new(GameRules.Square(p.Square), to);
            }
    }
    private bool HasLegalMove(OnlineMatch m) => Candidates(m).Any(move => TryMove(m, move, out _, true));
    public List<ChaosLegalMove> LegalMoves(OnlineMatch m)
    {
        if (m.Status != "InProgress" || m.Chaos!.Phase != "Playing") return new();
        return Candidates(m).Where(move => TryMove(m, move, out _, true))
            .Select(move => new ChaosLegalMove(move.From.ToString(), move.To.ToString(), move.Promotion?.ToString())).ToList();
    }
    public (string? WinnerTeam, string Reason)? Outcome(OnlineMatch m)
    {
        var a = m.Chaos!;
        bool Alive(string team) => Has(Side(m, team), 32) ? m.Board.Any(p => p.Team == team) : RealKing(m, team) is not null;
        bool white = Alive("White"), black = Alive("Black");
        if (!white || !black) return (!white && !black ? ChaosEngine.DrawWinner(m) : white ? "White" : "Black", "ArmyDestroyed");
        if (a.Phase != "Playing" || a.Deployments.Count > 0 || a.Rifle is not null || a.EscapeTeam is not null) return null;
        bool legal = HasLegalMove(m), check = InCheck(m, m.Turn);
        if (!legal && check)
        {
            var s = Side(m, m.Turn); var king = RealKing(m, m.Turn);
            if (Has(s, 20) && s.EscapeUses < 3 && king is not null && Empty(m).Any(to => Nearby(king, to, 2) && SafeRelocate(m, king, to)))
            { a.EscapeTeam = m.Turn; return null; }
            return (GameRules.Other(m.Turn), "Checkmate");
        }
        foreach (var s in a.Sides) if (Has(s, 32) && s.CompletedTurns >= 25) return (GameRules.Other(s.Team), "PoorKingDeadline");
        if (!legal) return (ChaosEngine.DrawWinner(m), "Stalemate");
        if (m.HalfMoveClock >= 100) return (ChaosEngine.DrawWinner(m), "FiftyMoveRule");
        if (m.Repetitions.GetValueOrDefault(PositionKey(m)) >= 3) return (ChaosEngine.DrawWinner(m), "ThreefoldRepetition");
        return null;
    }
    private static bool Nearby(OnlinePiece p, Square to, int distance) { var from = GameRules.Square(p.Square); return Math.Abs(to.File - from.File) <= distance && Math.Abs(to.Rank - from.Rank) <= distance; }
    private static string PositionKey(OnlineMatch m)
    {
        var a = m.Chaos!;
        string P(int? id) => m.Board.FirstOrDefault(p => p.Id == id)?.Square ?? "-";
        int Left(int ready, int clock) => Math.Max(0, ready - clock);
        var parts = new List<string> { string.Join(' ', FenCodec.Write(GameRules.Read(m)).Split(' ').Take(4)), "forced:" + P(a.ForcedPawn), "files:" + a.CollapsedFiles };
        foreach (var side in a.Sides)
        {
            string value = side.BuffId switch {
                0 => string.Join(",", side.CommandantPawns.Select(id => P(id)).OrderBy(x => x)),
                1 => side.CastledWings.ToString(), 2 => P(side.FreestyleKnight),
                3 => $"{P(side.SwappedKnight)}/{P(side.SwappedBishop)}/{side.SwapActive}/{side.DoppelDisabled}/{Left(side.SwapReady,a.CombinedPlies)}",
                4 => $"{P(side.OriginalQueen)}/{side.BomberUsed}",
                5 => $"{P(side.OriginalQueen)}/{side.TeleportUses}/{Left(side.TeleportReady,side.CompletedTurns)}",
                8 => side.ExtraUses.ToString(), 9 => Left(50,a.CombinedPlies).ToString(),
                10 => $"{side.PeaceFailed}/{side.PeaceUsed}/{Left(14,a.CombinedPlies)}",
                13 => Left(side.HidingReady,a.CombinedPlies).ToString(),
                14 => $"{side.Tickets}/{side.RollsThisTurn}", 15 => side.SubstituteUsed.ToString(),
                18 => $"{Left(10,side.CompletedTurns)}/{(a.CollapsedFiles==1?Left(a.CollapseSecondAt,a.CombinedPlies):0)}",
                20 => side.EscapeUses.ToString(), 25 => Left(side.RifleReady,side.CompletedTurns).ToString(),
                26 => $"{string.Join(",",side.QuestCaptures.OrderBy(x=>x))}/{side.QuestPromoted}/{side.QuestRewarded}",
                27 => $"{side.QueenRefundUsed}/{(side.QueenRefundUsed?0:Left(10,side.CompletedTurns))}",
                31 => $"{side.Credit}/{side.BoughtThisTurn}", 32 => Left(25,side.CompletedTurns).ToString(), _ => ""
            };
            parts.Add(side.Team + ":" + side.BuffId + ":" + value);
            foreach (var e in side.Effects.Where(e => m.Board.Any(p => p.Id == e.PieceId)))
            {
                string effect = $"{e.Bloodthirsty}/{e.Decoy}/{e.QueenMastery}/{e.TrapPawn}/{e.PromotedQueen}/{e.Commandant}/{Left(e.SniperUntil??0,a.CombinedPlies)}/{Left(e.CannonUntil??0,a.CombinedPlies)}/{Left(e.InfectedUntil??0,a.CombinedPlies)}";
                if (effect != "False/False/False/False/False/False/0/0/0" || e.Unstable || e.Passenger)
                    parts.Add($"{side.Team}:{P(e.PieceId)}:{effect}/{(e.Unstable?e.BetrayalChecks:-1)}/{e.Passenger}/{(e.Passenger?Left(e.PassengerReady,side.CompletedTurns):0)}/{(e.Passenger?OnlineJson.Write(e.PassengerTraits):"")}");
            }
        }
        foreach (var p in m.Board.Where(p => p.Kind == "Pawn" || p.Kind == "Rook" && Side(m,p.Team).BuffId == 1))
            parts.Add($"moved:{p.Team}:{p.Square}:{p.HasMoved}:{p.Forward}");
        foreach (var marker in a.Mines) parts.Add("mine:" + marker.Team + ":" + marker.Square);
        foreach (var marker in a.Crates) parts.Add("crate:" + marker.Team + ":" + marker.Square + ":" + marker.Kind);
        parts.Sort(StringComparer.Ordinal);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join(";",parts))));
    }
    private static void RecordPosition(OnlineMatch m)
    {
        var a = m.Chaos!;
        if (a.Deployments.Count > 0 || a.Rifle is not null || a.EscapeTeam is not null) return;
        string key = PositionKey(m);
        if (a.LastPositionKey == key && a.LastRecordedPly == a.CombinedPlies) return;
        m.Repetitions[key] = m.Repetitions.GetValueOrDefault(key) + 1;
        a.LastPositionKey = key; a.LastRecordedPly = a.CombinedPlies;
    }
}
