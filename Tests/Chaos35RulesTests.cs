using ChessButWeird.Domain;
using ChessButWeird.Domain.Chaos35;
using ChessGame.Api.Online;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Xunit;

namespace ChessGame.Api.Tests;

public sealed class Chaos35RulesTests
{
    private readonly ChaosEngine engine = new();
    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
    public static IEnumerable<object[]> Buffs => Enumerable.Range(0, 35).Select(id => new object[] { id });
    private OnlineMatch Start(int white, int black = 28, bool resolveSetup = true)
    {
        var m = new OnlineMatch { ReadyDeadline = Now.AddMinutes(3), Players = new() {
            new() { UserId = "white", Color = "White" }, new() { UserId = "black", Color = "Black" } } };
        GameRules.Write(m, FenCodec.Parse(FenCodec.InitialPosition)); engine.Initialize(m, Now);
        m.Chaos!.Sides[0].DraftOptions = new() { white }; m.Chaos.Sides[1].DraftOptions = new() { black };
        engine.ApplyAbility(m, "White", new() { Kind = "SelectBuff", BuffId = white }, Now);
        engine.ApplyAbility(m, "Black", new() { Kind = "SelectBuff", BuffId = black }, Now);
        if (!resolveSetup) return m;
        foreach (string team in new[] { "White", "Black" })
        {
            var s = Side(m, team);
            if (s.BuffId is 0 or 2 or 3)
            {
                string[] kinds = s.BuffId == 0 ? new[] { "Pawn", "Pawn", "Pawn" } : s.BuffId == 2 ? new[] { "Knight" } : new[] { "Knight", "Bishop" };
                var targets = new List<int>();
                foreach (var kind in kinds) targets.Add(m.Board.First(p => p.Team == team && p.Kind == kind && !targets.Contains(p.Id)).Id);
                engine.ApplyAbility(m, team, new() { Kind = "SelectTargets", PieceIds = targets }, Now);
            }
            if (s.BuffId == 13) engine.ApplyAbility(m, team, new() { Kind = "HideKing", TargetPieceId = m.Board.First(p => p.Team == team && p.Kind == "Rook").Id }, Now);
            if (s.BuffId == 27) engine.ApplyAbility(m, team, new() { Kind = "ReplaceQueen", PieceKind = "Rook" }, Now);
            if (s.BuffId == 23)
            {
                var formation = m.Board.Where(p => p.Team == team).Select(p => new FormationPlacement(p.Id, p.Square)).ToList();
                int pawn = m.Board.First(p => p.Team == team && p.Square == (team == "White" ? "a2" : "a7")).Id;
                formation[formation.FindIndex(p => p.PieceId == pawn)] = new(pawn, team == "White" ? "a3" : "a6");
                engine.ApplyAbility(m, team, new() { Kind = "ConfirmFormation", Formation = formation }, Now);
            }
        }
        Assert.Equal("Playing", m.Chaos!.Phase); m.Status = "InProgress";
        return m;
    }
    private static ChaosSide Side(OnlineMatch m, string team = "White") => m.Chaos!.Sides.Single(s => s.Team == team);
    private static OnlinePiece At(OnlineMatch m, string square) => m.Board.Single(p => p.Square == square);
    private static ChaosPieceEffect Effect(OnlineMatch m, OnlinePiece p)
    {
        var s = Side(m, p.Team); var e = s.Effects.FirstOrDefault(e => e.PieceId == p.Id);
        if (e is null) { e = new() { PieceId = p.Id }; s.Effects.Add(e); } return e;
    }
    private static void Board(OnlineMatch m, params (string kind, string team, string square)[] pieces)
    {
        m.Board.Clear(); m.Chaos!.Deployments.Clear(); m.Chaos.ForcedPawn = null; m.Chaos.NextPieceId = 100;
        int id = 1;
        foreach (var p in pieces) m.Board.Add(new() { Id = id++, Kind = p.kind, Team = p.team, Square = p.square, Forward = p.team == "White" ? 1 : -1, HasMoved = true });
        foreach (var s in m.Chaos.Sides) { s.Effects.Clear(); s.CommandantPawns.Clear(); s.OriginalQueen = null; }
    }
    private void Move(OnlineMatch m, string from, string to, PieceKind? promotion = null) => engine.ApplyMove(m, new(GameRules.Square(from), GameRules.Square(to), promotion));

