using ChessGame.Api.Models;
using ChessGame.Api.Online;
using ChessGame.Api.Services.Ratings;
using Microsoft.Extensions.Options;

namespace ChessGame.Api.Services.Ranks;

public sealed record RankProfile(string Mode, int Rating, string? TierId, bool Provisional,
    int PlacementPlayed, int PlacementRequired, int RatedGames, int? TierMinRating,
    string? NextTierId, int? NextTierMinRating, int? PointsToNextTier,
    int PeakRating, string? PeakTierId, long? LeaderboardPosition, string RankConfigVersion);
public sealed record PlayerRankProfiles(RankProfile Classic, RankProfile Chaos);

public sealed class RankService(IOptions<RankOptions> options)
{
    private readonly RankOptions config = options.Value;
    public RankConfig Config(string mode)
    {
        mode = ModeNormalization.Require(mode);
        // Return a copy so configuration used by settlement cannot be mutated by a caller.
        return new(mode, config.Version, config.PlacementRequired, "NearestIntegerAwayFromZero",
            config.Modes[mode].OrderBy(t => t.Order).ToArray());
    }
    public PlayerRankProfiles Profiles(PlayerRatings ratings) => new(Profile("Classic", ratings.Classic), Profile("Chaos", ratings.Chaos));
    public RankProfile Profile(string mode, ModeRating state, long? position = null)
    {
        var c = Config(mode);
        int rating = RatingPolicies.Display(state.Rating), peak = RatingPolicies.Display(state.Peak);
        bool provisional = state.RatedGames < c.PlacementRequired;
        var tier = provisional ? null : c.Tiers.Last(t => rating >= t.MinRating);
        var next = tier is null ? null : c.Tiers.FirstOrDefault(t => t.Order == tier.Order + 1);
        return new(c.Mode, rating, tier?.TierId, provisional, Math.Clamp(state.RatedGames, 0, c.PlacementRequired),
            c.PlacementRequired, state.RatedGames, tier?.MinRating, next?.TierId, next?.MinRating,
            next is null ? null : next.MinRating - rating, peak,
            provisional ? null : c.Tiers.Last(t => peak >= t.MinRating).TierId, position, c.Version);
    }
    public void Snapshot(PlayerResult result, string mode, ModeRating before, ModeRating after, bool applied)
    {
        var a = Profile(mode, before); var b = Profile(mode, after);
        result.TierBefore = a.TierId; result.TierAfter = b.TierId;
        result.ProvisionalBefore = a.Provisional; result.ProvisionalAfter = b.Provisional;
        result.RankConfigVersion = config.Version;
        result.RankChangeType = !applied ? "None" : a.Provisional && !b.Provisional ? "PlacementCompleted" :
            a.TierId == b.TierId ? "None" : b.Rating > a.Rating ? "Promoted" : "Demoted";
    }
}
