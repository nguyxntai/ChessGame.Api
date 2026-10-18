using ChessGame.Api.Services.Ranks;
using System.Text.Json.Serialization;

namespace ChessGame.Api.DTOs.Leaderboard;

public sealed record LeaderboardEntry(long? Rank, string UserId, string Username,
    string DisplayName, string? AvatarId, int Elo, string Mode = "Classic", int RatedGames = 0, double? Deviation = null, bool Provisional = false,
    [property: JsonIgnore] RankProfile? RankDetails = null)
{
    public long? LeaderboardPosition => Rank;
    public int Rating => Elo;
    public string? TierId => RankDetails?.TierId;
    public int PlacementPlayed => RankDetails?.PlacementPlayed ?? Math.Clamp(RatedGames, 0, 10);
    public int PlacementRequired => RankDetails?.PlacementRequired ?? 10;
    public int? TierMinRating => RankDetails?.TierMinRating;
    public string? NextTierId => RankDetails?.NextTierId;
    public int? NextTierMinRating => RankDetails?.NextTierMinRating;
    public int? PointsToNextTier => RankDetails?.PointsToNextTier;
    public int? PeakRating => RankDetails?.PeakRating;
    public string? PeakTierId => RankDetails?.PeakTierId;
    public string? RankConfigVersion => RankDetails?.RankConfigVersion;
}
