using ChessButWeird.Domain;

namespace ChessGame.Api.Online;

internal sealed partial class Chaos35Engine
{
    private static ChaosVisualCue Visual(OnlineMatch m, int buff, OnlinePiece actor, Square from, Square to, string outcome = "Resolved")
    {
        var a = m.Chaos!;
        var cue = new ChaosVisualCue { Sequence = ++a.VisualSequence, BuffId = buff, ActorId = actor.Id,
            Team = actor.Team, From = from.ToString(), To = to.ToString(), Outcome = outcome };
        a.VisualCues.Add(cue);
        if (a.VisualCues.Count > 24) a.VisualCues.RemoveRange(0, a.VisualCues.Count - 24);
        return cue;
    }
    private static void VisualChange(ChaosVisualCue cue,OnlinePiece p,string from,string? before,string? after)
    {cue.Changes.Add(new(p.Id,p.Team,from,p.Square,before,after));}
    private static void VisualArmy(OnlineMatch m,int buff,string team,IReadOnlyList<OnlinePiece> before,string outcome)
    {
        var after=m.Board.Where(p=>p.Team==team).ToList();var anchor=after.FirstOrDefault()??before.First();
        var cue=Visual(m,buff,anchor,GameRules.Square(anchor.Square),GameRules.Square(anchor.Square),outcome);
        foreach(var p in before)
        {
            var next=after.FirstOrDefault(n=>n.Id==p.Id);
            if(next is null||p.Kind!=next.Kind||p.Square!=next.Square||buff==19&&p.Kind is not ("King" or "Queen"))cue.Changes.Add(new(p.Id,team,p.Square,next?.Square??p.Square,p.Kind,next?.Kind));
        }
        foreach(var p in after.Where(p=>before.All(b=>b.Id!=p.Id)))VisualChange(cue,p,p.Square,null,p.Kind);
    }
}

internal static class ChaosVisualProjection
{
    // Run before privacy redaction, using real flags rather than a Doppelganger's displayed buff.
    public static void Fill(ChaosState state, IReadOnlyList<OnlinePiece> board)
    {
        state.VisualStatuses.Clear();
        state.VisualBoard.Clear();
        state.VisualHud.Clear();
        if (state.RulesVersion != 3 || state.Phase=="Draft") return;
        foreach (var side in state.Sides)
        foreach (var piece in board.Where(p => p.Team == side.Team))
        {
            var e = side.Effects.FirstOrDefault(e => e.PieceId == piece.Id);
            void Add(int id, int? left = null, int? percent = null) => state.VisualStatuses.Add(new(id, piece.Id, piece.Team, left, percent));
            if (e?.Bloodthirsty == true) Add(6);
            if (e?.SniperUntil > state.CombinedPlies) Add(7, e.SniperUntil - state.CombinedPlies);
            if (e?.CannonUntil > state.CombinedPlies) Add(16, e.CannonUntil - state.CombinedPlies);
            if (e?.Unstable == true) Add(17, percent: (int)Math.Round(100 * ChessButWeird.Domain.Chaos35.AramBalanceRules.BetrayalChance(e.BetrayalChecks)));
            if (side.BuffId == 4 && side.OriginalQueen == piece.Id && !side.BomberUsed) Add(4);
            if (side.BuffId == 25 && piece.Kind == "King" && e?.Decoy != true) Add(25, Math.Max(0, side.RifleReady - side.CompletedTurns));
            if(side.BuffId==5&&side.OriginalQueen==piece.Id&&side.TeleportUses<5)Add(5,Math.Max(0,side.TeleportReady-side.CompletedTurns));
            if(side.BuffId==13&&piece.Kind=="King"&&e?.Decoy!=true)Add(13,Math.Max(0,side.HidingReady-state.CombinedPlies));
            if(e?.Decoy==true)Add(15);
            if(side.BuffId==20&&piece.Kind=="King"&&e?.Decoy!=true&&side.EscapeUses<3)Add(20,3-side.EscapeUses);
            if(side.BuffId==21)Add(21);
            if(side.BuffId==30&&piece.Kind=="Rook")Add(30);
            if(e?.InfectedUntil>state.CombinedPlies)Add(22,e.InfectedUntil-state.CombinedPlies);
            if(side.CommandantPawns.Contains(piece.Id)||e?.Commandant==true)Add(0);
            if(side.FreestyleKnight==piece.Id)Add(2);
            if(side.BuffId==3&&!side.DoppelDisabled&&(side.SwappedKnight==piece.Id||side.SwappedBishop==piece.Id))Add(3,side.SwapActive?1:0);
            if(e?.QueenMastery==true)Add(29);
            if(e?.Passenger==true)Add(12,1);
            if(piece.Kind=="Pawn"&&state.Sides.Any(s=>s.Team!=piece.Team&&s.BuffId==33))Add(33,e?.TrapPawn==true?1:0);
            if(side.BuffId==10&&!side.PeaceFailed&&!side.PeaceUsed&&state.CombinedPlies>=14&&piece.Kind=="King"&&e?.Decoy!=true)Add(10);
        }
        foreach(var side in state.Sides)
        {
            if(side.BuffId==26)
            {
                string[] kinds={"Pawn","Knight","Bishop","Rook","Queen"};int mask=side.QuestPromoted?32:0;
                for(int i=0;i<5;i++)if(side.QuestCaptures.Contains(kinds[i]))mask|=1<<i;
                state.VisualHud.Add(new(26,side.Team,side.QuestRewarded?"Quest reward earned":"Capture each type + promote",System.Numerics.BitOperations.PopCount((uint)mask),6,mask));
            }
            if(side.BuffId==32)state.VisualHud.Add(new(32,side.Team,"KINGLESS",Math.Max(0,25-side.CompletedTurns),25,Unit:"owner turns"));
            if(side.BuffId==31)state.VisualHud.Add(new(31,side.Team,side.BoughtThisTurn?"Purchase used this turn":side.Credit<0?"Purchases blocked: in debt":"Purchase available",side.Credit,Unit:"points"));
            if(side.BuffId==8)state.VisualHud.Add(new(8,side.Team,"Extra moves used",side.ExtraUses,3));
            if(side.BuffId==10)state.VisualHud.Add(new(10,side.Team,side.PeaceFailed?"Recruitment failed":side.PeaceUsed?"Recruitment used":state.CombinedPlies>=14?"Recruitment ready":"Avoid check",Math.Max(0,14-state.CombinedPlies),Unit:"combined moves"));
        }
        // Mines are public in the current rules. A future hidden trap must be omitted by this projection.
        foreach(var mine in state.Mines)state.VisualBoard.Add(new(11,mine.Square,mine.Team,"Mine"));
        foreach(var crate in state.Crates)state.VisualBoard.Add(new(9,crate.Square,crate.Team,"Crate"));
        int collapsed=state.CollapsedFiles;
        for(int x=0;x<8;x++)if(x<collapsed||x>=8-collapsed)for(int y=0;y<8;y++)state.VisualBoard.Add(new(18,new ChessButWeird.Domain.Square(x,y).ToString(),"White","Barrier"));
        int left=collapsed==0?state.Sides.Where(s=>s.BuffId==18).Select(s=>10-s.CompletedTurns).DefaultIfEmpty(99).Min():state.CollapseSecondAt-state.CombinedPlies;
        if(collapsed<2&&left>0&&left<=2)foreach(int x in new[]{collapsed,7-collapsed})for(int y=0;y<8;y++)state.VisualBoard.Add(new(18,new ChessButWeird.Domain.Square(x,y).ToString(),"White","Warning",left));
    }
}
