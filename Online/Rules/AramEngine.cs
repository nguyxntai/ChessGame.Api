using System.Security.Cryptography;
using ChessButWeird.Domain;

namespace ChessGame.Api.Online;

/// <summary>ARAM v2 mirrors the current local Unity rules, not its six-buff legacy wire protocol.</summary>
public sealed class AramEngine
{
    public static readonly int[][] Tiers = { new[] { 0, 1, 2, 6, 7, 8, 9 },
        new[] { 3, 4, 10, 11, 12, 13, 14 }, new[] { 5, 15, 16, 17, 18, 19, 20 }, new[] { 21, 22, 23, 24, 25 } };
    private static int Random(int count) => RandomNumberGenerator.GetInt32(count);
    private static bool Chance(int percent) => Random(100) < percent;
    private static T Pick<T>(IReadOnlyList<T> values) => values[Random(values.Count)];
    private static bool Has(AramSide side, int id) => side.BuffId == id;
    private static AramSide Side(OnlineMatch m, string team) => m.Aram!.Sides.Single(s => s.Team == team);
    private static bool Home(Square s, string team, int ranks = 4) => team == "White" ? s.Rank < ranks : s.Rank >= 8 - ranks;
    private static int Back(string team) => team == "White" ? 0 : 7;
    private static OnlinePiece? At(OnlineMatch m, Square s) => m.Board.FirstOrDefault(p => p.Square == s.ToString());
    private static OnlinePiece Piece(OnlineMatch m, int? id) => m.Board.FirstOrDefault(p => p.Id == id)
        ?? throw new OnlineException("InvalidAbilityTarget");
    private static OnlinePiece King(OnlineMatch m, string team) => m.Board.First(p => p.Team == team && p.Kind == "King" && !Effect(m, p).Decoy);
    private static AramPieceEffect Effect(OnlineMatch m, OnlinePiece p)
    {
        var side = Side(m, p.Team);
        var value = side.Effects.FirstOrDefault(e => e.PieceId == p.Id);
        if (value is null) { value = new AramPieceEffect { PieceId = p.Id }; side.Effects.Add(value); }
        return value;
    }
    private static bool Available(OnlineMatch m, Square s) => s.IsValid && s.File >= m.Aram!.CollapsedFiles && s.File < 8 - m.Aram.CollapsedFiles;
    private static List<Square> Empty(OnlineMatch m, string? team = null, int ranks = 4) =>
        (from y in Enumerable.Range(0, 8) from x in Enumerable.Range(0, 8)
         let s = new Square(x, y) where Available(m, s) && At(m, s) is null && (team is null || Home(s, team, ranks)) select s).ToList();

