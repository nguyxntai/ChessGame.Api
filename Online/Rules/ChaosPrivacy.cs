namespace ChessGame.Api.Online;

internal static class ChaosPrivacy
{
    public static void Redact(ChaosState state, string? viewerTeam)
    {
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
