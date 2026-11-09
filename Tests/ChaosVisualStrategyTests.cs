using ChessButWeird.Domain;
using ChessGame.Api.Online;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Xunit;

namespace ChessGame.Api.Tests;

public sealed partial class ChaosVisualTests
{
    private static MatchSnapshot SpectatorView(OnlineMatch m){var envelope=new OnlineEvent{PayloadJson=OnlineJson.Write(new{state=OnlineService.Snapshot(m,Now)})};var node=(System.Text.Json.Nodes.JsonNode)OnlineJson.EventPayload(envelope,"spectator")!;return System.Text.Json.JsonSerializer.Deserialize<MatchSnapshot>(node["state"]!.ToJsonString(),OnlineJson.Options)!;}
    private OnlineMatch StrategySetup(int buff, Action<OnlineMatch>? beforeInitialization=null)
    {
        var m=new OnlineMatch{Id="strategy-fixture",StateVersion=1,ReadyDeadline=Now.AddMinutes(3),Players=new(){new(){UserId="white",Color="White"},new(){UserId="black",Color="Black"}}};
        GameRules.Write(m,FenCodec.Parse(FenCodec.InitialPosition));engine.Initialize(m,Now);
        Side(m).DraftOptions=new(){buff};Side(m,"Black").DraftOptions=new(){6};
        engine.ApplyAbility(m,"White",new(){Kind="SelectBuff",BuffId=buff},Now);beforeInitialization?.Invoke(OnlineJson.Clone(m));
        engine.ApplyAbility(m,"Black",new(){Kind="SelectBuff",BuffId=6},Now);m.StateVersion++;
        if(m.Chaos!.Phase=="Playing")m.Status="InProgress";
        return m;
    }
    [Fact] public void NineteenStrategyBuffsExportRealTransformationsPlacementsAndTacticalOutcomes()
    {
        var fixtures=new List<object>();OnlineMatch before=null!;
        void Save(string name,OnlineMatch m)
        {
            if(m.Chaos!.Phase=="Playing")m.Status="InProgress";
            m.StateVersion=Math.Max(m.StateVersion,before.StateVersion+1);
            Assert.Contains(m.Chaos!.VisualCues,c=>c.Sequence>before.Chaos!.VisualSequence);
            fixtures.Add(new{name,white=new{before=OnlineService.Snapshot(before,Now,"white"),after=OnlineService.Snapshot(m,Now,"white")},black=new{before=OnlineService.Snapshot(before,Now,"black"),after=OnlineService.Snapshot(m,Now,"black")},spectator=new{before=SpectatorView(before),after=SpectatorView(m)}});
        }
        foreach(int id in new[]{19,24,28,32})
        {
            var initialized=StrategySetup(id,b=>before=b);Save("initial-"+id,initialized);
            var cue=initialized.Chaos!.VisualCues.Single(c=>c.BuffId==id);
            var recovered=BsonSerializer.Deserialize<OnlineMatch>(initialized.ToBson());Assert.Equal(cue.Changes,recovered.Chaos!.VisualCues.Single(c=>c.BuffId==id).Changes);
            foreach(var c in cue.Changes.Where(c=>c.ToKind!=null))Assert.Contains(initialized.Board,p=>p.Id==c.ActorId&&p.Square==c.To&&p.Kind==c.ToKind);
            if(id==24){Assert.Equal(31,initialized.Board.Count(p=>p.Team=="White"&&p.Kind=="Pawn"));Assert.Equal(46,cue.Changes.Count);}
            if(id==28)Assert.Equal(4,cue.Changes.Count);
            if(id==32)Assert.Equal("Queen",cue.Changes.Single().ToKind);
        }
        foreach(int id in new[]{0,2,3})
        {
            var selected=StrategySetup(id);before=OnlineJson.Clone(selected);
            var kinds=id==0?new[]{"Pawn","Pawn","Pawn"}:id==2?new[]{"Knight"}:new[]{"Knight","Bishop"};var ids=new List<int>();
            foreach(var kind in kinds)ids.Add(selected.Board.First(p=>p.Team=="White"&&p.Kind==kind&&!ids.Contains(p.Id)).Id);
            engine.ApplyAbility(selected,"White",new(){Kind="SelectTargets",PieceIds=ids},Now);Save("selected-"+id,selected);
            Assert.Equal(ids.Count,selected.Chaos!.VisualCues.Single().Changes.Count);
        }
        var m=StrategySetup(27);before=OnlineJson.Clone(m);engine.ApplyAbility(m,"White",new(){Kind="ReplaceQueen",PieceKind="Knight"},Now);Save("sacrifice",m);
        Side(m).CompletedTurns=10;m.Status="InProgress";before=OnlineJson.Clone(m);engine.ApplyAbility(m,"White",new(){Kind="RestoreQueen",TargetPieceId=At(m,"a2").Id},Now);Save("queen-restored",m);

        m=StrategySetup(23);before=OnlineJson.Clone(m);var placements=m.Board.Where(p=>p.Team=="White").Select(p=>new FormationPlacement(p.Id,p.Square=="a2"?"a3":p.Square)).ToList();
        engine.ApplyAbility(m,"White",new(){Kind="ConfirmFormation",Formation=placements},Now);Save("formation-applied",m);Assert.Equal("Pawn",At(m,"a3").Kind);
        m=Match(14,6,("King","White","a1"),("King","Black","h8"));Side(m).Tickets=1;before=OnlineJson.Clone(m);engine.ApplyAbility(m,"White",new(){Kind="BattleGacha"},Now);Save("gacha",m);
        var summoned=m.Chaos!.VisualCues.Single();Assert.Equal(At(m,summoned.To).Kind,summoned.Changes.Single().ToKind);Assert.Equal(0,Side(m).Tickets);
        m=Match(31,6,("King","White","a1"),("King","Black","h8"));Side(m).Credit=40;before=OnlineJson.Clone(m);engine.ApplyAbility(m,"White",new(){Kind="BuyPiece",PieceKind="Bishop",Target="c2"},Now);Save("purchase",m);
        Assert.Equal(before.Chaos!.Sides[0].Credit-Side(m).Credit,m.Chaos!.VisualCues.Single().Amount);

        m=Match(12,6,("King","White","h1"),("King","Black","h8"),("Rook","White","a1"),("Pawn","White","a2"));int rook=At(m,"a1").Id;before=OnlineJson.Clone(m);
        engine.ApplyAbility(m,"White",new(){Kind="LoadPawn",PieceId=rook,TargetPieceId=At(m,"a2").Id},Now);Save("embarked",m);
        Side(m).CompletedTurns=1;before=OnlineJson.Clone(m);engine.ApplyAbility(m,"White",new(){Kind="UnloadPawn",PieceId=rook,Target="a2"},Now);Save("disembarked",m);
        m=Match(12,6,("King","White","h1"),("King","Black","h8"),("Rook","White","a3"),("Rook","Black","a6"));Effect(m,"a3").Passenger=true;m.Turn="Black";Move(m,"a6","a3");before=OnlineJson.Clone(m);
        engine.ApplyAbility(m,"White",new(){Kind="Deploy",Target="b3"},Now);Save("passenger-parachute",m);Assert.Equal("Parachuted",m.Chaos!.VisualCues.Last().Outcome);
        m=Match(29,6,("King","White","a1"),("King","Black","h8"),("Knight","White","c3"),("Queen","Black","d5"));before=OnlineJson.Clone(m);Move(m,"c3","d5");Save("mastery",m);Assert.Equal("Knight",At(m,"d5").Kind);
        m=Match(6,33,("King","White","a1"),("King","Black","h8"),("Pawn","White","c7"));before=OnlineJson.Clone(m);Move(m,"c7","c8");Save("trap-transformed",m);Assert.Equal("Pawn",At(m,"c8").Kind);
        m=Match(34,6,("King","White","a1"),("King","Black","h8"),("Pawn","White","c7"));engine.ApplyMove(m,new(GameRules.Square("c7"),GameRules.Square("c8"),PieceKind.Rook));before=OnlineJson.Clone(m);
        engine.ApplyAbility(m,"White",new(){Kind="Deploy",Target="c4"},Now);Save("paratrooper",m);Assert.Equal("Rook",At(m,"c4").Kind);
        m=Match(2,6,("King","White","a1"),("King","Black","h8"),("Knight","White","b1"));Side(m).FreestyleKnight=At(m,"b1").Id;before=OnlineJson.Clone(m);Move(m,"b1","b3");Save("freestyle-leap",m);
        m=Match(1,6,("King","White","e1"),("King","Black","h8"),("Rook","White","h1"));before=OnlineJson.Clone(m);Move(m,"e1","g1");Save("fortress-castle",m);Assert.Equal(2,m.Chaos!.VisualCues.Single().Changes.Count);
        m=Match(10,6,("King","White","a1"),("King","Black","h8"),("Knight","Black","f6"));m.Chaos!.CombinedPlies=14;before=OnlineJson.Clone(m);
        engine.ApplyAbility(m,"White",new(){Kind="Recruit",TargetPieceId=At(m,"f6").Id,Target="b2"},Now);Save("peace-recruited",m);Assert.True(Side(m).PeaceUsed);
        m=Match(26,6,("King","White","a1"),("King","Black","h8"),("Pawn","White","b2"),("Pawn","White","c2"),("Knight","White","c3"),("Knight","Black","e4"));
        Side(m).QuestPromoted=true;Side(m).QuestCaptures=new(){"Pawn","Bishop","Rook","Queen"};Move(m,"c3","e4");before=OnlineJson.Clone(m);
        engine.ApplyAbility(m,"White",new(){Kind="Deploy",Target="b2"},Now);Save("quest-reward",m);
        bool granted=false,failed=false;
        for(int i=0;i<200&&(!granted||!failed);i++)
        {
            m=Match(8,6,("King","White","a1"),("King","Black","h8"),("Pawn","White","c3"),("Knight","Black","d4"));before=OnlineJson.Clone(m);Move(m,"c3","d4");
            if(m.Turn=="White"){Assert.Equal(1,Side(m).ExtraUses);Assert.Equal(1,m.Chaos!.VisualCues.Single().Amount);if(!granted)Save("gambling-extra-move",m);granted=true;}
            else{Assert.DoesNotContain(m.Chaos!.VisualCues,c=>c.BuffId==8);failed=true;}
        }
        Assert.True(granted&&failed,"Actual random gameplay must exercise success and failure within 200 trials.");
        string? output=Environment.GetEnvironmentVariable("BUFF_VFX_STRATEGY_FIXTURE_OUTPUT");if(output!=null)File.WriteAllText(output,OnlineJson.Write(fixtures));
    }

