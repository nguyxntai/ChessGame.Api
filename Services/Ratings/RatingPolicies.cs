using ChessGame.Api.Models;

namespace ChessGame.Api.Services.Ratings;

public static class RatingPolicies
{
    public const string ClassicVersion = "classic-elo-v1";
    public const string AramVersion = "aram-glicko2-v1";
    public static string Version(string mode) => mode switch
    { "Classic" => ClassicVersion, "Aram" or "ARAM" => AramVersion, _ => throw new ArgumentException("Unsupported rating mode.") };

    // Classic is instant game Elo inspired by FIDE, not official monthly FIDE rating.
    // No age data is collected; junior K and monthly rating-list rules are intentionally not inferred.
    public static ModeRating Classic(ModeRating player, ModeRating opponent, double score, DateTime now)
    {
        ValidateScore(score);
        double difference = opponent.Rating - player.Rating;
        if (player.Rating < 2650) difference = Math.Clamp(difference, -400, 400);
        double expected = 1 / (1 + Math.Pow(10, difference / 400));
        int k = player.Peak >= 2400 ? 10 : player.RatedGames < 30 ? 40 : 20;
        double rating = Math.Max(0, player.Rating + Math.Round(k * (score - expected), MidpointRounding.AwayFromZero));
        return Next(player, rating, player.Deviation, player.Volatility, now);
    }

    // ARAM uses one accepted match per period, tau=.2, a persistent RD floor for game noise,
    // and a 75 point movement cap. This adaptation needs calibration on real match data.
    public static ModeRating Aram(ModeRating player, ModeRating opponent, double score, DateTime now)
    {
        ValidateScore(score);
        var state = Glicko2.Update(new(player.Rating, DeviationAt(player, now), player.Volatility),
            new[] { new Glicko2.Opponent(opponent.Rating, DeviationAt(opponent, now), score) });
        return Next(player, Math.Max(0, player.Rating + Math.Clamp(state.Rating - player.Rating, -75, 75)),
            Math.Clamp(state.Deviation, 60, 350), state.Volatility, now);
    }

    public static double DeviationAt(ModeRating player, DateTime now)
    {
        double idleDays = player.LastRatedAt is null ? 0 : Math.Clamp((now - player.LastRatedAt.Value).TotalDays, 0, 3650);
        double phi = player.Deviation / 173.7178;
        return Math.Clamp(173.7178 * Math.Sqrt(phi * phi + idleDays * player.Volatility * player.Volatility), 60, 350);
    }
    public static int Display(double rating) => (int)Math.Round(rating, MidpointRounding.AwayFromZero);
    private static void ValidateScore(double score)
    { if (score is not (0 or .5 or 1)) throw new ArgumentOutOfRangeException(nameof(score)); }
    private static ModeRating Next(ModeRating before, double rating, double deviation, double volatility, DateTime now) => new()
    { Rating = rating, Deviation = deviation, Volatility = volatility, RatedGames = checked(before.RatedGames + 1),
        Peak = Math.Max(before.Peak, rating), LastRatedAt = now };
}
