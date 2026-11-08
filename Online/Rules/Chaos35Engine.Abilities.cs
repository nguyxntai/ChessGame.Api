using ChessButWeird.Domain;
using ChessButWeird.Domain.Chaos35;

namespace ChessGame.Api.Online;

internal sealed partial class Chaos35Engine
{
    public void ApplyAbility(OnlineMatch original, string team, AbilityCommand c, DateTime now)
    {
        var m = OnlineJson.Clone(original); var a = m.Chaos!;
        if (a.Phase != "Playing") Setup(m, team, c, now);
        else
        {
            PruneDecisions(m);
            if (a.Deployments.Count > 0) ResolveDeployment(m, team, c);
            else PlayAbility(m, team, c, now);
            ObserveChecks(m); PruneDecisions(m); RecordPosition(m);
        }
        Commit(original, m);
    }
    private void PlayAbility(OnlineMatch m, string team, AbilityCommand c, DateTime now)
    {
        var a = m.Chaos!; var s = Side(m, team);
        Require(team == m.Turn, "NotYourTurn"); Require(a.ForcedPawn is null, "ForcedPawnMove");
        Require(a.EscapeTeam is null || c.Kind == "Escape", "PendingDecision");
        Require(a.Rifle is null || c.Kind is "FireRifle" or "ExitRifle", "RifleActive");
        switch (c.Kind)
        {
            case "ToggleSwap":
            case "KeepSwap":
            case "ReselectSwap":
                Require(Has(s, 3) && !s.DoppelDisabled && a.CombinedPlies >= s.SwapReady);
                if (c.Kind == "ReselectSwap") { SelectTargets(m, s, c.PieceIds); s.SwapActive = true; }
                else if (c.Kind == "ToggleSwap")
                { Piece(m, s.SwappedKnight); Piece(m, s.SwappedBishop); s.SwapActive = !s.SwapActive; }
                Require(!InCheck(m, team), "KingInCheck"); s.SwapReady = a.CombinedPlies + 4; break;
            case "HideKing":
                Require(Has(s, 13) && a.CombinedPlies >= s.HidingReady); HideKing(m, s, c.TargetPieceId, false); break;
            case "LoadPawn":
            {
                Require(Has(s, 12)); var rook = Piece(m, c.PieceId); var pawn = Piece(m, c.TargetPieceId);
                var e = Effect(m, rook); var traits = ReadEffect(m, pawn);
                Require(rook.Team == team && rook.Kind == "Rook" && !e.Passenger && pawn.Team == team && pawn.Kind == "Pawn" && Nearby(rook, GameRules.Square(pawn.Square), 1), "InvalidAbilityTarget");
                e.PassengerTraits = new() { Bloodthirsty = traits.Bloodthirsty, QueenMastery = traits.QueenMastery, TrapPawn = traits.TrapPawn, Commandant = traits.Commandant || s.CommandantPawns.Contains(pawn.Id) };
                Remove(m, pawn, false); Require(!InCheck(m, team), "KingInCheck"); e.Passenger = true; e.PassengerReady = s.CompletedTurns + 1; break;
            }
            case "UnloadPawn":
            {
                Require(Has(s, 12)); var rook = Piece(m, c.PieceId); var e = Effect(m, rook); var to = GameRules.Square(c.Target);
                Require(rook.Team == team && rook.Kind == "Rook" && e.Passenger && s.CompletedTurns >= e.PassengerReady && Nearby(rook, to, 1) && At(m, to) is null, "InvalidAbilityTarget");
                var d = new ChaosDeployment { Team = team, Kind = "Pawn", Center = rook.Square, Traits = e.PassengerTraits };
                Require(DeploymentSquares(m, d).Contains(to), "InvalidAbilityTarget");
                e.Passenger = false; e.PassengerTraits = null;
                a.Deployments.Insert(0, d); ResolveDeployment(m, team, new() { Kind = "Deploy", Target = c.Target }); break;
            }
            case "Recruit":
            {
                Require(Has(s, 10) && a.CombinedPlies >= 14 && !s.PeaceFailed && !s.PeaceUsed);
                var victim = Piece(m, c.TargetPieceId); var to = GameRules.Square(c.Target); var occupant = At(m, to);
                bool full = Empty(m, team, 3).Count == 0;
                Require(victim.Team != team && victim.Kind is not ("King" or "Queen") && Available(m, to) && Home(to, team, 3) &&
                    (occupant is null || full && occupant.Team == team && occupant.Kind == "Pawn"), "InvalidAbilityTarget");
                string kind = victim.Kind; Remove(m, victim, false);
                if (occupant is not null && occupant.Id != victim.Id) Remove(m, occupant);
                Spawn(m, team, kind, to); Require(!InCheck(m, team), "KingInCheck"); s.PeaceUsed = true; break;
            }
            case "BattleGacha":
            {
                Require(Has(s, 14) && s.Tickets > 0 && s.RollsThisTurn < 1);
                int roll = Random(100); string kind = roll < 70 ? "Pawn" : roll < 80 ? "Knight" : roll < 90 ? "Bishop" : roll < 99 ? "Rook" : "Queen";
                var squares = Empty(m).Where(to => CanPlace(m, team, kind, to, true)).ToList(); Require(squares.Count > 0, "InvalidAbilityTarget");
                var p = Spawn(m, team, kind, Pick(squares)); s.Tickets--; s.RollsThisTurn++; Land(m, p, GameRules.Square(p.Square)); break;
            }
            case "RestoreQueen":
            {
                Require(Has(s, 27) && s.CompletedTurns >= 10 && !s.QueenRefundUsed);
                var p = Piece(m, c.TargetPieceId);
                Require(p.Team == team && p.Kind != "King" && !ReadEffect(m, p).PromotedQueen, "InvalidAbilityTarget");
                p.Kind = "Queen"; p.HasMoved = true; s.Effects.RemoveAll(e => e.PieceId == p.Id);
                Require(!InCheck(m, team), "KingInCheck"); s.QueenRefundUsed = true; break;
            }
            case "BuyPiece":
            {
                Require(Has(s, 31) && c.PieceKind is "Pawn" or "Knight" or "Bishop" or "Rook" or "Queen");
                var kind = Enum.Parse<PieceKind>(c.PieceKind!);
                Require(AramBalanceRules.CanPurchase(s.Credit, s.BoughtThisTurn, kind));
                var to = GameRules.Square(c.Target);
                Require(Home(to, team) && At(m, to) is null && CanPlace(m, team, c.PieceKind!, to), "InvalidAbilityTarget");
                Spawn(m, team, kind.ToString(), to); s.Credit -= AramBalanceRules.PurchasePrice(kind); s.BoughtThisTurn = true; break;
            }
            case "Escape":
            {
                Require(Has(s, 20) && a.EscapeTeam == team && s.EscapeUses < 3);
                var king = RealKing(m, team) ?? throw new OnlineException("InvalidAbilityTarget"); var to = GameRules.Square(c.Target);
                Require(Available(m, to) && At(m, to) is null && Nearby(king, to, 2) && SafeRelocate(m, king, to), "InvalidAbilityTarget");
                king.Square = to.ToString(); king.HasMoved = true; s.EscapeUses++; a.EscapeTeam = null; break;
            }
            case "AimRifle":
                Require(Has(s, 25) && s.CompletedTurns >= s.RifleReady && a.Rifle is null);
                var from = GameRules.Square((RealKing(m, team) ?? throw new OnlineException("InvalidAbilityTarget")).Square);
                a.Rifle = new() { Team = team, Deadline = now.AddSeconds(15), OriginX = from.File, OriginY = .95, OriginZ = from.Rank }; break;
            case "ExitRifle": Require(a.Rifle?.Team == team); a.Rifle = null; break;
            case "FireRifle": FireRifle(m, s, c, now); break;
            default: throw new OnlineException("InvalidAbility");
        }
    }
    private static void HideKing(OnlineMatch m, ChaosSide s, int? target, bool initial)
    {
        var king = RealKing(m, s.Team) ?? throw new OnlineException("InvalidAbilityTarget"); var ally = Piece(m, target);
        Require(ally.Team == s.Team && ally.Id != king.Id && GameRules.Square(ally.Square).Rank == Back(s.Team) &&
            (initial || GameRules.Square(king.Square).Rank == Back(s.Team)), "InvalidAbilityTarget");
        SwapRelocate(m, king, GameRules.Square(ally.Square));
        Require(initial || !InCheck(m, s.Team), "KingInCheck"); s.HidingReady = m.Chaos!.CombinedPlies + 10;
    }
    public bool Tick(OnlineMatch original, DateTime now)
    {
        var m = OnlineJson.Clone(original); bool changed = false; var a = m.Chaos!;
        if (a.Phase == "Setup")
        {
            foreach (var s in a.Sides.Where(s => Has(s, 23) && !s.FormationComplete && s.FormationDeadline <= now).ToList())
            { ConfirmFormation(m, s, s.OriginalFormation); changed = true; }
            if (changed) CompleteSetup(m);
        }
        if (a.Rifle is { } rifle && now >= rifle.Deadline)
        { Side(m, rifle.Team).RifleReady = Side(m, rifle.Team).CompletedTurns + 3; a.Rifle = null; changed = true; }
        if (changed) Commit(original, m);
        return changed;
    }
}