    [Fact] public void DoppelgangerPrivateFxPairAndLegalPreviewsAreSanitizedForOpponentSpectatorAndReplay()
    {
        var m=StrategySetup(3);int knight=At(m,"b1").Id,bishop=At(m,"c1").Id;
        engine.ApplyAbility(m,"White",new(){Kind="SelectTargets",PieceIds=new(){knight,bishop}},Now);
        m.Status="InProgress";m.Chaos!.CombinedPlies=4;
        engine.ApplyAbility(m,"White",new(){Kind="ToggleSwap"},Now);
        // The central policy protects future pair cues even if an author omits the flag.
        m.Chaos!.VisualCues.Add(new(){Sequence=++m.Chaos.VisualSequence,BuffId=3,Team="White",ActorId=knight,From="b1",To="b1"});
        var owner=OnlineService.Snapshot(m,Now,"white");Assert.NotEmpty(owner.Chaos!.VisualCues);Assert.NotEmpty(owner.Chaos.LegalMoves!);Assert.Equal(2,owner.Chaos.VisualStatuses.Count(s=>s.BuffId==3));
        foreach(string user in new[]{"black","spectator"})
        {
            var view=user=="spectator"?SpectatorView(m):OnlineService.Snapshot(m,Now,user);var hidden=view.Chaos!.Sides.Single(s=>s.Team=="White");
            Assert.NotEqual(3,hidden.BuffId);Assert.Null(hidden.SwappedKnight);Assert.Null(hidden.SwappedBishop);Assert.Empty(view.Chaos.LegalMoves!);
            Assert.DoesNotContain(view.Chaos.VisualStatuses,s=>s.BuffId==3);Assert.DoesNotContain(view.Chaos.VisualCues,c=>c.BuffId==3);
            var envelope=new OnlineEvent{PayloadJson=OnlineJson.Write(new{state=OnlineService.Snapshot(m,Now),command=new{kind="ToggleSwap",pieceIds=new[]{knight,bishop}}})};
            string replay=OnlineJson.Write(OnlineJson.EventPayload(envelope,user));
            Assert.Contains("\"command\":null",replay);Assert.DoesNotContain("\"ownerOnly\":true",replay);Assert.DoesNotContain("\"swappedKnight\":"+knight,replay);
        }
        m.Turn="Black";Assert.Empty(OnlineService.Snapshot(m,Now,"white").Chaos!.LegalMoves!);Assert.NotEmpty(OnlineService.Snapshot(m,Now,"black").Chaos!.LegalMoves!);
        Assert.Contains(OnlineService.Snapshot(m,Now,"white").Chaos!.VisualStatuses,s=>s.BuffId==3);
    }
    [Fact] public void LegacyReplayWithoutLegalMoveOwnerDoesNotExposePrivatePreviews()
    {
        var m=StrategySetup(3);
        engine.ApplyAbility(m,"White",new(){Kind="SelectTargets",PieceIds=new(){At(m,"b1").Id,At(m,"c1").Id}},Now);m.Status="InProgress";
        var legacy=System.Text.Json.Nodes.JsonNode.Parse(OnlineJson.Write(new{state=OnlineService.Snapshot(m,Now)}))!;
        Assert.NotEmpty(legacy["state"]!["chaos"]!["legalMoves"]!.AsArray());
        legacy["state"]!["chaos"]!.AsObject().Remove("legalMoveTeam");
        var envelope=new OnlineEvent{PayloadJson=legacy.ToJsonString()};
        foreach(string viewer in new[]{"white","black","spectator"})
        {
            var projected=(System.Text.Json.Nodes.JsonNode)OnlineJson.EventPayload(envelope,viewer)!;
            Assert.Empty(projected["state"]!["chaos"]!["legalMoves"]!.AsArray());
        }
    }
    [Fact] public void StrategyHudUsesExactCountersAndIsOwnerOnlyAndDerivedRatherThanPersisted()
    {
        var m=Match(32,26,("Queen","White","a1"),("King","Black","h8"));Side(m).CompletedTurns=23;Side(m,"Black").QuestCaptures=new(){"Pawn","Rook"};Side(m,"Black").QuestPromoted=true;
        var white=OnlineService.Snapshot(m,Now,"white");var timer=Assert.Single(white.Chaos!.VisualHud);Assert.Equal(2,timer.Current);Assert.Equal("owner turns",timer.Unit);Assert.Equal(25,timer.Target);
        var quest=Assert.Single(OnlineService.Snapshot(m,Now,"black").Chaos!.VisualHud);Assert.Equal(3,quest.Current);Assert.Equal(6,quest.Target);Assert.Equal(41,quest.Mask);
        Assert.Empty(SpectatorView(m).Chaos!.VisualHud);
        Side(m).CompletedTurns=25;Assert.Equal(("Black","PoorKingDeadline"),engine.Outcome(m)!.Value);
        m.Chaos!.VisualHud.Add(timer);m.Chaos.LegalMoveTeam="White";var bson=m.ToBsonDocument()["chaos"].AsBsonDocument;
        Assert.False(bson.Contains("VisualHud"));Assert.False(bson.Contains("LegalMoveTeam"));
        var restored=BsonSerializer.Deserialize<OnlineMatch>(m.ToBson());Assert.Empty(restored.Chaos!.VisualHud);Assert.Equal(0,Assert.Single(OnlineService.Snapshot(restored,Now,"white").Chaos!.VisualHud).Current);
    }
    [Fact] public void FailedTransactionsUnsafeFormationAndInvalidTargetsPublishNothingAndDoNotCharge()
    {
        var m=Match(31,6,("King","White","a1"),("King","Black","h8"));Side(m).Credit=40;string original=OnlineJson.Write(m);
        foreach(string square in new[]{"a1","a8","i2"}){Assert.Throws<OnlineException>(()=>engine.ApplyAbility(m,"White",new(){Kind="BuyPiece",PieceKind="Queen",Target=square},Now));Assert.Equal(original,OnlineJson.Write(m));}
        Side(m).Credit=-1;original=OnlineJson.Write(m);Assert.Throws<OnlineException>(()=>engine.ApplyAbility(m,"White",new(){Kind="BuyPiece",PieceKind="Queen",Target="b2"},Now));Assert.Equal(original,OnlineJson.Write(m));
        m=StrategySetup(23);original=OnlineJson.Write(m);var bad=m.Board.Where(p=>p.Team=="White").Select(p=>new FormationPlacement(p.Id,"a4")).ToList();
        Assert.Throws<OnlineException>(()=>engine.ApplyAbility(m,"White",new(){Kind="ConfirmFormation",Formation=bad},Now));Assert.Equal(original,OnlineJson.Write(m));
        m.Board.Add(new(){Id=999,Team="Black",Kind="Rook",Square="e5",Forward=-1});original=OnlineJson.Write(m);
        var unsafePlan=m.Board.Where(p=>p.Team=="White").Select(p=>new FormationPlacement(p.Id,p.Square=="e2"?"d3":p.Square)).ToList();
        Assert.Throws<OnlineException>(()=>engine.ApplyAbility(m,"White",new(){Kind="ConfirmFormation",Formation=unsafePlan},Now));Assert.Equal(original,OnlineJson.Write(m));
        m=StrategySetup(0);original=OnlineJson.Write(m);int id=At(m,"a2").Id;
        Assert.Throws<OnlineException>(()=>engine.ApplyAbility(m,"White",new(){Kind="SelectTargets",PieceIds=new(){id,id,id}},Now));Assert.Equal(original,OnlineJson.Write(m));
    }
}
