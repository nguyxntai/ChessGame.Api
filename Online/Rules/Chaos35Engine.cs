using System.Security.Cryptography;
using ChessButWeird.Domain;
using ChessButWeird.Domain.Chaos35;

namespace ChessGame.Api.Online;

internal sealed partial class Chaos35Engine
{
    internal static readonly int[][] Tiers = {
        new[] { 0, 1, 2, 6, 7, 8, 9, 26, 27, 28 },
        new[] { 3, 4, 10, 11, 12, 13, 14, 29, 30, 31 },
        new[] { 5, 15, 16, 17, 18, 19, 20, 32, 33, 34 }, new[] { 21, 22, 23, 24, 25 }
    };
    private static int Random(int count) => RandomNumberGenerator.GetInt32(count);
    private static T Pick<T>(IReadOnlyList<T> list) => list[Random(list.Count)];
    private static bool Has(ChaosSide side, int buff) => side.BuffId == buff;
    private static ChaosSide Side(OnlineMatch m, string team) => m.Chaos!.Sides.Single(s => s.Team == team);
    private static int Back(string team) => team == "White" ? 0 : 7;
    private static bool Home(Square s, string team, int ranks = 4) => team == "White" ? s.Rank < ranks : s.Rank >= 8 - ranks;
    private static bool Available(OnlineMatch m, Square s) => s.IsValid && s.File >= m.Chaos!.CollapsedFiles && s.File < 8 - m.Chaos.CollapsedFiles;
    private static OnlinePiece? At(OnlineMatch m, Square s) => m.Board.FirstOrDefault(p => p.Square == s.ToString());
    private static OnlinePiece Piece(OnlineMatch m, int? id) => m.Board.FirstOrDefault(p => p.Id == id) ?? throw new OnlineException("InvalidAbilityTarget");
    private static ChaosPieceEffect Effect(OnlineMatch m, OnlinePiece p)
    {
        var side = Side(m, p.Team);
        var effect = side.Effects.FirstOrDefault(e => e.PieceId == p.Id);
        if (effect is null) { effect = new() { PieceId = p.Id }; side.Effects.Add(effect); }
        return effect;
    }
    private static List<Square> Empty(OnlineMatch m, string? team = null, int ranks = 4) =>
        (from x in Enumerable.Range(0, 8) from y in Enumerable.Range(0, 8) let s = new Square(x, y)
         where Available(m, s) && At(m, s) is null && (team is null || ranks == 0 || Home(s, team, ranks)) select s).ToList();
    private static void Require(bool valid, string error = "InsufficientAbilityResource")
    { if (!valid) throw new OnlineException(error); }
    private static void Commit(OnlineMatch target, OnlineMatch next)
    {
        target.Board = next.Board; target.Chaos = next.Chaos; target.Turn = next.Turn;
        target.Captures = next.Captures; target.EnPassantTarget = next.EnPassantTarget;
        target.HalfMoveClock = next.HalfMoveClock; target.FullMoveNumber = next.FullMoveNumber;
        target.Repetitions = next.Repetitions; target.ReadyDeadline = next.ReadyDeadline;
    }
    public void Initialize(OnlineMatch m, DateTime now)
    {
        var pool = Pick(Tiers);
        m.Chaos = new() { RulesVersion = 3, SetupDeadline = m.ReadyDeadline,
            NextPieceId = Math.Max(100, m.Board.Select(p => p.Id).DefaultIfEmpty().Max() + 1) };
        foreach (string team in new[] { "White", "Black" })
            m.Chaos.Sides.Add(new() { Team = team, SwapReady = 4,
                DraftOptions = pool.OrderBy(_ => Random(int.MaxValue)).Take(3).ToList(),
                OriginalQueen = m.Board.FirstOrDefault(p => p.Team == team && p.Kind == "Queen")?.Id });
        foreach (var kind in Enum.GetNames<PieceKind>())
            m.Chaos.Hitboxes.Add(new() { Kind = kind, HalfWidth = .3, HalfDepth = .3, Height = kind is "King" or "Queen" ? 1.2 : kind == "Pawn" ? .65 : .9 });
    }
    private static bool NeedsTargets(int? buff) => buff is 0 or 2 or 3 or 13 or 27;
    private void InitialEffects(OnlineMatch m, ChaosSide s)
    {
        s.Initialized = true; s.SetupComplete = !NeedsTargets(s.BuffId); s.SwapReady = Has(s, 3) ? m.Chaos!.CombinedPlies + 4 : 0;
        var army = m.Board.Where(p => p.Team == s.Team).ToList();
        if (Has(s, 24) || Has(s, 25))
        {
            foreach (var p in army.Where(p => p.Kind != "King")) Remove(m, p, false);
            if (Has(s, 24)) foreach (var square in Empty(m, s.Team)) Spawn(m, s.Team, "Pawn", square);
        }
        if (Has(s, 19)) foreach (var p in army.Where(p => p.Kind is not ("King" or "Queen")))
        { p.Kind = Pick(new[] { "Pawn", "Knight", "Bishop", "Rook" }); p.HasMoved = true; }
        if (Has(s, 17))
        {
            var squares = Empty(m, s.Team, 3).Where(p => p.Rank == (s.Team == "White" ? 2 : 5)).ToList();
            if (squares.Count > 0) Effect(m, Spawn(m, s.Team, "Queen", Pick(squares))).Unstable = true;
        }
        if (Has(s, 16)) foreach (var p in army.Where(p => p.Kind == "Rook")) Effect(m, p).CannonUntil = m.Chaos!.CombinedPlies + 10;
        if (Has(s, 23)) s.OriginalFormation = army.Select(p => new FormationPlacement(p.Id, p.Square)).ToList();
        if (Has(s, 28))
        {
            var king = RealKing(m, s.Team); var rook = At(m, new(7, Back(s.Team)));
            if (king is not null && rook?.Team == s.Team && rook.Kind == "Rook")
            { SwapRelocate(m, king, new(6, Back(s.Team))); SwapRelocate(m, rook, new(5, Back(s.Team))); s.CastledWings |= 1; }
        }
        if (Has(s, 32) && RealKing(m, s.Team) is { } poorKing) poorKing.Kind = "Queen";
        if (Has(s, 3)) s.DisguisedBuffId = Pick(Tiers[1].Where(id => id != 3).ToArray());
    }
    private void Setup(OnlineMatch m, string team, AbilityCommand c, DateTime now)
    {
        var a = m.Chaos!; var s = Side(m, team);
        Require(now < a.SetupDeadline, "SetupExpired");
        if (c.Kind == "SelectBuff")
        {
            Require(a.Phase == "Draft" && s.BuffId is null && c.BuffId.HasValue && s.DraftOptions.Contains(c.BuffId.Value), "InvalidBuffSelection");
            s.BuffId = c.BuffId;
            if (a.Sides.All(side => side.BuffId.HasValue))
            {
                foreach (var side in a.Sides) InitialEffects(m, side);
                a.Phase = "Setup";
                if (a.Sides.Any(side => Has(side, 23))) m.ReadyDeadline = a.SetupDeadline = now.AddSeconds(180);
                foreach (var side in a.Sides.Where(side => Has(side, 23))) side.FormationDeadline = now.AddSeconds(120);
            }
        }
        else
        {
            Require(a.Phase == "Setup", "InvalidSetupPhase");
            if (c.Kind == "ConfirmFormation") { Require(Has(s, 23) && !s.FormationComplete, "InvalidAbility"); ConfirmFormation(m, s, c.Formation); }
            else
            {
                Require(!s.SetupComplete, "SetupAlreadyCompleted");
                if (c.Kind == "SelectTargets") SelectTargets(m, s, c.PieceIds);
                else if (c.Kind == "ReplaceQueen")
                {
                    Require(Has(s, 27) && c.PieceKind is "Rook" or "Bishop" or "Knight", "InvalidAbilityTarget");
                    var queen = Piece(m, s.OriginalQueen); queen.Kind = c.PieceKind!; s.OriginalQueen = null;
                }
                else if (c.Kind == "HideKing") { Require(Has(s, 13), "InvalidAbility"); HideKing(m, s, c.TargetPieceId, true); }
                else throw new OnlineException("InvalidAbility");
                s.SetupComplete = true;
            }
        }
        CompleteSetup(m);
    }
    private static void SelectTargets(OnlineMatch m, ChaosSide s, List<int> ids)
    {
        var selected = ids.Select(id => Piece(m, id)).ToList();
        Require(selected.Select(p => p.Id).Distinct().Count() == selected.Count && selected.All(p => p.Team == s.Team), "InvalidAbilityTarget");
        if (Has(s, 0))
        {
            int count = Math.Min(3, m.Board.Count(p => p.Team == s.Team && p.Kind == "Pawn"));
            Require(selected.Count == count && selected.All(p => p.Kind == "Pawn"), "InvalidAbilityTarget"); s.CommandantPawns = ids.ToList();
        }
        else if (Has(s, 2))
        { Require(selected.Count == 1 && selected[0].Kind == "Knight", "InvalidAbilityTarget"); s.FreestyleKnight = selected[0].Id; }
        else if (Has(s, 3))
        {
            Require(selected.Count == 2 && selected.Count(p => p.Kind == "Knight") == 1 && selected.Count(p => p.Kind == "Bishop") == 1, "InvalidAbilityTarget");
            s.SwappedKnight = selected.Single(p => p.Kind == "Knight").Id; s.SwappedBishop = selected.Single(p => p.Kind == "Bishop").Id;
        }
        else throw new OnlineException("InvalidAbility");
    }
    private void ConfirmFormation(OnlineMatch m, ChaosSide s, List<FormationPlacement> placements)
    {
        var army = m.Board.Where(p => p.Team == s.Team).ToList();
        Require(placements.Count == army.Count && placements.Select(p => p.PieceId).Distinct().Count() == army.Count &&
            placements.Select(p => p.Square).Distinct().Count() == army.Count && placements.All(p => army.Any(a => a.Id == p.PieceId)), "InvalidFormation");
        foreach (var p in placements) { var square = GameRules.Square(p.Square); Require(Available(m, square) && Home(square, s.Team) &&
            !m.Board.Any(x => x.Team != s.Team && x.Square == p.Square), "InvalidFormation"); }
        foreach (var p in placements) Piece(m, p.PieceId).Square = p.Square;
        Require(!InCheck(m, s.Team), "KingInCheck");
        bool unchanged = s.OriginalFormation.All(p => Piece(m, p.PieceId).Square == p.Square);
        s.FormationComplete = true; s.FormationDeadline = null;
        if (unchanged) { s.BuffId = Pick(Tiers[1]); InitialEffects(m, s); }
    }
    private void CompleteSetup(OnlineMatch m)
    {
        var a = m.Chaos!;
        if (a.Phase == "Setup" && a.Sides.All(s => s.SetupComplete && (!Has(s, 23) || s.FormationComplete)))
        { a.Phase = "Playing"; BeginTurn(m); RecordPosition(m); }
    }
    private static void SwapRelocate(OnlineMatch m, OnlinePiece p, Square to)
    {
        var other = At(m, to); if (other is not null) { other.Square = p.Square; other.HasMoved = true; }
        p.Square = to.ToString(); p.HasMoved = true;
    }
    private static OnlinePiece? RealKing(OnlineMatch m, string team) => m.Board.FirstOrDefault(p => p.Team == team && p.Kind == "King" && !ReadEffect(m, p).Decoy);
    private static AramPieceContext Context(OnlineMatch m, OnlinePiece p)
    {
        var s = Side(m, p.Team); var e = ReadEffect(m, p);
        return new(s.BuffId.HasValue ? (AramBuffs)(1UL << s.BuffId.Value) : 0,
            s.CommandantPawns.Contains(p.Id) || e.Commandant, s.SwappedKnight == p.Id && s.SwapActive && !s.DoppelDisabled,
            s.SwappedBishop == p.Id && s.SwapActive && !s.DoppelDisabled, s.OriginalQueen == p.Id,
            s.TeleportUses < 5 && s.CompletedTurns >= s.TeleportReady, e.Bloodthirsty,
            e.CannonUntil > m.Chaos!.CombinedPlies, e.Decoy, s.FreestyleKnight == p.Id, e.QueenMastery, e.TrapPawn,
            s.OriginalQueen == p.Id && Has(s, 4) && !s.BomberUsed);
    }
    private static bool InCheck(OnlineMatch m, string team)
    {
        if (Has(Side(m, team), 32)) return false;
        var king = RealKing(m, team); if (king is null) return true;
        var board = GameRules.Read(m).Board; var square = GameRules.Square(king.Square);
        foreach (var p in m.Board.Where(p => p.Team != team))
        {
            var from = GameRules.Square(p.Square); var state = board.GetPiece(from); var context = Context(m, p);
            if ((!AramRules.SuppressesStandardMovement(context) && MovementRules.Attacks(board, state, from, square)) ||
                AramRules.BuiltIn.Allows(board, state, from, square, context, true)) return true;
        }
        return false;
    }
    private static bool SafeRelocate(OnlineMatch m, OnlinePiece p, Square to)
    {
        string from = p.Square; p.Square = to.ToString(); bool safe = !InCheck(m, p.Team); p.Square = from; return safe;
    }
}
