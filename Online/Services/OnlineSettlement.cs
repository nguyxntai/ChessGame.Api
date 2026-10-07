using ChessGame.Api.Models;
using ChessGame.Api.Services.Ratings;
using MongoDB.Driver;

namespace ChessGame.Api.Online;

public sealed partial class OnlineService
{
    private async Task<(bool Rating, bool Rewards)> SettlementEligibility(IClientSessionHandle s, OnlineMatch m,
        OfficialResult result, DateTime now, CancellationToken ct)
    {
        result.RatingMode = m.Settings.Mode;
        result.RatingPolicy = RatingPolicies.Version(m.Settings.Mode);
        // Derive participation from durable server moves, including matches started before this release.
        var counts = new List<long>(2);
        foreach (var player in m.Players)
            counts.Add(await store.Moves.CountDocumentsAsync(s,
                x => x.MatchId == m.Id && x.Kind == "Move" && x.UserId == player.UserId,
                new CountOptions { Limit = 5 }, ct));
        result.StatsApplied = counts.All(c => c >= 1);
        if (!m.Rated) { result.RatingDecision = "Unrated"; result.RewardDecision = "Unrated"; return (false, false); }
        string? exclusion = m.SettlementReviewRequired ? "ReviewRequired" : !result.StatsApplied ? "NotPlayed" :
            result.Reason == "BothAbandoned" ? "BothAbandoned" : null;
        if (exclusion is null)
        {
            var pair = m.Players.Select(p => p.UserId).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var previous = await store.Matches.Find(s, x => x.SettlementPairKey == pair[0] + ":" + pair[1] &&
                x.FinishedAt > now.AddHours(-24) && x.Result!.RatingApplied).Limit(3).Project(x => x.Id).ToListAsync(ct);
            if (previous.Count >= 3) exclusion = "RepeatedOpponentLimit";
            m.SettlementPairKey = pair[0] + ":" + pair[1];
        }
        result.RatingApplied = exclusion is null;
        result.RatingDecision = exclusion ?? "Applied";
        string? rewardExclusion = exclusion ?? (result.Reason is "Abandonment" or "BothAbandoned" ? "Abandonment" :
            !SettlementRules.HasRewardParticipation(counts[0], counts[1], m.StartedAt, now)
                ? "InsufficientParticipation" : null);
        result.RewardsApplied = rewardExclusion is null;
        result.RewardDecision = rewardExclusion ?? "Applied";
        return (result.RatingApplied, result.RewardsApplied);
    }
}