    public void Initialize(OnlineMatch match, DateTime now)
    {
        var pool = Pick(Tiers);
        match.Aram = new AramState { SetupDeadline = match.ReadyDeadline };
        foreach (var team in new[] { "White", "Black" })
            match.Aram.Sides.Add(new AramSide { Team = team, DraftOptions = pool.OrderBy(_ => Random(int.MaxValue)).Take(3).ToList(),
                OriginalQueen = match.Board.First(p => p.Team == team && p.Kind == "Queen").Id });
        // Canonical colliders are independent of cosmetic mesh bounds. Unity must use these for online rifle aiming.
        foreach (var kind in Enum.GetNames<PieceKind>())
            match.Aram.Hitboxes.Add(new PieceHitbox { Kind = kind, HalfWidth = .3, HalfDepth = .3,
                Height = kind is "King" or "Queen" ? 1.2 : kind == "Pawn" ? .65 : .9 });
    }
    public bool SetupDone(OnlineMatch m) => m.Aram is null || m.Aram.Phase == "Playing";
    public void SetupAbility(OnlineMatch m, string team, AbilityCommand c, DateTime now)
    {
        var side = Side(m, team);
        if (m.Aram!.Phase == "Playing") throw new OnlineException("SetupAlreadyCompleted");
        if (now >= m.Aram.SetupDeadline) throw new OnlineException("SetupExpired");
        switch (c.Kind)
        {
            case "SelectBuff":
                if (side.BuffId is not null || c.BuffId is null || !side.DraftOptions.Contains(c.BuffId.Value))
                    throw new OnlineException("InvalidBuffSelection");
                side.BuffId = c.BuffId;
                side.SetupComplete = side.BuffId is not (0 or 3);
                if (m.Aram.Sides.All(s => s.BuffId is not null))
                {
                    foreach (var s in m.Aram.Sides) InitialEffects(m, s);
                    if (m.Aram.Sides.Any(s => Has(s, 23)))
                        m.ReadyDeadline = m.Aram.SetupDeadline = now.AddSeconds(180);
                    foreach (var s in m.Aram.Sides.Where(s => Has(s, 23))) s.FormationDeadline = now.AddSeconds(120);
                    m.Aram.Phase = "Setup";
                }
                break;
            case "SelectTargets":
                if (m.Aram.Phase != "Setup" || side.SetupComplete) throw new OnlineException("InvalidSetupPhase");
                var selected = c.PieceIds.Select(id => Piece(m, id)).ToList();
                if (selected.Select(p => p.Id).Distinct().Count() != selected.Count || selected.Any(p => p.Team != team))
                    throw new OnlineException("InvalidAbilityTarget");
                if (Has(side, 0))
                {
                    int required = Math.Min(3, m.Board.Count(p => p.Team == team && p.Kind == "Pawn"));
                    if (selected.Count != required || selected.Any(p => p.Kind != "Pawn")) throw new OnlineException("InvalidAbilityTarget");
                    side.CommandantPawns = selected.Select(p => p.Id).ToList();
                }
                else if (Has(side, 3))
                {
                    var knight = selected.FirstOrDefault(p => p.Kind == "Knight");
                    var bishop = selected.FirstOrDefault(p => p.Kind == "Bishop");
                    if (selected.Count != 2 || knight is null || bishop is null) throw new OnlineException("InvalidAbilityTarget");
                    side.SwappedKnight = knight.Id; side.SwappedBishop = bishop.Id;
                }
                else throw new OnlineException("InvalidAbility");
                side.SetupComplete = true;
                break;
            case "ConfirmFormation":
                if (m.Aram.Phase != "Setup" || !Has(side, 23) || side.FormationComplete) throw new OnlineException("InvalidAbility");
                ConfirmFormation(m, side, c.Formation);
                break;
            default: throw new OnlineException("InvalidSetupPhase");
        }
        CompleteSetup(m);
    }
    private void CompleteSetup(OnlineMatch m)
    {
        if (m.Aram!.Phase != "Playing" && m.Aram.Sides.All(s => s.BuffId is not null && s.SetupComplete && (!Has(s, 23) || s.FormationComplete)))
        { m.Aram.Phase = "Playing"; BeginTurn(m); }
    }
    private void InitialEffects(OnlineMatch m, AramSide side)
    {
        var army = m.Board.Where(p => p.Team == side.Team).ToList();
        if (Has(side, 24) || Has(side, 25))
        {
            foreach (var p in army.Where(p => p.Kind != "King")) Remove(m, p, false);
            if (Has(side, 24)) foreach (var s in Empty(m, side.Team)) Spawn(m, side.Team, "Pawn", s);
        }
        if (Has(side, 19)) foreach (var p in army.Where(p => p.Kind is not ("King" or "Queen")))
        { p.Kind = Pick(new[] { "Pawn", "Knight", "Bishop", "Rook" }); p.HasMoved = true; }
        if (Has(side, 17))
        {
            var squares = Empty(m, side.Team, 3).Where(s => s.Rank == (side.Team == "White" ? 2 : 5)).ToList();
            if (squares.Count > 0) Effect(m, Spawn(m, side.Team, "Queen", Pick(squares))).Unstable = true;
        }
        if (Has(side, 16)) foreach (var p in army.Where(p => p.Kind == "Rook")) Effect(m, p).CannonUntil = 10;
        if (Has(side, 23)) side.OriginalFormation = army.Select(p => new FormationPlacement(p.Id, p.Square)).ToList();
    }
    private void ConfirmFormation(OnlineMatch m, AramSide side, List<FormationPlacement> placements)
    {
        var army = m.Board.Where(p => p.Team == side.Team).ToList();
        if (placements.Count != army.Count || placements.Select(p => p.PieceId).Distinct().Count() != army.Count ||
            placements.Select(p => p.Square).Distinct().Count() != army.Count || placements.Any(p => !army.Any(a => a.Id == p.PieceId)))
            throw new OnlineException("InvalidFormation");
        foreach (var p in placements)
        {
            var s = GameRules.Square(p.Square);
            if (!Available(m, s) || !Home(s, side.Team) || m.Board.Any(x => x.Team != side.Team && x.Square == p.Square))
                throw new OnlineException("InvalidFormation");
        }
        foreach (var p in placements) Piece(m, p.PieceId).Square = p.Square;
        if (InCheck(m, side.Team)) throw new OnlineException("KingInCheck");
        bool unchanged = side.OriginalFormation.All(p => Piece(m, p.PieceId).Square == p.Square);
        side.FormationComplete = true; side.FormationDeadline = null;
        if (unchanged)
        {
            side.BuffId = Pick(Tiers[1]); side.SetupComplete = side.BuffId != 3;
            InitialEffects(m, side);
        }
    }
    private readonly struct Context(OnlineMatch m) : IBuffContextProvider
    {
        public AramPieceContext GetContext(PieceState piece)
        {
            var side = Side(m, piece.Team.ToString());
            var e = side.Effects.FirstOrDefault(x => x.PieceId == piece.Id);
            return new AramPieceContext(side.BuffId is null ? AramBuffs.None : (AramBuffs)(1 << side.BuffId.Value),
                side.CommandantPawns.Contains(piece.Id), side.SwappedKnight == piece.Id && side.SwapActive,
                side.SwappedBishop == piece.Id && side.SwapActive, side.OriginalQueen == piece.Id,
                side.TeleportUses < 5 && side.CompletedTurns >= side.TeleportReady,
                e?.Bloodthirsty == true, e?.CannonUntil > side.CompletedTurns, e?.Decoy == true);
        }
    }
    private bool InCheck(OnlineMatch m, string team)
    {
        var king = m.Board.FirstOrDefault(p => p.Team == team && p.Kind == "King" && !Effect(m, p).Decoy);
        return king is null || KingSafetyRules.IsAttacked(GameRules.Read(m).Board, GameRules.Square(king.Square),
            Enum.Parse<Team>(GameRules.Other(team)), new Context(m));
    }
    private bool SafeRelocate(OnlineMatch m, OnlinePiece p, Square to)
    {
        var previous = p.Square;
        p.Square = to.ToString();
        var safe = !InCheck(m, p.Team);
        p.Square = previous;
        return safe;
    }
    public void ApplyMove(OnlineMatch m, Move move)
    {
        if (!TryMove(m, move, out var next)) throw new OnlineException("InvalidMove");
        // TryMove's copy includes all ARAM effects. Copy back only after complete validation.
        m.Board = next.Board; m.Turn = next.Turn; m.EnPassantTarget = next.EnPassantTarget;
        m.HalfMoveClock = next.HalfMoveClock; m.FullMoveNumber = next.FullMoveNumber; m.Aram = next.Aram;
    }
    private bool TryMove(OnlineMatch original, Move move, out OnlineMatch next, bool simulate = false)
    {
        next = OnlineJson.Clone(original);
        var m = next;
        var a = m.Aram!;
        if (a.Phase != "Playing" || a.Rifle is not null || a.EscapeTeam is not null || a.Deployments.Count > 0 ||
            !Available(m, move.From) || !Available(m, move.To) || move.From == move.To) return false;
        var moving = At(m, move.From); var captured = At(m, move.To);
        if (moving is null || moving.Team != m.Turn || (a.ForcedPawn is not null && moving.Id != a.ForcedPawn)) return false;
        var side = Side(m, moving.Team); var effect = Effect(m, moving);
        if (captured is not null && captured.Kind == "King" && !Effect(m, captured).Decoy) return false;
        bool allied = captured?.Team == moving.Team;
        if (allied && !(moving.Kind == "Pawn" && captured!.Kind == "Pawn" && Has(side, 6))) return false;
        bool promotion = moving.Kind == "Pawn" && move.To.Rank == (moving.Forward > 0 ? 7 : 0);
        if (promotion != move.Promotion.HasValue || move.Promotion is PieceKind.King or PieceKind.Pawn) return false;
        var state = GameRules.Read(m);
        var p = state.Board.GetPiece(move.From);
        var context = new Context(m).GetContext(p);
        bool castle = CanCastle(m, moving, move);
        bool ep = moving.Kind == "Pawn" && move.To.ToString() == m.EnPassantTarget && captured is null &&
            Math.Abs(move.To.File - move.From.File) == 1 && move.To.Rank - move.From.Rank == moving.Forward &&
            move.From.Rank == (moving.Forward > 0 ? 4 : 3);
        if (ep)
        {
            captured = At(m, new Square(move.To.File, move.From.Rank));
            if (captured is null || captured.Team == moving.Team || captured.Kind != "Pawn") return false;
        }
        bool pattern = !AramRules.SuppressesStandardMovement(context) && !allied && MovementRules.IsLegalPattern(state.Board, p, move.From, move.To);
        bool extension = AramRules.BuiltIn.Allows(state.Board, p, move.From, move.To, context);
        if (!pattern && !extension && !castle && !ep) return false;
        bool teleported = moving.Id == side.OriginalQueen && Has(side, 5) && !pattern;
        bool extra = false;
        if (captured is not null)
        {
            CaptureEffects(m, moving, captured, move.From, move.To, simulate);
            extra = !simulate && !allied && moving.Kind == "Pawn" && Has(side, 8) && side.ExtraUses < 2 && Chance(30);
            if (extra) side.ExtraUses++;
            Remove(m, captured);
        }
        moving.Square = move.To.ToString(); moving.HasMoved = true;
        if (teleported) { side.TeleportUses++; side.TeleportReady = side.CompletedTurns + 5; }
        if (castle)
        {
            var rook = At(m, new Square(move.To.File == 6 ? 7 : 0, move.From.Rank))!;
            rook.Square = new Square(move.To.File == 6 ? 5 : 3, move.From.Rank).ToString(); rook.HasMoved = true;
        }
        if (captured is not null && captured.Id == Side(m, captured.Team).OriginalQueen && Has(Side(m, captured.Team), 4))
            Detonate(m, captured, move.To);
        if (effect.Decoy && captured is not null && !allied) { effect.Decoy = false; moving.Kind = captured.Kind; }
        bool wasPawn = p.Kind == PieceKind.Pawn;
        if (promotion && m.Board.Contains(moving)) moving.Kind = move.Promotion!.Value.ToString();
        if (moving.Kind == "Pawn" && Has(side, 11) && move.To.Rank == Back(moving.Team) && move.To.Rank - move.From.Rank == -moving.Forward)
        { Remove(m, moving, false); a.Mines.Add(new AramMarker { Square = move.To.ToString(), Team = moving.Team }); }
        Land(m, moving, move.To);
        if (InCheck(m, moving.Team)) return false;
        m.EnPassantTarget = wasPawn && Math.Abs(move.To.Rank - move.From.Rank) == 2
            ? new Square(move.From.File, (move.From.Rank + move.To.Rank) / 2).ToString() : "-";
        m.HalfMoveClock = wasPawn || captured is not null ? 0 : m.HalfMoveClock + 1;
        if (extra && moving.Kind == "Pawn" && m.Board.Contains(moving))
        {
            a.ForcedPawn = moving.Id;
            if (HasLegalMove(m)) return true;
        }
        a.ForcedPawn = null;
        if (!simulate) CompleteTurn(m, moving.Team);
        else m.Turn = GameRules.Other(moving.Team);
        return true;
    }
    private bool CanCastle(OnlineMatch m, OnlinePiece king, Move move)
    {
        if (king.Kind != "King" || Effect(m, king).Decoy || move.From != new Square(4, Back(king.Team)) ||
            move.To.Rank != move.From.Rank || move.To.File is not (2 or 6)) return false;
        var state = GameRules.Read(m); var rookSquare = new Square(move.To.File == 6 ? 7 : 0, move.From.Rank);
        var rook = At(m, rookSquare);
        if (rook is null || rook.Team != king.Team || rook.Kind != "Rook" || !Available(m, rookSquare) ||
            !MovementRules.IsPathClear(state.Board, move.From, rookSquare)) return false;
        var transit = new Square(move.To.File == 6 ? 5 : 3, move.From.Rank);
        bool inCheck = InCheck(m, king.Team), crossesCheck = !SafeRelocate(m, king, transit);
        int restrictions = (king.HasMoved ? 1 : 0) + (rook.HasMoved ? 1 : 0) + (inCheck ? 1 : 0) + (crossesCheck ? 1 : 0);
        return restrictions <= (Has(Side(m, king.Team), 1) ? 1 : 0);
    }
    private void CaptureEffects(OnlineMatch m, OnlinePiece moving, OnlinePiece victim, Square from, Square to, bool simulate = false)
    {
        var side = Side(m, moving.Team); var e = Effect(m, moving);
        if (e.CannonUntil is not null && !MovementRules.IsPathClear(GameRules.Read(m).Board, from, to)) e.CannonUntil = side.CompletedTurns + 11;
        if (victim.Team == moving.Team) { if (Has(side, 6)) e.Bloodthirsty = true; return; }
        if (Has(Side(m, victim.Team), 22)) e.InfectedUntil = side.CompletedTurns + 5;
        if (Has(side, 14)) side.Tickets += victim.Kind == "Pawn" ? 1 : victim.Kind == "Queen" ? 3 : 2;
        if (moving.Kind == "Bishop" && Has(side, 7) && victim.Kind != "Pawn" && Math.Max(Math.Abs(to.File - from.File), Math.Abs(to.Rank - from.Rank)) >= 4)
            e.SniperUntil = side.CompletedTurns + 6;
    }
    private static OnlinePiece Spawn(OnlineMatch m, string team, string kind, Square square, bool moved = true)
    {
        if (!Available(m, square) || At(m, square) is not null) throw new OnlineException("InvalidAbilityTarget");
        var p = new OnlinePiece { Id = m.Aram!.NextPieceId++, Team = team, Kind = kind, Square = square.ToString(),
            Forward = team == "White" ? 1 : -1, HasMoved = moved };
        m.Board.Add(p); return p;
    }
    private static void Remove(OnlineMatch m, OnlinePiece p, bool releasePassenger = true)
    {
        if (!m.Board.Remove(p)) return;
        var effect = Effect(m, p);
        if (effect.Passenger && releasePassenger)
        { effect.Passenger = false; m.Aram!.Deployments.Add(new AramDeployment { Team = p.Team, Center = p.Square }); }
    }
    private void Detonate(OnlineMatch m, OnlinePiece queen, Square center)
    {
        var side = Side(m, queen.Team);
        if (!Has(side, 4) || side.OriginalQueen != queen.Id || side.BomberUsed) return;
        side.BomberUsed = true;
        foreach (var p in m.Board.ToList())
        {
            var square = GameRules.Square(p.Square);
            if (p.Kind != "King" && Math.Abs(square.File - center.File) <= 1 && Math.Abs(square.Rank - center.Rank) <= 1) Remove(m, p);
        }
    }
    private static void Land(OnlineMatch m, OnlinePiece p, Square to)
    {
        if (!m.Board.Contains(p)) return;
        var mine = m.Aram!.Mines.FirstOrDefault(x => x.Square == to.ToString() && x.Team != p.Team);
        if (mine is not null) { m.Aram.Mines.Remove(mine); Remove(m, p); return; }
        var crate = m.Aram.Crates.FirstOrDefault(x => x.Square == to.ToString());
        if (crate is null) return;
        m.Aram.Crates.Remove(crate);
        if (crate.Team == p.Team) m.Aram.Deployments.Add(new AramDeployment { Team = p.Team, Kind = crate.Kind });
    }
    private void CompleteTurn(OnlineMatch m, string team)
    {
        var side = Side(m, team); side.CompletedTurns++;
        if (side.CompletedTurns <= 15 && InCheck(m, team)) side.PeaceFailed = true;
        foreach (var p in m.Board.Where(p => p.Team == team).ToList())
        {
            var e = Effect(m, p);
            if (e.InfectedUntil is not null && side.CompletedTurns >= e.InfectedUntil) { Remove(m, p); continue; }
            if (e.Unstable && Chance(10))
            {
                side.Effects.Remove(e); p.Team = GameRules.Other(team); p.Forward = -p.Forward; p.HasMoved = true;
                // Unity replaces the queen, clearing infection/cannon/passenger state while retaining instability.
                Side(m, p.Team).Effects.Add(new AramPieceEffect { PieceId = p.Id, Unstable = true });
            }
        }
        if (team == "Black") m.FullMoveNumber++;
        m.Turn = GameRules.Other(team);
        BeginTurn(m);
    }
    private void BeginTurn(OnlineMatch m)
    {
        var a = m.Aram!; var side = Side(m, m.Turn); side.RollsThisTurn = 0;
        if (!m.Board.Any(p => p.Team == m.Turn && p.Kind == "King" && !Effect(m, p).Decoy)) return;
        if (InCheck(m, m.Turn))
        {
            if (side.CompletedTurns < 15) side.PeaceFailed = true;
            if (Has(side, 15) && !side.SubstituteUsed)
            {
                side.SubstituteUsed = true;
                var king = King(m, m.Turn); var origin = GameRules.Square(king.Square);
                var safe = Empty(m, m.Turn).Where(s => SafeRelocate(m, king, s)).ToList();
                if (safe.Count > 0)
                {
                    king.Square = Pick(safe).ToString(); king.HasMoved = true;
                    Effect(m, Spawn(m, m.Turn, "King", origin)).Decoy = true;
                }
            }
        }
        int round = 1 + a.Sides.Min(s => s.CompletedTurns);
        foreach (int milestone in new[] { 25, 50 }) if (round >= milestone && !a.LootRounds.Contains(milestone))
        {
            a.LootRounds.Add(milestone);
            foreach (var owner in a.Sides.Where(s => Has(s, 9)))
            {
                var empty = Empty(m).Where(s => a.Crates.All(c => c.Square != s.ToString())).ToList();
                if (empty.Count > 0) a.Crates.Add(new AramMarker { Team = owner.Team, Kind = milestone == 25 ? "Rook" : "Queen", Square = Pick(empty).ToString() });
            }
        }
        if (a.Sides.Any(s => Has(s, 18)))
        {
            a.CollapsedFiles = round >= 15 ? 2 : round >= 10 ? 1 : 0;
            foreach (var p in m.Board.ToList()) if (!Available(m, GameRules.Square(p.Square))) Remove(m, p);
            a.Mines.RemoveAll(x => !Available(m, GameRules.Square(x.Square)));
            a.Crates.RemoveAll(x => !Available(m, GameRules.Square(x.Square)));
        }
        a.Deployments.RemoveAll(d => DeploymentSquares(m, d).Count == 0);
    }
    private static List<Square> DeploymentSquares(OnlineMatch m, AramDeployment d)
    {
        var squares = d.Center is null ? Empty(m, d.Team) : Empty(m);
        if (d.Center is not null) { var c = GameRules.Square(d.Center); squares.RemoveAll(s => Math.Abs(s.File - c.File) > 1 || Math.Abs(s.Rank - c.Rank) > 1); }
        return squares;
    }
    private bool HasLegalMove(OnlineMatch m)
    {
        foreach (var p in m.Board.Where(p => p.Team == m.Turn))
            foreach (var to in Enumerable.Range(0, 64).Select(i => new Square(i % 8, i / 8)))
            {
                PieceKind? promotion = p.Kind == "Pawn" && to.Rank == (p.Forward > 0 ? 7 : 0) ? PieceKind.Queen : null;
                if (TryMove(m, new Move(GameRules.Square(p.Square), to, promotion), out _, true)) return true;
            }
        return false;
    }
    public (string? WinnerTeam, string Reason)? Outcome(OnlineMatch m)
    {
        var a = m.Aram!;
        bool white = m.Board.Any(p => p.Team == "White" && p.Kind == "King" && !Effect(m, p).Decoy);
        bool black = m.Board.Any(p => p.Team == "Black" && p.Kind == "King" && !Effect(m, p).Decoy);
        if (!white || !black) return (!white && !black ? null : white ? "White" : "Black", "KingDestroyed");
        if (a.Phase != "Playing" || a.Deployments.Count > 0 || a.Rifle is not null || a.EscapeTeam is not null) return null;
        if (HasLegalMove(m)) return null;
        if (!InCheck(m, m.Turn)) return (null, "Stalemate");
        var side = Side(m, m.Turn);
        if (Has(side, 20) && side.EscapeUses < 3)
        {
            var king = King(m, m.Turn); var from = GameRules.Square(king.Square);
            if (Empty(m).Any(s => Math.Abs(s.File - from.File) <= 2 && Math.Abs(s.Rank - from.Rank) <= 2 && SafeRelocate(m, king, s)))
            { a.EscapeTeam = m.Turn; return null; }
        }
        return (GameRules.Other(m.Turn), "Checkmate");
    }
    public void ApplyAbility(OnlineMatch m, string team, AbilityCommand c, DateTime now)
    {
        var a = m.Aram!; var side = Side(m, team);
        if (a.Phase != "Playing") { SetupAbility(m, team, c, now); return; }
        if (a.Deployments.Count > 0)
        {
            var d = a.Deployments[0];
            if (d.Team != team || c.Kind != "Deploy") throw new OnlineException("PendingDecision");
            var square = GameRules.Square(c.Target);
            if (!DeploymentSquares(m, d).Contains(square)) throw new OnlineException("InvalidAbilityTarget");
            a.Deployments.RemoveAt(0);
            var kind = d.Kind == "Pawn" && square.Rank == (team == "White" ? 7 : 0) ? "Queen" : d.Kind;
            Spawn(m, team, kind, square, true);
            return;
        }
        if (team != m.Turn) throw new OnlineException("NotYourTurn");
        if (a.ForcedPawn is not null) throw new OnlineException("ForcedPawnMove");
        if (a.EscapeTeam is not null && c.Kind != "Escape") throw new OnlineException("PendingDecision");
        if (a.Rifle is not null && c.Kind is not ("FireRifle" or "ExitRifle")) throw new OnlineException("RifleActive");
        switch (c.Kind)
        {
            case "ToggleSwap":
                Require(Has(side, 3) && side.CompletedTurns >= side.SwapReady);
                Piece(m, side.SwappedKnight); Piece(m, side.SwappedBishop);
                side.SwapActive = !side.SwapActive; Require(!InCheck(m, team), "KingInCheck");
                side.SwapReady = side.CompletedTurns + 5; break;
            case "HideKing":
                Require(Has(side, 13) && side.CompletedTurns >= side.HidingReady);
                var target = Piece(m, c.TargetPieceId); var king = King(m, team);
                Require(target.Team == team && target.Id != king.Id && GameRules.Square(target.Square).Rank == Back(team), "InvalidAbilityTarget");
                (target.Square, king.Square) = (king.Square, target.Square);
                target.HasMoved = king.HasMoved = true; Require(!InCheck(m, team), "KingInCheck");
                side.HidingReady = side.CompletedTurns + 10; break;
            case "LoadPawn":
                Require(Has(side, 12)); var rook = Piece(m, c.PieceId); var pawn = Piece(m, c.TargetPieceId);
                Require(rook.Team == team && rook.Kind == "Rook" && !Effect(m, rook).Passenger && pawn.Team == team && pawn.Kind == "Pawn", "InvalidAbilityTarget");
                Remove(m, pawn, false); Require(!InCheck(m, team), "KingInCheck"); Effect(m, rook).Passenger = true; break;
            case "Recruit":
                Require(Has(side, 10) && side.CompletedTurns >= 15 && !side.PeaceFailed && !side.PeaceUsed);
                var enemy = Piece(m, c.TargetPieceId); var deployment = GameRules.Square(c.Target);
                Require(enemy.Team != team && enemy.Kind is not ("King" or "Queen") && Empty(m, team, 3).Contains(deployment), "InvalidAbilityTarget");
                var recruitedKind = enemy.Kind; Remove(m, enemy, false); Spawn(m, team, recruitedKind, deployment, true);
                side.PeaceUsed = true; Require(!InCheck(m, team), "KingInCheck"); break;
            case "BattleGacha":
                Require(Has(side, 14) && side.Tickets > 0 && side.RollsThisTurn < 2);
                var empty = Empty(m); Require(empty.Count > 0, "InvalidAbilityTarget");
                int roll = Random(100); var gachaKind = roll < 70 ? "Pawn" : roll < 80 ? "Knight" : roll < 90 ? "Bishop" : roll < 99 ? "Rook" : "Queen";
                var dropped = Spawn(m, team, gachaKind, Pick(empty), true);
                side.Tickets--; side.RollsThisTurn++; Land(m, dropped, GameRules.Square(dropped.Square)); break;
            case "Snipe":
                Require(Has(side, 7)); var bishop = Piece(m, c.PieceId); var victim = Piece(m, c.TargetPieceId);
                var from = GameRules.Square(bishop.Square); var to = GameRules.Square(victim.Square);
                Require(bishop.Team == team && bishop.Kind == "Bishop" && Effect(m, bishop).SniperUntil > side.CompletedTurns &&
                    victim.Team != team && victim.Kind != "King" && Math.Abs(to.File - from.File) == Math.Abs(to.Rank - from.Rank) &&
                    MovementRules.IsPathClear(GameRules.Read(m).Board, from, to), "InvalidAbilityTarget");
                CaptureEffects(m, bishop, victim, from, to); Effect(m, bishop).SniperUntil = null;
                Remove(m, victim, false); Detonate(m, victim, to); Require(!InCheck(m, team), "KingInCheck");
                m.EnPassantTarget = "-"; m.HalfMoveClock = 0; CompleteTurn(m, team); break;
            case "Escape":
                Require(Has(side, 20) && a.EscapeTeam == team && side.EscapeUses < 3);
                var escapingKing = King(m, team); var origin = GameRules.Square(escapingKing.Square); var destination = GameRules.Square(c.Target);
                Require(Empty(m).Contains(destination) && Math.Abs(destination.File - origin.File) <= 2 && Math.Abs(destination.Rank - origin.Rank) <= 2 &&
                    SafeRelocate(m, escapingKing, destination), "InvalidAbilityTarget");
                escapingKing.Square = destination.ToString(); escapingKing.HasMoved = true; side.EscapeUses++; a.EscapeTeam = null; break;
            case "AimRifle":
                Require(Has(side, 25) && side.CompletedTurns >= side.RifleReady && a.Rifle is null);
                var shooter = GameRules.Square(King(m, team).Square);
                a.Rifle = new RifleSession { Team = team, Deadline = now.AddSeconds(15), OriginX = shooter.File, OriginY = .95, OriginZ = shooter.Rank }; break;
            case "ExitRifle": Require(a.Rifle?.Team == team); a.Rifle = null; break;
            case "FireRifle": FireRifle(m, side, c, now); break;
            default: throw new OnlineException("InvalidAbility", 400);
        }
    }
    private static void Require(bool condition, string code = "InsufficientAbilityResource")
    { if (!condition) throw new OnlineException(code); }
    private void FireRifle(OnlineMatch m, AramSide side, AbilityCommand c, DateTime now)
    {
        var a = m.Aram!; var rifle = a.Rifle;
        Require(Has(side, 25) && rifle?.Team == side.Team && now < rifle.Deadline, "RifleExpired");
        Require(c.Yaw.HasValue && c.Pitch.HasValue && double.IsFinite(c.Yaw.Value) && double.IsFinite(c.Pitch.Value) &&
            c.Pitch.Value >= -80 && c.Pitch.Value <= 80 && Math.Abs(c.Yaw.Value) <= 36000, "InvalidAim");
        double yaw = c.Yaw!.Value * Math.PI / 180, pitch = c.Pitch!.Value * Math.PI / 180;
        var direction = new[] { Math.Sin(yaw) * Math.Cos(pitch), -Math.Sin(pitch), Math.Cos(yaw) * Math.Cos(pitch) };
        var origin = new[] { rifle!.OriginX, rifle.OriginY, rifle.OriginZ };
        OnlinePiece? nearest = null; double distance = 500;
        // The board plane is an occluder too; a downward shot cannot hit pieces behind it.
        if (direction[1] < 0) distance = Math.Min(distance, -origin[1] / direction[1]);
        foreach (var p in m.Board.Where(p => p.Id != King(m, side.Team).Id))
        {
            var square = GameRules.Square(p.Square); var box = a.Hitboxes.Single(x => x.Kind == p.Kind);
            double? hit = RayBox(origin, direction, new[] { square.File - box.HalfWidth, 0, square.Rank - box.HalfDepth },
                new[] { square.File + box.HalfWidth, box.Height, square.Rank + box.HalfDepth });
            if (hit is not null && hit < distance) { distance = hit.Value; nearest = p; }
        }
        if (nearest is not null && nearest.Team != side.Team && nearest.Kind is not ("King" or "Queen")) Remove(m, nearest);
        a.Rifle = null; side.RifleReady = side.CompletedTurns + 3;
    }
    private static double? RayBox(double[] origin, double[] dir, double[] min, double[] max)
    {
        double enter = 0, exit = 500;
        for (int i = 0; i < 3; i++)
        {
            if (Math.Abs(dir[i]) < 1e-9) { if (origin[i] < min[i] || origin[i] > max[i]) return null; continue; }
            double a = (min[i] - origin[i]) / dir[i], b = (max[i] - origin[i]) / dir[i];
            enter = Math.Max(enter, Math.Min(a, b)); exit = Math.Min(exit, Math.Max(a, b));
            if (enter > exit) return null;
        }
        return enter;
    }
    public bool Tick(OnlineMatch m, DateTime now)
    {
        bool changed = false;
        if (m.Aram is { Phase: "Setup" } setup)
        {
            foreach (var side in setup.Sides.Where(s => Has(s, 23) && !s.FormationComplete && s.FormationDeadline <= now).ToList())
            { ConfirmFormation(m, side, side.OriginalFormation); changed = true; }
            if (changed) CompleteSetup(m);
        }
        if (m.Aram?.Rifle is { } rifle && now >= rifle.Deadline)
        { Side(m, rifle.Team).RifleReady = Side(m, rifle.Team).CompletedTurns + 3; m.Aram.Rifle = null; changed = true; }
        return changed;
    }
}
