using ChessButWeird.Domain;

namespace ChessGame.Api.Online;

// The version is pinned when a match is created. Stored v2 matches finish on v2.
public sealed class ChaosEngine
{
    public const int CurrentRulesVersion = 3;
    public static readonly int[][] Tiers = Chaos35Engine.Tiers;
    private readonly LegacyChaosEngine legacy = new();
    private readonly Chaos35Engine current = new();
    private static bool Current(OnlineMatch m)
    {
        if (m.Chaos?.RulesVersion is not (2 or 3)) throw new OnlineException("UnsupportedChaosRules");
        return m.Chaos.RulesVersion == 3;
    }
    public void Initialize(OnlineMatch m, DateTime now) => current.Initialize(m, now);
    public bool SetupDone(OnlineMatch m) => m.Chaos is null || m.Chaos.Phase == "Playing";
    public void ApplyMove(OnlineMatch m, Move move)
    { if (Current(m)) current.ApplyMove(m, move); else legacy.ApplyMove(m, move); }
    public void SetupAbility(OnlineMatch m, string team, AbilityCommand c, DateTime now) => ApplyAbility(m, team, c, now);
    public void ApplyAbility(OnlineMatch m, string team, AbilityCommand c, DateTime now)
    { if (Current(m)) current.ApplyAbility(m, team, c, now); else legacy.ApplyAbility(m, team, c, now); }
    public (string? WinnerTeam, string Reason)? Outcome(OnlineMatch m) => Current(m) ? current.Outcome(m) : legacy.Outcome(m);
    public bool Tick(OnlineMatch m, DateTime now) => m.Chaos is not null && (Current(m) ? current.Tick(m, now) : legacy.Tick(m, now));
    public static string? DrawWinner(OnlineMatch m)
    {
        if (m.Chaos?.RulesVersion != 3) return null;
        bool white = m.Chaos.Sides.Any(s => s.Team == "White" && s.BuffId is 26 or 32);
        bool black = m.Chaos.Sides.Any(s => s.Team == "Black" && s.BuffId is 26 or 32);
        string? winner = white == black ? null : white ? "Black" : "White";
        if (winner is null) return null;
        var side = m.Chaos.Sides.Single(s => s.Team == winner);
        bool alive = side.BuffId == 32 ? m.Board.Any(p => p.Team == winner) : m.Board.Any(p => p.Team == winner && p.Kind == "King" && !side.Effects.Any(e => e.PieceId == p.Id && e.Decoy));
        return alive ? winner : null;
    }
}
