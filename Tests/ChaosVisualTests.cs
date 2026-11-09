using ChessButWeird.Domain;
using ChessGame.Api.Online;
using Xunit;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace ChessGame.Api.Tests;

public sealed partial class ChaosVisualTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
    private readonly ChaosEngine engine = new();
    private static ChaosSide Side(OnlineMatch m, string team = "White") => m.Chaos!.Sides.Single(s => s.Team == team);
    private static OnlinePiece At(OnlineMatch m, string square) => m.Board.Single(p => p.Square == square);
    private static ChaosPieceEffect Effect(OnlineMatch m, string square)
    {
        var p = At(m, square); var side = Side(m, p.Team); var e = new ChaosPieceEffect { PieceId = p.Id }; side.Effects.Add(e); return e;
    }
    private OnlineMatch Match(int white, int black, params (string kind, string team, string square)[] pieces)
    {
        var m = new OnlineMatch { Id = "vfx-fixture", ReadyDeadline = Now.AddMinutes(3), StateVersion = 1,
            Players = new() { new() { UserId = "white", Color = "White" }, new() { UserId = "black", Color = "Black" } } };
        GameRules.Write(m, FenCodec.Parse(FenCodec.InitialPosition)); engine.Initialize(m, Now);
        m.Board = pieces.Select((p, i) => new OnlinePiece { Id = i + 1, Kind = p.kind, Team = p.team, Square = p.square, Forward = p.team == "White" ? 1 : -1 }).ToList();
        Side(m).BuffId = white; Side(m, "Black").BuffId = black;
        Side(m).OriginalQueen = m.Board.FirstOrDefault(p => p.Team == "White" && p.Kind == "Queen")?.Id;
        Side(m, "Black").OriginalQueen = m.Board.FirstOrDefault(p => p.Team == "Black" && p.Kind == "Queen")?.Id;
        m.Chaos!.Phase = "Playing"; m.Status = "InProgress"; return m;
    }
    private void Move(OnlineMatch m, string from, string to) { engine.ApplyMove(m, new Move(GameRules.Square(from), GameRules.Square(to))); m.StateVersion++; }
    private object Fixture(string name, OnlineMatch before, OnlineMatch after) => new {
        name, white = new { before = OnlineService.Snapshot(before, Now, "white"), after = OnlineService.Snapshot(after, Now, "white") },
        black = new { before = OnlineService.Snapshot(before, Now, "black"), after = OnlineService.Snapshot(after, Now, "black") } };

    [Fact] public void SixCombatBuffsEmitConfirmedCuesAndExportViewerSnapshots()
    {
        var fixtures = new List<object>();
        var m = Match(6, 26, ("King","White","h1"),("King","Black","h8"),("Pawn","White","c2"),("Pawn","White","d3"));
        var before = OnlineJson.Clone(m); Move(m,"c2","d3");
        Assert.Equal(6, m.Chaos!.VisualCues.Single().BuffId); Assert.True(Side(m).Effects.Single(e=>e.PieceId==At(m,"d3").Id).Bloodthirsty); fixtures.Add(Fixture("noble", before, m));

        m = Match(7,26,("King","White","h1"),("King","Black","h8"),("Bishop","White","c1"),("Rook","Black","g5"));
        Effect(m,"c1").SniperUntil=4; before=OnlineJson.Clone(m); Move(m,"c1","g5");
        Assert.Equal("Bishop",At(m,"c1").Kind);Assert.DoesNotContain(m.Board,p=>p.Square=="g5");Assert.Equal(7,m.Chaos!.VisualCues.Single().BuffId);fixtures.Add(Fixture("sniper",before,m));

        m=Match(16,26,("King","White","h1"),("King","Black","h8"),("Rook","White","b1"),("Pawn","White","b3"),("Bishop","Black","b5"));
        Effect(m,"b1").CannonUntil=10;before=OnlineJson.Clone(m);Move(m,"b1","b5");
        Assert.Equal(16,m.Chaos!.VisualCues.Single().BuffId);Assert.Equal("Rook",At(m,"b5").Kind);fixtures.Add(Fixture("cannon",before,m));

        m=Match(26,4,("King","White","h1"),("King","Black","h8"),("Rook","White","b1"),("Queen","Black","a1"));
        before=OnlineJson.Clone(m);Move(m,"b1","a1");var bomb=m.Chaos!.VisualCues.Single();
        Assert.Equal(4,bomb.BuffId);Assert.Equal(new[]{"a1","a2","b1","b2"},bomb.Tiles);Assert.All(m.Board,p=>Assert.Equal("King",p.Kind));fixtures.Add(Fixture("bomber-edge",before,m));

        m=Match(17,26,("King","White","h1"),("King","Black","h8"),("Queen","White","d3"));
        var unstable=Effect(m,"d3");unstable.Unstable=true;unstable.BetrayalChecks=20;
        before=OnlineJson.Clone(m);Move(m,"h1","g1");Assert.Equal("Betrayal",m.Chaos!.VisualCues.Single().Outcome);Assert.DoesNotContain(m.Board,p=>p.Kind=="Queen");fixtures.Add(Fixture("betrayal",before,m));
        before=OnlineJson.Clone(m);engine.ApplyAbility(m,"Black",new(){Kind="Deploy",Target="a6"},Now);m.StateVersion++;
        Assert.Equal("Black",At(m,"a6").Team);Assert.Equal("Transferred",m.Chaos.VisualCues.Last().Outcome);fixtures.Add(Fixture("betrayal-transfer",before,m));

        m=Match(25,26,("King","White","c2"),("King","Black","h8"),("Rook","Black","c5"));
        engine.ApplyAbility(m,"White",new(){Kind="AimRifle"},Now);before=OnlineJson.Clone(m);
        engine.ApplyAbility(m,"White",new(){Kind="FireRifle",Yaw=0,Pitch=8},Now);m.StateVersion++;
        var shot=m.Chaos!.VisualCues.Single();Assert.Equal(25,shot.BuffId);Assert.Equal("Hit",shot.Outcome);Assert.NotNull(shot.Impact);Assert.DoesNotContain(m.Board,p=>p.Kind=="Rook");fixtures.Add(Fixture("king-rifle",before,m));

        string? output=Environment.GetEnvironmentVariable("BUFF_VFX_FIXTURE_OUTPUT");
        if(output is not null)File.WriteAllText(output,OnlineJson.Write(fixtures));
    }
    [Fact] public void LegalQueriesAndRejectedCommandsCannotPublishVisuals()
    {
        var m=Match(26,4,("King","White","h1"),("King","Black","h8"),("Rook","White","b1"),("Queen","Black","a1"));
        string before=OnlineJson.Write(m);OnlineService.Snapshot(m,Now,"white");Assert.Equal(before,OnlineJson.Write(m));
        Assert.Throws<OnlineException>(()=>Move(m,"b1","c2"));Assert.Empty(m.Chaos!.VisualCues);Assert.Equal(0,m.Chaos.VisualSequence);
    }
    [Fact] public void ProjectionUsesRealEffectsAndHidesEnemyRifleReadiness()
    {
        var m=Match(3,25,("King","White","h1"),("King","Black","h8"),("Queen","White","d3"));
        Side(m).DisguisedBuffId=4;Side(m,"Black").RifleReady=3;
        var black=OnlineService.Snapshot(m,Now,"black");Assert.Equal(4,black.Chaos!.Sides[0].BuffId);Assert.DoesNotContain(black.Chaos.VisualStatuses,s=>s.BuffId==4);
        var white=OnlineService.Snapshot(m,Now,"white");Assert.DoesNotContain(white.Chaos!.VisualStatuses,s=>s.BuffId==25);Assert.Contains(black.Chaos.VisualStatuses,s=>s.BuffId==25&&s.Remaining==3);
        var e=new OnlineEvent {PayloadJson=OnlineJson.Write(new{state=OnlineService.Snapshot(m,Now)})};
        Assert.DoesNotContain("\"buffId\":25,\"actorId\"",OnlineJson.Write(OnlineJson.EventPayload(e,"white")));
    }
    [Fact] public void RifleMissAndProtectedHitStillPublishTheActualRay()
    {
        var m=Match(25,26,("King","White","c2"),("King","Black","h8"),("Queen","Black","c5"));
        engine.ApplyAbility(m,"White",new(){Kind="AimRifle"},Now);engine.ApplyAbility(m,"White",new(){Kind="FireRifle",Yaw=0,Pitch=0},Now);
        Assert.Equal("Blocked",m.Chaos!.VisualCues.Single().Outcome);Assert.Equal("Queen",At(m,"c5").Kind);
        Side(m).CompletedTurns=3;engine.ApplyAbility(m,"White",new(){Kind="AimRifle"},Now);engine.ApplyAbility(m,"White",new(){Kind="FireRifle",Yaw=90,Pitch=0},Now);
        Assert.Equal("Miss",m.Chaos.VisualCues.Last().Outcome);Assert.Equal(2,m.Chaos.VisualSequence);
    }
    [Fact] public void DurableTailStaysBoundedAndSnapshotStatusesAreNotStored()
    {
        var m=Match(25,26,("King","White","c2"),("King","Black","h8"));
        for(int i=0;i<30;i++)
        {
            Side(m).CompletedTurns=i*3;
            engine.ApplyAbility(m,"White",new(){Kind="AimRifle"},Now);
            engine.ApplyAbility(m,"White",new(){Kind="FireRifle",Yaw=90,Pitch=0},Now);
        }
        Assert.Equal(30,m.Chaos!.VisualSequence);Assert.Equal(24,m.Chaos.VisualCues.Count);
        Assert.Equal(Enumerable.Range(7,24).Select(i=>(long)i),m.Chaos.VisualCues.Select(c=>c.Sequence));
        m.Chaos.VisualStatuses.Add(new(25,1,"White",3));
        var bson=m.ToBsonDocument();Assert.False(bson["chaos"].AsBsonDocument.Contains("VisualStatuses"));
        var restored=BsonSerializer.Deserialize<OnlineMatch>(bson);
        Assert.Equal(30,restored.Chaos!.VisualSequence);Assert.Equal(24,restored.Chaos.VisualCues.Count);Assert.Empty(restored.Chaos.VisualStatuses);
        restored.Chaos.VisualCues.Clear();var before=OnlineJson.Write(restored);
        OnlineService.Snapshot(restored,Now,"white");Assert.Equal(before,OnlineJson.Write(restored));
    }
    [Fact] public void OverlappingSnipeAndExplosionRemainDistinctAndExpireFromRealState()
    {
        var m=Match(7,4,("King","White","h1"),("King","Black","h8"),("Bishop","White","c1"),("Queen","Black","b2"));
        Effect(m,"c1").SniperUntil=4;Move(m,"c1","b2");
        Assert.Equal(new[]{4,7},m.Chaos!.VisualCues.Select(c=>c.BuffId));Assert.Equal(new long[]{1,2},m.Chaos.VisualCues.Select(c=>c.Sequence));
        Assert.Empty(OnlineService.Snapshot(m,Now,"white").Chaos!.VisualStatuses);
        m=Match(7,26,("King","White","h1"),("King","Black","h8"),("Bishop","White","c1"));Effect(m,"c1").SniperUntil=4;
        Assert.Contains(OnlineService.Snapshot(m,Now,"white").Chaos!.VisualStatuses,s=>s.BuffId==7&&s.Remaining==4);
        m.Chaos!.CombinedPlies=4;Assert.DoesNotContain(OnlineService.Snapshot(m,Now,"white").Chaos!.VisualStatuses,s=>s.BuffId==7);
    }
    [Fact] public void DownwardRifleShotShowsBoardOcclusionWithoutInventingACapture()
    {
        var m=Match(25,26,("King","White","c2"),("King","Black","h8"));
        engine.ApplyAbility(m,"White",new(){Kind="AimRifle"},Now);engine.ApplyAbility(m,"White",new(){Kind="FireRifle",Yaw=0,Pitch=80},Now);
        var cue=m.Chaos!.VisualCues.Single();Assert.Equal("Blocked",cue.Outcome);Assert.InRange(cue.Impact![1],-.0001,.0001);Assert.Equal(2,m.Board.Count);
    }
}
