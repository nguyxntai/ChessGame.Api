using ChessButWeird.Domain;
using ChessButWeird.Domain.Chaos35;

namespace ChessGame.Api.Online;

internal sealed partial class Chaos35Engine
{
    private static OnlinePiece Spawn(OnlineMatch m, string team, string kind, Square to)
    {
        Require(Available(m, to) && At(m, to) is null, "InvalidAbilityTarget");
        var p = new OnlinePiece { Id = m.Chaos!.NextPieceId++, Team = team, Kind = kind, Square = to.ToString(), HasMoved = true, Forward = team == "White" ? 1 : -1 };
        m.Board.Add(p);
        if (kind == "Pawn" && to.Rank == (p.Forward > 0 ? 7 : 0) && Has(Side(m, GameRules.Other(team)), 33)) Effect(m, p).TrapPawn = true;
        return p;
    }
    private void Remove(OnlineMatch m, OnlinePiece p, bool passenger = true, bool explode = true)
    {
        if (!m.Board.Remove(p)) return;
        var e = ReadEffect(m, p);
        if (passenger && e.Passenger)
        {
            e.Passenger = false;
            m.Chaos!.Deployments.Add(new() { Team = p.Team, Kind = "Pawn", Center = p.Square, Traits = e.PassengerTraits });
        }
        if (explode) Detonate(m, p, GameRules.Square(p.Square));
    }
    private void Detonate(OnlineMatch m, OnlinePiece queen, Square center)
    {
        var s = Side(m, queen.Team);
        if (queen.Kind != "Queen" || !Has(s, 4) || s.OriginalQueen != queen.Id || s.BomberUsed) return;
        s.BomberUsed = true;
        foreach (var p in m.Board.ToList()) if (p.Kind != "King" && Nearby(p, center, 1)) Remove(m, p);
    }
    private static void RewardCapture(OnlineMatch m, string team, OnlinePiece victim)
    {
        if (victim.Team == team) return;
        var s = Side(m, team);
        GameRules.RecordCapture(m, team, victim);
        if (Has(s, 14) && victim.Kind != "Pawn") s.Tickets++;
        if (Has(s, 31)) s.Credit += AramBalanceRules.CapturePoints(Enum.Parse<PieceKind>(victim.Kind));
    }
    private static void CaptureEffects(OnlineMatch m, OnlinePiece p, OnlinePiece victim, Square from, Square to)
    {
        var s = Side(m, p.Team); var e = Effect(m, p); var a = m.Chaos!;
        if (e.CannonUntil.HasValue && !MovementRules.IsPathClear(GameRules.Read(m).Board, from, to)) e.CannonUntil = AramBalanceRules.ExpiryAfterCapture(a.CombinedPlies, 10);
        if (victim.Team == p.Team) { if (Has(s, 6)) e.Bloodthirsty = true; return; }
        RewardCapture(m, p.Team, victim);
        if ((p.Kind != "King" || e.Decoy) && Has(Side(m, victim.Team), 22)) e.InfectedUntil = AramBalanceRules.ExpiryAfterCapture(a.CombinedPlies, 4);
        if (Has(s, 26) && p.Kind != "King" && p.Kind == victim.Kind && !s.QuestCaptures.Contains(p.Kind)) s.QuestCaptures.Add(p.Kind);
        if (Has(s, 29) && p.Kind != "Queen" && victim.Kind == "Queen") e.QueenMastery = true;
        if (Has(s, 7) && p.Kind == "Bishop" && victim.Kind != "Pawn" && Math.Max(Math.Abs(to.File - from.File), Math.Abs(to.Rank - from.Rank)) >= 4)
            e.SniperUntil = AramBalanceRules.ExpiryAfterCapture(a.CombinedPlies, 4);
    }
    private void Land(OnlineMatch m, OnlinePiece p, Square to)
    {
        if (!m.Board.Contains(p)) return;
        var a = m.Chaos!; var mine = a.Mines.FirstOrDefault(x => x.Square == to.ToString() && x.Team != p.Team);
        if (mine is not null) { a.Mines.Remove(mine); Remove(m, p); return; }
        var crate = a.Crates.FirstOrDefault(x => x.Square == to.ToString());
        if (crate is null) return;
        a.Crates.Remove(crate);
        if (crate.Team == p.Team) a.Deployments.Add(new() { Team = p.Team, Kind = crate.Kind });
    }
    private static void Promoted(OnlineMatch m, OnlinePiece p)
    {
        var s = Side(m, p.Team); s.QuestPromoted = true;
        Effect(m, p).PromotedQueen = p.Kind == "Queen";
        QueueQuest(m, s);
        if (Has(s, 34)) m.Chaos!.Deployments.Add(new() { Team = p.Team, Kind = p.Kind, Action = "Redeploy", PieceId = p.Id, Ranks = 0 });
    }
    private static void QueueQuest(OnlineMatch m, ChaosSide s)
    {
        if (!Has(s, 26) || s.QuestRewarded || !s.QuestPromoted || s.QuestCaptures.Count < 5) return;
        s.QuestRewarded = true;
        bool two = m.Board.Count(p => p.Team == s.Team && p.Kind == "Pawn") >= 2;
        m.Chaos!.Deployments.Add(new() { Team = s.Team, Kind = "Queen", Ranks = 0, Action = two ? "QuestPawns" : "Deploy", Remaining = two ? 2 : 1 });
    }
    private static void ObserveChecks(OnlineMatch m)
    {
        if (m.Chaos!.CombinedPlies >= 14) return;
        foreach (var s in m.Chaos.Sides) if (InCheck(m, s.Team)) s.PeaceFailed = true;
    }
    private void AdvancePly(OnlineMatch m)
    {
        var a = m.Chaos!; a.CombinedPlies++;
        foreach (var p in m.Board.ToList())
        {
            var e = Effect(m, p);
            if (e.SniperUntil <= a.CombinedPlies) e.SniperUntil = null;
            if (e.CannonUntil <= a.CombinedPlies) e.CannonUntil = null;
            if (e.InfectedUntil <= a.CombinedPlies) Remove(m, p);
        }
        if (a.CombinedPlies <= 14) foreach (var s in a.Sides) if (InCheck(m, s.Team)) s.PeaceFailed = true;
        foreach (var s in a.Sides)
        {
            if (Has(s, 3) && (!m.Board.Any(p => p.Team == s.Team && p.Kind == "Knight") || !m.Board.Any(p => p.Team == s.Team && p.Kind == "Bishop")))
            { s.DoppelDisabled = true; s.SwapActive = false; s.SwappedKnight = s.SwappedBishop = null; }
            QueueQuest(m, s);
        }
        foreach (var p in m.Board.Where(p => ReadEffect(m, p).Unstable).ToList())
        {
            var e = Effect(m, p);
            if (Random(100) < (int)Math.Round(100 * AramBalanceRules.BetrayalChance(e.BetrayalChecks)))
            { Remove(m, p); a.Deployments.Add(new() { Team = GameRules.Other(p.Team), Kind = "Queen", Ranks = 3 }); }
            else e.BetrayalChecks++;
        }
        foreach (int milestone in new[] { 25, 50 }) if (a.CombinedPlies >= milestone && !a.LootRounds.Contains(milestone))
        {
            a.LootRounds.Add(milestone);
            foreach (var s in a.Sides.Where(s => Has(s, 9)))
            {
                var eligible = (from x in Enumerable.Range(a.CollapsedFiles, 8 - 2 * a.CollapsedFiles) from y in Enumerable.Range(0, 8)
                    let square = new Square(x, y) let occupant = At(m, square)
                    where a.Crates.All(c => c.Square != square.ToString()) && (milestone == 50 || occupant?.Kind != "King") select square).ToList();
                if (eligible.Count == 0) continue;
                var to = Pick(eligible); var victim = At(m, to);
                if (victim is not null) { RewardCapture(m, s.Team, victim); Remove(m, victim); }
                a.Crates.Add(new() { Team = s.Team, Kind = milestone == 25 ? "Rook" : "Queen", Square = to.ToString() });
            }
        }
        CheckCollapse(m);
    }
    private void CheckCollapse(OnlineMatch m)
    {
        var a = m.Chaos!;
        if (a.CollapsedFiles == 0 && a.Sides.Any(s => Has(s, 18) && s.CompletedTurns >= 10))
        { a.CollapsedFiles = 1; a.CollapseSecondAt = a.CombinedPlies + 5; Collapse(m); }
        if (a.CollapsedFiles == 1 && a.CombinedPlies >= a.CollapseSecondAt) { a.CollapsedFiles = 2; Collapse(m); }
    }
    private void Collapse(OnlineMatch m)
    {
        foreach (var p in m.Board.ToList()) if (!Available(m, GameRules.Square(p.Square))) Remove(m, p);
        m.Chaos!.Mines.RemoveAll(c => !Available(m, GameRules.Square(c.Square)));
        m.Chaos.Crates.RemoveAll(c => !Available(m, GameRules.Square(c.Square)));
    }
    private void BeginTurn(OnlineMatch m)
    {
        var s = Side(m, m.Turn); s.RollsThisTurn = 0; s.BoughtThisTurn = false;
        ObserveChecks(m);
        if (Has(s, 15) && !s.SubstituteUsed && InCheck(m, m.Turn))
        {
            s.SubstituteUsed = true; var king = RealKing(m, m.Turn);
            if (king is not null)
            {
                var from = GameRules.Square(king.Square); var safe = Empty(m, m.Turn).Where(to => SafeRelocate(m, king, to)).ToList();
                if (safe.Count > 0) { king.Square = Pick(safe).ToString(); king.HasMoved = true; Effect(m, Spawn(m, m.Turn, "King", from)).Decoy = true; }
            }
        }
        PruneDecisions(m);
    }
    private static bool CanPlace(OnlineMatch m, string team, string kind, Square to, bool noEnemyCheck = false, int? removed = null)
    {
        if (!Available(m, to)) return false;
        var clone = OnlineJson.Clone(m);
        clone.Board.RemoveAll(p => p.Id == removed || p.Square == to.ToString());
        Spawn(clone, team, kind, to);
        return !InCheck(clone, team) && (!noEnemyCheck || !InCheck(clone, GameRules.Other(team)));
    }
    private static string DeploymentKind(OnlineMatch m, ChaosDeployment d, Square to) => d.Kind == "Pawn" && to.Rank == (d.Team == "White" ? 7 : 0) && !Has(Side(m, GameRules.Other(d.Team)), 33) ? "Queen" : d.Kind;
    private static List<Square> DeploymentSquares(OnlineMatch m, ChaosDeployment d)
    {
        if (d.Action == "QuestPawns") return m.Board.Where(p => p.Team == d.Team && p.Kind == "Pawn").Select(p => GameRules.Square(p.Square)).ToList();
        if (d.Action == "Redeploy")
        {
            var p = m.Board.FirstOrDefault(p => p.Id == d.PieceId); if (p is null) return new();
            return Empty(m).Where(to => CanPlace(m, d.Team, p.Kind, to, removed: p.Id)).Append(GameRules.Square(p.Square)).ToList();
        }
        var squares = d.Center is null ? Empty(m, d.Team, d.Ranks) : Empty(m);
        if (d.Center is not null) { var center = GameRules.Square(d.Center); squares.RemoveAll(to => Math.Abs(to.File - center.File) > 1 || Math.Abs(to.Rank - center.Rank) > 1); }
        return squares.Where(to => CanPlace(m, d.Team, DeploymentKind(m, d, to), to)).ToList();
    }
    private static void PruneDecisions(OnlineMatch m)
    {
        while (m.Chaos!.Deployments.Count > 0 && DeploymentSquares(m, m.Chaos.Deployments[0]).Count == 0) m.Chaos.Deployments.RemoveAt(0);
    }
    private static void ResolveDeployment(OnlineMatch m, string team, AbilityCommand c)
    {
        var a = m.Chaos!; var d = a.Deployments[0];
        Require(d.Team == team && c.Kind == "Deploy", "PendingDecision");
        var to = GameRules.Square(c.Target);
        Require(DeploymentSquares(m, d).Contains(to), "InvalidAbilityTarget");
        if (d.Action == "QuestPawns")
        {
            var pawn = At(m, to)!; pawn.Kind = "Queen"; pawn.HasMoved = true;
            Side(m, team).Effects.RemoveAll(e => e.PieceId == pawn.Id); Effect(m, pawn).PromotedQueen = true;
            if (--d.Remaining > 0) return;
        }
        else if (d.Action == "Redeploy") { var piece = Piece(m, d.PieceId); piece.Square = to.ToString(); piece.HasMoved = true; }
        else
        {
            string kind = DeploymentKind(m, d, to); var p = Spawn(m, team, kind, to);
            if (d.Traits is { } traits)
            {
                var e = Effect(m, p); e.QueenMastery = traits.QueenMastery;
                if (kind == "Pawn") { e.Bloodthirsty = traits.Bloodthirsty; e.Commandant = traits.Commandant; e.TrapPawn |= traits.TrapPawn; }
            }
            if (d.Kind == "Pawn" && kind != "Pawn") Promoted(m, p);
        }
        a.Deployments.RemoveAt(0); PruneDecisions(m);
    }
}
