using ChessGame.Api.Models;

namespace ChessGame.Api.Online;

public static class MatchmakingPolicy
{
    public static string PoolKey(GameSettings settings, bool rated = false) =>
        (rated ? "Ranked:" : "Normal:") + ModeNormalization.SettingsFingerprint(settings);
    public static int Range(OnlineTicket ticket, DateTime now, OnlineOptions options) =>
        (int)Math.Min(options.MaximumRatingRange, options.InitialRatingRange +
            Math.Max(0, (now - ticket.CreatedAt).TotalSeconds) * options.RatingRangePerSecond);
    public static double? Cost(OnlineTicket a, OnlineTicket b, DateTime now, OnlineOptions options, int priorRatedGames, DateTime? lastPlayed, bool smallPool = false)
    {
        if (a.UserId == b.UserId || a.Settings != b.Settings || a.Rated != b.Rated) return null;
        double waiting = Math.Min((now - a.CreatedAt).TotalSeconds, (now - b.CreatedAt).TotalSeconds);
        bool relaxed = smallPool && waiting >= options.SmallPoolRelaxSeconds;
        // Normal matches never wait for rating or ranked-opponent restrictions.
        // Settlement still enforces its own repeated-opponent rating limit.
        if (a.Rated && priorRatedGames >= 3 && !relaxed) return null;
        double gap = Math.Abs((long)a.Rating - b.Rating);
        if (a.Rated && !relaxed && gap > Math.Min(Range(a, now, options), Range(b, now, options))) return null;
        if (a.Rated && !relaxed && lastPlayed is not null && now - lastPlayed.Value < TimeSpan.FromSeconds(options.RecentOpponentSeconds) &&
            waiting < options.RecentOpponentRelaxSeconds) return null;
        bool networkKnown = a.RoundTripMilliseconds is not null && b.RoundTripMilliseconds is not null;
        double networkGap = networkKnown ? Math.Abs(a.RoundTripMilliseconds!.Value - b.RoundTripMilliseconds!.Value) : 0;
        if (!relaxed && networkKnown && networkGap > options.MaximumLatencyDifferenceMilliseconds) return null;
        return gap + (a.Provisional != b.Provisional ? 75 : 0) + networkGap / 4 +
            (lastPlayed is null ? 0 : 100) + (!networkKnown ? 15 : 0);
    }
    public static string PairKey(string a, string b) => string.CompareOrdinal(a, b) < 0 ? a + ":" + b : b + ":" + a;

    public static bool FirstIsWhite(ColorHistory a, ColorHistory b, Func<bool> tie)
    {
        double Cost(ColorHistory h, string color) => Math.Abs(h.WhiteGames - h.BlackGames + (color == "White" ? 1 : -1)) * 10 +
            (h.LastColor == color ? (h.Run >= 2 ? 1000 : 20) : 0);
        double first = Cost(a, "White") + Cost(b, "Black"), second = Cost(a, "Black") + Cost(b, "White");
        return first == second ? tie() : first < second;
    }
}