    [Theory, MemberData(nameof(Buffs))]
    public void EveryBuffStartsRecoversAndProvidesAuthoritativeMoves(int buff)
    {
        var m = Start(buff); string before = OnlineJson.Write(m);
        var snapshot = OnlineService.Snapshot(m, Now, "white");
        Assert.Equal(3, snapshot.Chaos!.RulesVersion); Assert.Equal(35, snapshot.Chaos.BuffDefinitions.Count);
        Assert.NotEmpty(snapshot.Chaos.LegalMoves!); Assert.Equal(before, OnlineJson.Write(m));
        var json = OnlineJson.Clone(m); var bson = BsonSerializer.Deserialize<OnlineMatch>(m.ToBson());
        Assert.Equal(buff, Side(json).BuffId); Assert.Equal(buff, Side(bson).BuffId);
        var command = snapshot.Chaos.LegalMoves![0];
        Move(json, command.From, command.To, command.Promotion is null ? null : Enum.Parse<PieceKind>(command.Promotion));
        Assert.Equal(1, json.Chaos!.CombinedPlies);
        Assert.Equal(0, m.Chaos!.CombinedPlies);
    }
    [Fact]
    public void PoolAndFlagsContainAll35Without32BitAliasing()
    {
        Assert.Equal(Enumerable.Range(0, 35), ChaosEngine.Tiers.SelectMany(p => p).OrderBy(id => id));
        Assert.Equal(35, ChaosBuffCatalog.Definitions.Count); Assert.Equal(26, ChaosBuffCatalog.LegacyDefinitions.Count);
        foreach (var d in ChaosBuffCatalog.Definitions) { Assert.Contains(d.Id, ChaosEngine.Tiers[(int)Enum.Parse<AramTier>(d.Tier)]); Assert.NotEmpty(d.Summary!); }
        Assert.NotEqual(AramBuffs.CommandantPawn, AramBuffs.KingIsPoorPiece);
        Assert.NotEqual(AramBuffs.StrongFortress, AramBuffs.DoubleEdgedTrap);
        Assert.NotEqual(AramBuffs.FreestyleLeap, AramBuffs.Paratrooper);
    }
    private enum AramTier { Silver, Gold, Diamond, Legendary }
    [Fact]
    public void DraftsAreIndependentUniqueAndSameTier()
    {
        var m = Start(28);
        for (int i = 0; i < 100; i++)
        {
            engine.Initialize(m, Now); var options = m.Chaos!.Sides.SelectMany(s => s.DraftOptions).ToList();
            Assert.All(m.Chaos.Sides, s => Assert.Equal(3, s.DraftOptions.Distinct().Count()));
            Assert.Contains(ChaosEngine.Tiers, pool => options.All(pool.Contains));
        }
    }
    [Fact]
    public void InvalidCommandsCannotMutateStateOrUseUnownedBuffs()
    {
        var m = Start(31); string before = OnlineJson.Write(m);
        Assert.Throws<OnlineException>(() => engine.ApplyAbility(m, "White", new() { Kind = "BuyPiece", PieceKind = "King", Target = "a3" }, Now));
        Assert.Equal(before, OnlineJson.Write(m));
        Assert.Throws<OnlineException>(() => engine.ApplyAbility(m, "White", new() { Kind = "BuyPiece", PieceKind = ((int)PieceKind.Queen).ToString(), Target = "a3" }, Now));
        Assert.Equal(before, OnlineJson.Write(m));
        Assert.Throws<OnlineException>(() => engine.ApplyAbility(m, "White", new() { Kind = "RestoreQueen", TargetPieceId = At(m, "a2").Id }, Now));
        Assert.Equal(before, OnlineJson.Write(m));
        Assert.Throws<OnlineException>(() => Move(m, "a1", "a8")); Assert.Equal(before, OnlineJson.Write(m));
    }
    [Fact]
    public void FormationDraftExtendsBothDeadlinesBeforeTheOrdinaryReadyTimeout()
    {
        var m = new OnlineMatch { ReadyDeadline = Now.AddSeconds(30) };
        GameRules.Write(m, FenCodec.Parse(FenCodec.InitialPosition)); engine.Initialize(m, Now);
        m.Chaos!.Sides[0].DraftOptions = new() { 23 }; m.Chaos.Sides[1].DraftOptions = new() { 28 };
        engine.ApplyAbility(m, "White", new() { Kind = "SelectBuff", BuffId = 23 }, Now.AddSeconds(10));
        engine.ApplyAbility(m, "Black", new() { Kind = "SelectBuff", BuffId = 28 }, Now.AddSeconds(20));
        Assert.Equal(Now.AddSeconds(140), Side(m).FormationDeadline);
        Assert.Equal(Now.AddSeconds(200), m.ReadyDeadline);
        Assert.Equal(m.ReadyDeadline, m.Chaos.SetupDeadline);
    }
    [Fact]
    public void CreditAllowsOnePurchaseOnDebtThenRequiresSolvency()
    {
        var m = Start(31); engine.ApplyAbility(m, "White", new() { Kind = "BuyPiece", PieceKind = "Queen", Target = "a3" }, Now);
        Assert.Equal(-10, Side(m).Credit); Assert.True(Side(m).BoughtThisTurn); Assert.Equal("Queen", At(m, "a3").Kind); Assert.Equal(0, m.Chaos!.CombinedPlies);
        Side(m).BoughtThisTurn = false;
        Assert.Throws<OnlineException>(() => engine.ApplyAbility(m, "White", new() { Kind = "BuyPiece", PieceKind = "Pawn", Target = "b3" }, Now));
    }
    [Fact]
    public void UltimateQuestAddsQueenMovementWithoutChangingPieceKind()
    {
        var m = Start(29); Board(m, ("King", "White", "a1"), ("King", "Black", "h8"), ("Knight", "White", "c3"), ("Queen", "Black", "d5"));
        Move(m, "c3", "d5"); var knight = At(m, "d5"); Assert.True(Effect(m, knight).QueenMastery); Assert.Equal("Knight", knight.Kind);
        m.Turn = "White"; Move(m, "d5", "d7"); Assert.Equal("Knight", At(m, "d7").Kind);
    }
    [Fact]
    public void MonsterTruckCannotCaptureThroughAllies()
    {
        var m = Start(30); Board(m, ("King", "White", "a1"), ("King", "Black", "h8"), ("Rook", "White", "b2"), ("Pawn", "White", "b3"), ("Pawn", "Black", "b5"));
        Assert.Throws<OnlineException>(() => Move(m, "b2", "b5")); Move(m, "b2", "b4"); Assert.Equal("Rook", At(m, "b4").Kind);
    }
    [Fact]
    public void TrapSuppressesPromotionAndOrdinaryPawnMovement()
    {
        var m = Start(28, 33); Board(m, ("King", "White", "a1"), ("King", "Black", "h8"), ("Pawn", "White", "c7"));
        Assert.Throws<OnlineException>(() => Move(m, "c7", "c8", PieceKind.Queen)); Move(m, "c7", "c8");
        Assert.Equal("Pawn", At(m, "c8").Kind); Assert.True(Effect(m, At(m, "c8")).TrapPawn);
        m.Turn = "White"; Move(m, "c8", "d8"); Assert.Equal("Pawn", At(m, "d8").Kind);
    }
    [Fact]
    public void ParatrooperDecisionSurvivesReconnectAndBlocksMovesUntilPlacement()
    {
        var m = Start(34); Board(m, ("King", "White", "a1"), ("King", "Black", "h8"), ("Pawn", "White", "c7"));
        Move(m, "c7", "c8", PieceKind.Rook); m = OnlineJson.Clone(m);
        Assert.Equal("Redeploy", m.Chaos!.Deployments.Single().Action);
        Assert.Throws<OnlineException>(() => Move(m, "h8", "g8"));
        Assert.Throws<OnlineException>(() => engine.ApplyAbility(m, "Black", new() { Kind = "Deploy", Target = "c4" }, Now));
        engine.ApplyAbility(m, "White", new() { Kind = "Deploy", Target = "c4" }, Now);
        Assert.Equal("Rook", At(m, "c4").Kind); Assert.Empty(m.Chaos!.Deployments); Assert.Equal("Black", m.Turn);
    }
    [Fact]
    public void KinglessSurvivesWithoutKingAndLosesAtDeadlineButCheckmateTakesPriority()
    {
        var m = Start(32); Assert.DoesNotContain(m.Board, p => p.Team == "White" && p.Kind == "King"); Assert.Null(engine.Outcome(m));
        Side(m).CompletedTurns = 25; Assert.Equal("Black", engine.Outcome(m)!.Value.WinnerTeam);
        Board(m, ("King", "Black", "h8"), ("Queen", "White", "g7"), ("Queen", "White", "f6")); m.Turn = "Black";
        Assert.Equal(("White", "Checkmate"), engine.Outcome(m)!.Value);
    }
    [Fact]
    public void QueenRefundWaitsTenOwnerTurnsAndRejectsPromotedQueen()
    {
        var m = Start(27); int pawn = At(m, "a2").Id;
        Assert.Throws<OnlineException>(() => engine.ApplyAbility(m, "White", new() { Kind = "RestoreQueen", TargetPieceId = pawn }, Now));
        Side(m).CompletedTurns = 10; engine.ApplyAbility(m, "White", new() { Kind = "RestoreQueen", TargetPieceId = pawn }, Now);
        Assert.Equal("Queen", At(m, "a2").Kind); Assert.True(Side(m).QueenRefundUsed);
    }
    [Fact]
    public void QuestRewardSelectsTwoPawnsOrDeploysOneQueenAndDrawBecomesLoss()
    {
        var m = Start(26); Board(m, ("King", "White", "a1"), ("King", "Black", "h8"), ("Pawn", "White", "b2"), ("Pawn", "White", "c2"), ("Knight", "White", "c3"), ("Knight", "Black", "e4"));
        var s = Side(m); s.QuestPromoted = true; s.QuestCaptures = new() { "Pawn", "Bishop", "Rook", "Queen" };
        Move(m, "c3", "e4"); Assert.True(Side(m).QuestRewarded); Assert.Equal("QuestPawns", m.Chaos!.Deployments.Single().Action);
        engine.ApplyAbility(m, "White", new() { Kind = "Deploy", Target = "b2" }, Now); Assert.Single(m.Chaos!.Deployments);
        engine.ApplyAbility(m, "White", new() { Kind = "Deploy", Target = "c2" }, Now); Assert.Empty(m.Chaos!.Deployments); Assert.Equal("Queen", At(m, "b2").Kind);
        Assert.Equal("Black", ChaosEngine.DrawWinner(m));
    }
    [Fact]
    public void QueenRefundReplacesATrappedPawnWithNormalQueenMovement()
    {
        var m = Start(27, 33); Board(m, ("King", "White", "a1"), ("King", "Black", "h8"), ("Pawn", "White", "c7"));
        Move(m, "c7", "c8"); Assert.True(Effect(m, At(m, "c8")).TrapPawn);
        m.Turn = "White"; Side(m).CompletedTurns = 10;
        engine.ApplyAbility(m, "White", new() { Kind = "RestoreQueen", TargetPieceId = At(m, "c8").Id }, Now);
        Assert.False(Effect(m, At(m, "c8")).TrapPawn);
        Move(m, "c8", "c5"); Assert.Equal("Queen", At(m, "c5").Kind);
    }
    [Fact]
    public void AutomaticSniperStaysOnItsSquareAndSpendsOneCombinedMove()
    {
        var m = Start(7); Board(m, ("King", "White", "a1"), ("King", "Black", "h8"), ("Bishop", "White", "b2"), ("Rook", "Black", "f6"), ("Knight", "Black", "d4"));
        m.Board.Remove(At(m, "d4")); Move(m, "b2", "f6"); Assert.Equal(5, Effect(m, At(m, "f6")).SniperUntil);
        m.Turn = "White"; m.Board.Add(new() { Id = 99, Kind = "Rook", Team = "Black", Square = "d4", Forward = -1 });
        Move(m, "f6", "d4"); Assert.Equal("Bishop", At(m, "f6").Kind); Assert.DoesNotContain(m.Board, p => p.Square == "d4"); Assert.Equal(2, m.Chaos!.CombinedPlies);
    }
    [Fact]
    public void GachaUsesNonPawnTicketsAndOneRollWithoutCheckingEnemyKing()
    {
        var m = Start(14); Board(m, ("King", "White", "a1"), ("King", "Black", "h8"), ("Rook", "White", "b2"), ("Pawn", "Black", "b3"), ("Knight", "Black", "b4"));
        Move(m, "b2", "b3"); Assert.Equal(0, Side(m).Tickets); m.Turn = "White"; Move(m, "b3", "b4"); Assert.Equal(1, Side(m).Tickets);
        m.Turn = "White"; engine.ApplyAbility(m, "White", new() { Kind = "BattleGacha" }, Now); Assert.Equal(0, Side(m).Tickets); Assert.Equal(1, Side(m).RollsThisTurn);
    }
    [Fact]
    public void PassengerMustBeAdjacentAndCannotUnloadUntilNextOwnerTurn()
    {
        var m = Start(12); int rook = At(m, "a1").Id, pawn = At(m, "a2").Id;
        Assert.Throws<OnlineException>(() => engine.ApplyAbility(m, "White", new() { Kind = "LoadPawn", PieceId = rook, TargetPieceId = At(m, "h2").Id }, Now));
        engine.ApplyAbility(m, "White", new() { Kind = "LoadPawn", PieceId = rook, TargetPieceId = pawn }, Now);
        Assert.Throws<OnlineException>(() => engine.ApplyAbility(m, "White", new() { Kind = "UnloadPawn", PieceId = rook, Target = "a2" }, Now));
        Side(m).CompletedTurns = 1; engine.ApplyAbility(m, "White", new() { Kind = "UnloadPawn", PieceId = rook, Target = "a2" }, Now);
        Assert.Equal("Pawn", At(m, "a2").Kind); Assert.False(Effect(m, At(m, "a1")).Passenger);
    }
    [Fact]
    public void NewSnapshotsAndEventsHideDoppelgangerButNotFromOwner()
    {
        var m = Start(3); var own = OnlineService.Snapshot(m, Now, "white"); var other = OnlineService.Snapshot(m, Now, "black");
        Assert.Equal(3, Side(m).BuffId); Assert.Equal(3, own.Chaos!.Sides[0].BuffId); Assert.NotEqual(3, other.Chaos!.Sides[0].BuffId);
        Assert.Null(other.Chaos.Sides[0].SwappedKnight); Assert.Empty(other.Chaos.Sides[0].DraftOptions);
        var e = new OnlineEvent { PayloadJson = OnlineJson.Write(new { state = OnlineService.Snapshot(m, Now), command = new AbilityCommand { Kind = "ToggleSwap" } }) };
        var payload = OnlineJson.Write(OnlineJson.EventPayload(e, "black")); Assert.DoesNotContain("\"buffId\":3,", payload); Assert.Contains("\"command\":null", payload);
    }
    [Fact]
    public void StoredV2KeepsOldRulesAnd26Metadata()
    {
        var m = Start(28); m.Chaos!.RulesVersion = 2; Side(m).BuffId = 2; Side(m).FreestyleKnight = null;
        var snapshot = OnlineService.Snapshot(m, Now, "white"); Assert.Equal(26, snapshot.Chaos!.BuffDefinitions.Count); Assert.Null(snapshot.Chaos.LegalMoves);
        Move(m, "b1", "b3"); Assert.Equal("Knight", At(m, "b3").Kind); Assert.Equal(0, m.Chaos.CombinedPlies);
    }
    [Fact]
    public void FreestyleOnlyExtendsTheSelectedKnight()
    {
        var m = Start(2); Side(m).FreestyleKnight = At(m, "b1").Id;
        Move(m, "b1", "b3"); m.Turn = "White";
        Assert.Throws<OnlineException>(() => Move(m, "g1", "g3"));
    }
    [Fact]
    public void FortressIgnoresAttackAndMovementRestrictionsAndCanUseOppositeWing()
    {
        var m = Start(1); Board(m, ("King", "White", "e1"), ("Rook", "White", "a1"), ("Rook", "White", "h1"), ("King", "Black", "e8"), ("Rook", "Black", "e4"), ("Rook", "Black", "f4"));
        At(m, "a1").HasMoved = At(m, "h1").HasMoved = false;
        Move(m, "e1", "g1"); Assert.Equal("King", At(m, "g1").Kind); Assert.Equal(1, Side(m).CastledWings);
        m.Board.Remove(At(m, "f1")); m.Board.Remove(At(m, "e4")); m.Board.Remove(At(m, "f4")); m.Turn = "White";
        Move(m, "g1", "c1"); Assert.Equal(3, Side(m).CastledWings); Assert.Equal("Rook", At(m, "d1").Kind);
    }
    [Fact]
    public void NobleSacrificeCannotEatTwiceOrRetreatFromBloodthirstyAlone()
    {
        var m = Start(6); Board(m, ("King", "White", "a1"), ("King", "Black", "h8"), ("Pawn", "White", "c2"), ("Pawn", "White", "d3"), ("Pawn", "White", "e4"));
        Move(m, "c2", "d3"); m.Turn = "White";
        Assert.Throws<OnlineException>(() => Move(m, "d3", "e4")); Assert.Throws<OnlineException>(() => Move(m, "d3", "d2"));
        Move(m, "d3", "c4"); Assert.True(Effect(m, At(m, "c4")).Bloodthirsty);
    }
    [Fact]
    public void TeleportCostsOneUseAndRechargesByOwnerTurns()
    {
        var m = Start(5); Side(m).OriginalQueen = At(m, "d1").Id;
        Move(m, "d1", "c4"); Assert.Equal(1, Side(m).TeleportUses); Assert.Equal(5, Side(m).TeleportReady);
        m.Turn = "White"; Assert.Throws<OnlineException>(() => Move(m, "c4", "e5"));
        Side(m).CompletedTurns = 5; Move(m, "c4", "e5"); Assert.Equal(2, Side(m).TeleportUses);
    }
    [Fact]
    public void OriginalQueenBombDestroysCapturerButNotKings()
    {
        var m = Start(4); Board(m, ("King", "White", "a1"), ("King", "Black", "e6"), ("Queen", "White", "d5"), ("Rook", "Black", "d8"), ("Pawn", "White", "e5"));
        Side(m).OriginalQueen = At(m, "d5").Id; m.Turn = "Black"; Move(m, "d8", "d5");
        Assert.DoesNotContain(m.Board, p => p.Square is "d5" or "e5"); Assert.Equal("King", At(m, "e6").Kind); Assert.True(Side(m).BomberUsed);
    }
    [Fact]
    public void PlagueExpiresAfterFourFollowingCombinedMovesAndKingsAreImmune()
    {
        var m = Start(22); Board(m, ("King", "White", "a1"), ("King", "Black", "h8"), ("Pawn", "White", "d4"), ("Rook", "Black", "d7"));
        m.Turn = "Black"; Move(m, "d7", "d4"); Assert.Equal(5, Effect(m, At(m, "d4")).InfectedUntil);
        Move(m, "a1", "b1"); Move(m, "h8", "g8"); Move(m, "b1", "a1"); Move(m, "g8", "h8");
        Assert.DoesNotContain(m.Board, p => p.Kind == "Rook");
        Board(m, ("King", "White", "a1"), ("King", "Black", "h8"), ("Pawn", "White", "g7")); m.Turn = "Black";
        Move(m, "h8", "g7"); Assert.Null(Effect(m, At(m, "g7")).InfectedUntil);
    }
    [Fact]
    public void CollapseUsesOwnerTenThenFiveCombinedMoves()
    {
        var m = Start(18); Board(m, ("King", "White", "d1"), ("King", "Black", "e8"), ("Pawn", "Black", "a7"));
        Side(m).CompletedTurns = 9; m.Chaos!.CombinedPlies = 18; Move(m, "d1", "e1");
        Assert.Equal(1, m.Chaos.CollapsedFiles); Assert.Equal(24, m.Chaos.CollapseSecondAt); Assert.DoesNotContain(m.Board, p => p.Square == "a7");
        Move(m, "e8", "d8"); Move(m, "e1", "d1"); Move(m, "d8", "e8"); Move(m, "d1", "e1"); Move(m, "e8", "d8"); Assert.Equal(2, m.Chaos.CollapsedFiles);
    }
    [Fact]
    public void BetrayalBecomesReceiverDeploymentWithinFirstThreeRanks()
    {
        var m = Start(17); var queen = m.Board.First(p => p.Team == "White" && Effect(m, p).Unstable);
        // The initial bonus queen is random and may otherwise block a2-a3.
        queen.Square = "d3"; Effect(m, queen).BetrayalChecks = 18;
        Move(m, "a2", "a3"); var d = m.Chaos!.Deployments.Single(); Assert.Equal("Black", d.Team); Assert.Equal(3, d.Ranks);
        engine.ApplyAbility(m, "Black", new() { Kind = "Deploy", Target = "a6" }, Now); Assert.Equal("Black", At(m, "a6").Team); Assert.False(Effect(m, At(m, "a6")).Unstable);
    }
    [Fact]
    public void PeaceRecruitUnlocksAfter14CombinedMovesAndCanReplacePawnWhenHomeFull()
    {
        var m = Start(10); Side(m).PeaceFailed = false; m.Chaos!.CombinedPlies = 14;
        for (int x = 0; x < 8; x++) m.Board.Add(new() { Id = 100 + x, Team = "White", Kind = "Pawn", Square = ((char)('a' + x)).ToString() + "3", Forward = 1 });
        int enemy = At(m, "b8").Id; engine.ApplyAbility(m, "White", new() { Kind = "Recruit", TargetPieceId = enemy, Target = "a2" }, Now);
        Assert.True(Side(m).PeaceUsed); Assert.Equal("Knight", At(m, "a2").Kind); Assert.DoesNotContain(m.Board, p => p.Id == enemy);
    }
    [Fact]
    public void DoppelgangerUsesCombinedCooldownAndDisablesWhenARequiredTypeIsLost()
    {
        var m = Start(3); Side(m).SwapReady = 4;
        Assert.Throws<OnlineException>(() => engine.ApplyAbility(m, "White", new() { Kind = "KeepSwap" }, Now));
        m.Chaos!.CombinedPlies = 4; engine.ApplyAbility(m, "White", new() { Kind = "KeepSwap" }, Now); Assert.Equal(8, Side(m).SwapReady);
        m.Board.RemoveAll(p => p.Team == "White" && p.Kind == "Bishop"); Move(m, "a2", "a3"); Assert.True(Side(m).DoppelDisabled); Assert.Null(Side(m).SwappedKnight);
    }
    [Fact]
    public void AutoCastlePreservesAllPiecesBySwappingBlockingAllies()
    {
        var m = Start(28); Assert.Equal(32, m.Board.Count); Assert.Equal("King", At(m, "g1").Kind); Assert.Equal("Rook", At(m, "f1").Kind);
        Assert.Equal("Knight", At(m, "e1").Kind); Assert.Equal("Bishop", At(m, "h1").Kind);
    }
    [Fact]
    public void PawnArmyAndRifleHaveTheirOwnInitialStateAndRifleExpirySpendsNoMove()
    {
        var army = Start(24); Assert.Equal(31, army.Board.Count(p => p.Team == "White" && p.Kind == "Pawn"));
        var m = Start(25); Assert.Single(m.Board, p => p.Team == "White");
        engine.ApplyAbility(m, "White", new() { Kind = "AimRifle" }, Now);
        Assert.True(engine.Tick(m, Now.AddSeconds(16))); Assert.Null(m.Chaos!.Rifle); Assert.Equal(3, Side(m).RifleReady); Assert.Equal(0, m.Chaos.CombinedPlies);
    }
    [Fact]
    public void FormationExpiryRefundsOnlyGoldAndCanContinueItsRequiredSetup()
    {
        var m = Start(23, resolveSetup: false); Assert.True(engine.Tick(m, Now.AddSeconds(121)));
        Assert.Contains(Side(m).BuffId!.Value, ChaosEngine.Tiers[1]); Assert.Equal(Side(m).BuffId is not (3 or 13), Side(m).SetupComplete);
    }
    [Fact]
    public void RejectedSetupDoesNotExposeOrSelectAnUnofferedBuff()
    {
        var m = Start(28); engine.Initialize(m, Now); int offered = Side(m).DraftOptions[0]; int wrong = Enumerable.Range(0, 35).First(id => !Side(m).DraftOptions.Contains(id));
        string before = OnlineJson.Write(m); Assert.Throws<OnlineException>(() => engine.ApplyAbility(m, "White", new() { Kind = "SelectBuff", BuffId = wrong }, Now)); Assert.Equal(before, OnlineJson.Write(m));
        engine.ApplyAbility(m, "White", new() { Kind = "SelectBuff", BuffId = offered }, Now);
        Assert.Throws<OnlineException>(() => engine.ApplyAbility(m, "White", new() { Kind = "SelectBuff", BuffId = offered }, Now));
        Assert.Empty(OnlineService.Snapshot(m, Now, "black").Chaos!.Sides[0].DraftOptions);
    }
    [Fact]
    public void OptionalCrossClientFixturesUseRealServerSnapshots()
    {
        string? output = Environment.GetEnvironmentVariable("CHAOS35_FIXTURE_OUTPUT");
        if (string.IsNullOrEmpty(output)) return;
        var fixtures = new List<object>();
        foreach (int buff in Enumerable.Range(0, 35))
        {
            var m = Start(buff); fixtures.Add(new { name = "play-" + buff, white = OnlineService.Snapshot(m, Now, "white"), black = OnlineService.Snapshot(m, Now, "black") });
            var setup = Start(buff, resolveSetup: false); fixtures.Add(new { name = "setup-" + buff, white = OnlineService.Snapshot(setup, Now, "white"), black = OnlineService.Snapshot(setup, Now, "black") });
            var reverseSetup = Start(28, buff, resolveSetup: false);
            fixtures.Add(new { name = "reverse-setup-" + buff, white = OnlineService.Snapshot(reverseSetup, Now, "white"), black = OnlineService.Snapshot(reverseSetup, Now, "black") });
            var reverse = Start(28, buff); reverse.Turn = "Black";
            fixtures.Add(new { name = "reverse-" + buff, white = OnlineService.Snapshot(reverse, Now, "white"), black = OnlineService.Snapshot(reverse, Now, "black") });
        }
        File.WriteAllText(output, OnlineJson.Write(fixtures));
    }
    [Fact]
    public void PassivePositionsRepeatAfterThreeCyclesAndNoMoveRifleExitCannotAddOccurrences()
    {
        var m = Start(28);
        for (int cycle = 0; cycle < 2; cycle++)
        { Move(m, "e1", "f3"); Move(m, "e8", "f6"); Move(m, "f3", "e1"); Move(m, "f6", "e8"); }
        Assert.Equal((null, "ThreefoldRepetition"), engine.Outcome(m)!.Value);
        var rifle = Start(25); int count = rifle.Repetitions.Values.Sum();
        for (int i = 0; i < 4; i++) { engine.ApplyAbility(rifle,"White",new(){Kind="AimRifle"},Now); engine.ApplyAbility(rifle,"White",new(){Kind="ExitRifle"},Now); }
        Assert.Equal(count, rifle.Repetitions.Values.Sum());
    }
    [Fact]
    public void SimultaneousRoyalLossRemainsDrawAndKinglessCanWinOnTime()
    {
        var m = Start(26); m.Board.RemoveAll(p => p.Kind == "King"); Assert.Null(ChaosEngine.DrawWinner(m)); Assert.Null(engine.Outcome(m)!.Value.WinnerTeam);
        var kingless = Start(32); Assert.True(GameRules.CanPossiblyMate(kingless,"White"));
    }
    [Fact]
    public void EveryManWithFewerThanTwoPawnsReceivesAnywhereQueen()
    {
        var m = Start(26); Board(m,("King","White","a1"),("King","Black","h8"),("Knight","White","c3"),("Knight","Black","e4"));
        Side(m).QuestPromoted=true; Side(m).QuestCaptures=new(){"Pawn","Bishop","Rook","Queen"};
        Move(m,"c3","e4"); Assert.Equal("Deploy",m.Chaos!.Deployments.Single().Action); Assert.Equal(0,m.Chaos.Deployments[0].Ranks);
        engine.ApplyAbility(m,"White",new(){Kind="Deploy",Target="g7"},Now); Assert.Equal("Queen",At(m,"g7").Kind);
    }
    [Fact]
    public void CommandantKeepsTwoSquareAdvanceAfterMovingButOnlyForSelectedPawns()
    {
        var m=Start(0); Board(m,("King","White","a1"),("King","Black","h8"),("Pawn","White","c3"),("Pawn","White","e3"));
        Side(m).CommandantPawns.Add(At(m,"c3").Id);
        Assert.Throws<OnlineException>(()=>Move(m,"e3","e5")); Move(m,"c3","c5"); Assert.Equal("Pawn",At(m,"c5").Kind);
    }
    [Fact]
    public void GamblingCannotGrantAFourthBonus()
    {
        var m=Start(8); Board(m,("King","White","a1"),("King","Black","h8"),("Pawn","White","c3"),("Pawn","Black","d4")); Side(m).ExtraUses=3;
        Move(m,"c3","d4"); Assert.Equal("Black",m.Turn); Assert.Null(m.Chaos!.ForcedPawn); Assert.Equal(3,Side(m).ExtraUses);
        Assert.Equal(.25f,AramBalanceRules.GamblingChance);
    }
    [Theory, InlineData(25), InlineData(50)]
    public void CrateScheduleUsesCombinedMovesAndProtectsKingsOnlyAt25(int milestone)
    {
        var m=Start(9); Board(m,("King","White","a1"),("King","Black","h8")); m.Chaos!.CombinedPlies=milestone-1;
        for(int x=0;x<8;x++) for(int y=0;y<8;y++) if(x!=1||y!=0) m.Chaos.Crates.Add(new(){Team="Black",Square=new Square(x,y).ToString(),Kind="Rook"});
        Move(m,"a1","b1"); Assert.Contains(milestone,m.Chaos.LootRounds);
        Assert.Equal(milestone==25,m.Board.Any(p=>p.Team=="White"&&p.Kind=="King"));
    }
    [Fact]
    public void RetreatLeavesConsumableMineAndDestroysEnemyLandingOnIt()
    {
        var m=Start(11); Board(m,("King","White","a1"),("King","Black","h8"),("Pawn","White","c2"),("Rook","Black","c8"));
        Move(m,"c2","c1"); Assert.Single(m.Chaos!.Mines); Assert.DoesNotContain(m.Board,p=>p.Square=="c1");
        Move(m,"c8","c1"); Assert.Empty(m.Chaos.Mines); Assert.DoesNotContain(m.Board,p=>p.Kind=="Rook");
    }
    [Fact]
    public void HidingKingOpeningIsMandatoryAndRechargeUsesCombinedMoves()
    {
        var m=Start(13); Assert.Equal("King",At(m,"a1").Kind); Assert.Equal(10,Side(m).HidingReady);
        int knight=At(m,"b1").Id; Assert.Throws<OnlineException>(()=>engine.ApplyAbility(m,"White",new(){Kind="HideKing",TargetPieceId=knight},Now));
        m.Chaos!.CombinedPlies=10; engine.ApplyAbility(m,"White",new(){Kind="HideKing",TargetPieceId=knight},Now);
        Assert.Equal("King",At(m,"b1").Kind); Assert.Equal(20,Side(m).HidingReady); Assert.Equal(0,Side(m).CompletedTurns);
    }
    [Fact]
    public void SubstituteRespondsToFirstCheckAndCreatesANonRoyalDecoy()
    {
        var m=Start(15); Board(m,("King","White","e1"),("King","Black","h8"),("Rook","Black","e8")); m.Turn="Black";
        Move(m,"h8","g8"); Assert.True(Side(m).SubstituteUsed); Assert.True(Effect(m,At(m,"e1")).Decoy);
        Assert.Equal(2,m.Board.Count(p=>p.Team=="White"&&p.Kind=="King")); Assert.Null(engine.Outcome(m));
    }
    [Fact]
    public void CannonCaptureRefreshesForTenFollowingCombinedMoves()
    {
        var m=Start(16); Board(m,("King","White","a1"),("King","Black","h8"),("Rook","White","b2"),("Pawn","Black","b3"),("Knight","Black","b5"));
        Effect(m,At(m,"b2")).CannonUntil=10; Move(m,"b2","b5"); Assert.Equal(11,Effect(m,At(m,"b5")).CannonUntil);
    }
    [Fact]
    public void RandomStartingArmyPreservesSquaresKingAndQueen()
    {
        var m=Start(19); Assert.Equal(16,m.Board.Count(p=>p.Team=="White")); Assert.Equal("King",At(m,"e1").Kind); Assert.Equal("Queen",At(m,"d1").Kind);
        Assert.All(m.Board.Where(p=>p.Team=="White"&&p.Kind is not ("King" or "Queen")),p=>Assert.True(p.HasMoved));
    }
    [Fact]
    public void IFrameOffersSafeNearbyEscapeWithoutSpendingAMove()
    {
        var m=Start(20); Board(m,("King","White","h1"),("King","Black","f8"),("Rook","Black","g8"),("Rook","Black","h8"));
        Assert.Null(engine.Outcome(m)); Assert.Equal("White",m.Chaos!.EscapeTeam);
        engine.ApplyAbility(m,"White",new(){Kind="Escape",Target="f1"},Now); Assert.Equal("King",At(m,"f1").Kind); Assert.Equal(1,Side(m).EscapeUses); Assert.Equal(0,m.Chaos.CombinedPlies);
    }
    [Fact]
    public void GhostArmyCanCaptureThroughAlliesWhileEnemiesStillBlock()
    {
        var m=Start(21); Board(m,("King","White","a1"),("King","Black","h8"),("Rook","White","b2"),("Pawn","White","b3"),("Pawn","Black","b5"),("Pawn","Black","b6"));
        Assert.Throws<OnlineException>(()=>Move(m,"b2","b6")); Move(m,"b2","b5"); Assert.Equal("Rook",At(m,"b5").Kind); Assert.Equal("Pawn",At(m,"b3").Kind);
    }
    [Fact]
    public void ClassicTickIsNoOpAndClassicMovesKeepTheirRules()
    {
        var m=new OnlineMatch{Status="InProgress"}; GameRules.Write(m,FenCodec.Parse(FenCodec.InitialPosition));
        Assert.False(engine.Tick(m,Now)); Assert.True(engine.SetupDone(m));
        new GameRules(engine).ApplyMove(m,"White",new("e2","e4",null)); Assert.Null(m.Chaos); Assert.Equal("Black",m.Turn);
    }
}
