using ChessButWeird.Domain;
using ChessGame.Api.Online;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Xunit;

namespace ChessGame.Api.Tests;

public sealed partial class ChaosVisualTests
{
    [Fact] public void TenMobilityAndEnvironmentBuffsExportActualResolvedTransitions()
    {
        var fixtures=new List<object>();
        var m=Match(5,26,("King","White","h1"),("King","Black","h8"),("Queen","White","c2"));
        var before=OnlineJson.Clone(m);Move(m,"c2","e3");
        Assert.Equal("Teleport",m.Chaos!.VisualCues.Single().Outcome);Assert.Equal(1,Side(m).TeleportUses);fixtures.Add(Fixture("teleport",before,m));
        Assert.Throws<OnlineException>(()=>{m.Turn="White";Move(m,"e3","b4");});Assert.Single(m.Chaos.VisualCues);

        m=Match(13,26,("King","White","e1"),("King","Black","h8"),("Rook","White","a1"));before=OnlineJson.Clone(m);
        engine.ApplyAbility(m,"White",new(){Kind="HideKing",TargetPieceId=At(m,"a1").Id},Now);m.StateVersion++;
        Assert.Equal("King",At(m,"a1").Kind);Assert.Equal("Rook",At(m,"e1").Kind);Assert.Equal("Swap",m.Chaos!.VisualCues.Single().Outcome);fixtures.Add(Fixture("swap",before,m));

        m=Match(15,26,("King","White","e1"),("King","Black","h8"),("Rook","Black","e8"));m.Turn="Black";before=OnlineJson.Clone(m);Move(m,"h8","g8");
        var decoy=At(m,"e1");Assert.NotEqual(At(m,m.Chaos!.VisualCues.Single().To).Id,decoy.Id);
        Assert.Contains(OnlineService.Snapshot(m,Now,"black").Chaos!.VisualStatuses,s=>s.BuffId==15&&s.ActorId==decoy.Id);fixtures.Add(Fixture("substitute",before,m));
        before=OnlineJson.Clone(m);m.Turn="Black";Move(m,"e8","e1");
        Assert.Equal("DecoyEnd",m.Chaos.VisualCues.Last().Outcome);Assert.DoesNotContain(OnlineService.Snapshot(m,Now,"white").Chaos!.VisualStatuses,s=>s.ActorId==decoy.Id);fixtures.Add(Fixture("decoy-end",before,m));

        m=Match(20,26,("King","White","h1"),("King","Black","f8"),("Rook","Black","g8"),("Rook","Black","h8"));
        Assert.Null(engine.Outcome(m));Assert.Equal("White",m.Chaos!.EscapeTeam);before=OnlineJson.Clone(m);
        engine.ApplyAbility(m,"White",new(){Kind="Escape",Target="f1"},Now);m.StateVersion++;
        Assert.Equal(0,m.Chaos.CombinedPlies);Assert.Equal("Escape",m.Chaos.VisualCues.Single().Outcome);fixtures.Add(Fixture("escape",before,m));

        foreach(int id in new[]{21,30})
        {
            m=Match(id,26,("King","White","a1"),("King","Black","h8"),("Rook","White","b2"),("Pawn","White","b3"),("Pawn","White","b4"),("Pawn","White","b5"),("Pawn","Black","b6"));
            if(id==30)m.Board.Remove(At(m,"b6"));
            before=OnlineJson.Clone(m);Move(m,"b2","b6");Assert.Equal(new[]{"b3","b4","b5"},m.Chaos!.VisualCues.Single().Tiles);
            Assert.Equal(3,m.Board.Count(p=>p.Team=="White"&&p.Kind=="Pawn"));fixtures.Add(Fixture(id==21?"ghost":"truck",before,m));
        }
        m=Match(26,22,("King","White","h1"),("King","Black","h8"),("Rook","White","b2"),("Pawn","Black","b3"));before=OnlineJson.Clone(m);Move(m,"b2","b3");
        Assert.Contains(OnlineService.Snapshot(m,Now,"white").Chaos!.VisualStatuses,s=>s.BuffId==22&&s.Remaining==4);fixtures.Add(Fixture("plague",before,m));
        m.Chaos!.CombinedPlies=4;m.Turn="White";before=OnlineJson.Clone(m);Move(m,"h1","g1");
        Assert.DoesNotContain(m.Board,p=>p.Square=="b3");Assert.Equal("Expire",m.Chaos.VisualCues.Last().Outcome);fixtures.Add(Fixture("plague-expire",before,m));

        m=Match(9,26,("King","White","a1"),("King","Black","h8"));m.Chaos!.CombinedPlies=24;before=OnlineJson.Clone(m);Move(m,"a1","b1");
        Assert.Single(m.Chaos.Crates);Assert.Equal("Drop",m.Chaos.VisualCues.Single().Outcome);fixtures.Add(Fixture("crate-drop",before,m));
        m=Match(9,26,("King","White","h1"),("King","Black","h8"),("Rook","White","b2"));m.Chaos!.Crates.Add(new(){Team="White",Square="b3",Kind="Rook"});before=OnlineJson.Clone(m);Move(m,"b2","b3");
        Assert.Empty(m.Chaos.Crates);Assert.Single(m.Chaos.Deployments);Assert.Equal("Claim",m.Chaos.VisualCues.Single().Outcome);fixtures.Add(Fixture("crate-claim",before,m));

        m=Match(11,26,("King","White","a1"),("King","Black","h8"),("Pawn","White","c2"),("Rook","Black","c8"));before=OnlineJson.Clone(m);Move(m,"c2","c1");
        Assert.Equal("Arm",m.Chaos!.VisualCues.Single().Outcome);fixtures.Add(Fixture("mine-arm",before,m));before=OnlineJson.Clone(m);Move(m,"c8","c1");
        Assert.Empty(m.Chaos.Mines);Assert.DoesNotContain(m.Board,p=>p.Kind=="Rook");Assert.Equal("White",m.Chaos.VisualCues.Last().Team);fixtures.Add(Fixture("mine-trigger",before,m));

        m=Match(18,26,("King","White","c1"),("King","Black","f8"));Side(m).CompletedTurns=9;before=OnlineJson.Clone(m);Move(m,"c1","d1");
        Assert.Equal(16,m.Chaos!.VisualCues.Single().Tiles.Count);Assert.Equal(16,OnlineService.Snapshot(m,Now,"white").Chaos!.VisualBoard.Count(s=>s.Kind=="Barrier"));fixtures.Add(Fixture("collapse",before,m));
        string? output=Environment.GetEnvironmentVariable("BUFF_VFX_LANDSCAPE_FIXTURE_OUTPUT");if(output is not null)File.WriteAllText(output,OnlineJson.Write(fixtures));
    }
    [Theory,InlineData(5),InlineData(13),InlineData(20)]
    public void ReadinessIsOwnerOnlyAndCannotMutateDuringProjection(int buff)
    {
        var m=Match(buff,26,("King","White","h1"),("King","Black","h8"),("Queen","White","c2"));Side(m).TeleportReady=4;Side(m).HidingReady=4;
        string original=OnlineJson.Write(m);var own=OnlineService.Snapshot(m,Now,"white");var other=OnlineService.Snapshot(m,Now,"black");
        Assert.Contains(own.Chaos!.VisualStatuses,s=>s.BuffId==buff);Assert.DoesNotContain(other.Chaos!.VisualStatuses,s=>s.BuffId==buff);Assert.Equal(original,OnlineJson.Write(m));
    }
    [Fact] public void WarningCellsTrackBothRealCollapseDeadlinesAndRestoreWithoutStoredProjection()
    {
        var m=Match(18,26,("King","White","c1"),("King","Black","f8"));Side(m).CompletedTurns=7;
        Assert.Empty(OnlineService.Snapshot(m,Now,"white").Chaos!.VisualBoard);Side(m).CompletedTurns=8;
        var warning=OnlineService.Snapshot(m,Now,"black").Chaos!.VisualBoard;Assert.Equal(16,warning.Count);Assert.All(warning,s=>{Assert.Equal("Warning",s.Kind);Assert.Equal(2,s.Remaining);Assert.Contains(s.Square[0],new[]{'a','h'});});
        m.Chaos!.CollapsedFiles=1;m.Chaos.CollapseSecondAt=5;m.Chaos.CombinedPlies=3;
        var board=OnlineService.Snapshot(m,Now,"white").Chaos!.VisualBoard;Assert.Equal(16,board.Count(s=>s.Kind=="Barrier"));Assert.All(board.Where(s=>s.Kind=="Warning"),s=>Assert.Contains(s.Square[0],new[]{'b','g'}));
        var bson=m.ToBsonDocument();Assert.False(bson["chaos"].AsBsonDocument.Contains("VisualBoard"));var restored=BsonSerializer.Deserialize<OnlineMatch>(bson);
        Assert.Equal(board,OnlineService.Snapshot(restored,Now,"black").Chaos!.VisualBoard);
    }
    [Fact] public void PublicMineAndCrateProjectionDoesNotInventRewardsOrDuplicateClaims()
    {
        var m=Match(26,9,("King","White","h1"),("King","Black","h8"),("Rook","White","b2"));m.Chaos!.Crates.Add(new(){Team="Black",Square="b3",Kind="Queen"});m.Chaos.Mines.Add(new(){Team="White",Square="a1"});
        foreach(string viewer in new[]{"white","black"})Assert.Equal(2,OnlineService.Snapshot(m,Now,viewer).Chaos!.VisualBoard.Count);
        Move(m,"b2","b3");Assert.Empty(m.Chaos.Deployments);Assert.Equal("Destroyed",m.Chaos.VisualCues.Single().Outcome);Assert.Equal("Black",m.Chaos.VisualCues.Single().Team);
        string original=OnlineJson.Write(m);Assert.Throws<OnlineException>(()=>Move(m,"b2","b3"));Assert.Equal(original,OnlineJson.Write(m));
    }
    [Fact] public void DecoyTransformationEndsItsExactVisualAndKingRemainsImmuneToPlague()
    {
        var m=Match(15,26,("King","White","a1"),("King","White","b2"),("King","Black","h8"),("Knight","Black","c3"));var decoy=At(m,"b2");Effect(m,"b2").Decoy=true;
        Move(m,"b2","c3");Assert.Equal("Knight",At(m,"c3").Kind);Assert.Equal(decoy.Id,m.Chaos!.VisualCues.Single().ActorId);Assert.Empty(OnlineService.Snapshot(m,Now,"white").Chaos!.VisualStatuses);
        m=Match(26,22,("King","White","a1"),("King","Black","h8"),("Pawn","Black","b2"));Move(m,"a1","b2");Assert.DoesNotContain(m.Chaos!.VisualCues,c=>c.BuffId==22);
    }
    [Fact] public void PlagueRefreshesFromTheAcceptedCaptureAndExpiresOnceAtTheNewDeadline()
    {
        var m=Match(26,22,("King","White","h1"),("King","Black","h8"),("Rook","White","b2"),("Pawn","Black","b3"),("Pawn","Black","b4"));
        Move(m,"b2","b3");Move(m,"h8","g8");Move(m,"b3","b4");
        Assert.Equal(2,m.Chaos!.VisualCues.Count(c=>c.BuffId==22&&c.Outcome=="Infect"));
        Assert.Contains(OnlineService.Snapshot(m,Now,"white").Chaos!.VisualStatuses,s=>s.BuffId==22&&s.Remaining==4);
        m.Chaos.CombinedPlies=6;m.Turn="White";Move(m,"h1","g1");
        Assert.DoesNotContain(m.Board,p=>p.Square=="b4");Assert.Single(m.Chaos.VisualCues,c=>c.BuffId==22&&c.Outcome=="Expire");
    }
    [Fact] public void ConsecutiveTeleportsConsumeExactlyFiveUsesAndReadinessRetires()
    {
        var m=Match(5,26,("King","White","h1"),("King","Black","h8"),("Queen","White","c2"));
        for(int i=0;i<5;i++){Side(m).CompletedTurns=i*5;m.Turn="White";Move(m,i%2==0?"c2":"e3",i%2==0?"e3":"c2");}
        Assert.Equal(5,Side(m).TeleportUses);Assert.Equal(5,m.Chaos!.VisualCues.Count(c=>c.BuffId==5));
        Assert.DoesNotContain(OnlineService.Snapshot(m,Now,"white").Chaos!.VisualStatuses,s=>s.BuffId==5);
        Side(m).CompletedTurns=30;m.Turn="White";string before=OnlineJson.Write(m);
        Assert.Throws<OnlineException>(()=>Move(m,"e3","c2"));Assert.Equal(before,OnlineJson.Write(m));
    }
    [Fact] public void RemovedSwapTargetCannotEmitAnAnimationOrMoveTheKing()
    {
        var m=Match(13,26,("King","White","e1"),("King","Black","h8"),("Rook","White","a1"));var ally=At(m,"a1");m.Board.Remove(ally);string before=OnlineJson.Write(m);
        Assert.Throws<OnlineException>(()=>engine.ApplyAbility(m,"White",new(){Kind="HideKing",TargetPieceId=ally.Id},Now));
        Assert.Equal(before,OnlineJson.Write(m));Assert.Empty(m.Chaos!.VisualCues);Assert.Equal("King",At(m,"e1").Kind);
    }
}
