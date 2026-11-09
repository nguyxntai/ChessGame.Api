namespace ChessGame.Api.Online;

internal static class ChaosPrivacy
{
    public static void Redact(ChaosState state, string? viewerTeam)
    {
        // Readiness/remaining uses belong to the owner HUD. Completed actions remain public.
        state.VisualStatuses.RemoveAll(s => (s.BuffId is 3 or 5 or 10 or 13 or 20 or 25) && s.Team != viewerTeam);
        state.VisualCues.RemoveAll(c=>(c.OwnerOnly||c.BuffId==3)&&c.Team!=viewerTeam);
        state.VisualHud.RemoveAll(h=>h.Team!=viewerTeam);
        if(state.RulesVersion==3&&(viewerTeam is not ("White" or "Black")||state.LegalMoveTeam!=viewerTeam))state.LegalMoves=new();
        foreach (var side in state.Sides)
        {
            if (side.Team == viewerTeam) continue;
            side.DraftOptions.Clear();
            if (state.RulesVersion == 3 && side.BuffId == 3)
            {
                side.BuffId = side.DisguisedBuffId;
                side.SwappedKnight = side.SwappedBishop = null;
                side.SwapActive = side.DoppelDisabled = false; side.SwapReady = 0;
            }
            side.DisguisedBuffId = null;
        }
    }
}
