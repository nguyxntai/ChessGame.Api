using System.Text.Json.Serialization;

namespace ChessGame.Api.Services.Ratings;

public enum FidePool { Standard, Rapid, Blitz }
public sealed record FidePlayer([property: JsonRequired] string Id, [property: JsonRequired] int Rating, [property: JsonRequired] int PeakPublishedRating, [property: JsonRequired] int CompletedRatedGames, int? VerifiedBirthYear = null);
public sealed record FideOpponentResult(int Rating, decimal Score);
public sealed record FidePeriodResult(string PlayerId, int RatingBefore, int? PublishedRatingAfter, int Change, int K, int Games);
public sealed record FideInitialGame(string EventId, DateOnly Date, int OpponentRating, decimal Score);
public sealed record FideGame([property: JsonRequired] string Id, [property: JsonRequired] string WhiteId, [property: JsonRequired] string BlackId, [property: JsonRequired] decimal WhiteScore, [property: JsonRequired] string Mode, [property: JsonRequired] FidePool Pool,
    [property: JsonRequired] int WhiteInitialSeconds, [property: JsonRequired] int BlackInitialSeconds, [property: JsonRequired] int IncrementSeconds, [property: JsonRequired] bool BothMoved,
    [property: JsonRequired] bool EventEligibilityVerified, bool FairPlayExcluded = false, bool ForceMajeure = false);
public sealed record FideSimulationInput([property: JsonRequired] DateOnly Period, [property: JsonRequired] FidePool Pool, [property: JsonRequired] List<FidePlayer> Players, [property: JsonRequired] List<FideGame> Games);
public sealed record FideExcludedGame(string GameId, string Reason);
public sealed record FideSimulationReport(string Policy, DateOnly Period, FidePool Pool,
    List<FidePeriodResult> Players, List<FideExcludedGame> ExcludedGames);

// Offline simulation with trusted list snapshots. Never writes online ratings or accepts client settlement data.
// Sources: FIDE Handbook B022024 sections 8.1-8.3; B02RBRegulations2024 sections 1,4,7.
public static class FideSimulation
{
    public const string Policy = "fide-period-simulation-2025-10-v1";
    private static readonly int[] ProbabilityUpperBounds =
        [3,10,17,25,32,39,46,53,61,68,76,83,91,98,106,113,121,129,137,145,153,162,170,179,188,197,
         206,215,225,235,245,256,267,278,290,302,315,328,344,357,374,391,411,432,456,484,517,559,619,735];
    // dp for p=.50 through 1.00. Lower half is the symmetric negative table.
    private static readonly int[] PerformanceDifferences =
        [0,7,14,21,29,36,43,50,57,65,72,80,87,95,102,110,117,125,133,141,149,158,166,175,184,193,
         202,211,220,230,240,251,262,273,284,296,309,322,336,351,366,383,401,422,444,470,501,538,589,677,800];

    public static decimal Expected(int ownRating, int opponentRating, FidePool pool)
    {
        ValidatePool(pool);
        if (ownRating < 1400 || opponentRating < 1400) throw new ArgumentException("Published ratings must be at least 1400.");
        long signed = (long)ownRating - opponentRating, difference = Math.Abs(signed);
        if (pool != FidePool.Standard || ownRating < 2650) difference = Math.Min(400, difference);
        int index = Array.FindIndex(ProbabilityUpperBounds, upper => difference <= upper);
        decimal high = index < 0 ? 1m : .50m + index / 100m;
        return signed >= 0 ? high : 1m - high;
    }
    public static int DevelopmentCoefficient(FidePlayer player, int year, int gamesInPeriod)
    {
        ValidatePlayer(player, year);
        if (gamesInPeriod < 0) throw new ArgumentOutOfRangeException(nameof(gamesInPeriod));
        int k = player.PeakPublishedRating >= 2400 || player.Rating >= 2400 ? 10 :
            player.CompletedRatedGames < 30 || player.VerifiedBirthYear is { } birth && year <= birth + 18 && player.Rating < 2300 ? 40 : 20;
        return gamesInPeriod == 0 ? k : Math.Min(k, 700 / gamesInPeriod);
    }
    public static FidePeriodResult CalculatePeriod(FidePlayer player, IReadOnlyList<FideOpponentResult> games, FidePool pool, int year)
    {
        ValidatePool(pool);
        int k = DevelopmentCoefficient(player, year, games.Count);
        decimal delta = 0;
        foreach (var game in games) { ValidateScore(game.Score); delta += game.Score - Expected(player.Rating, game.Rating, pool); }
        // Decimal arithmetic prevents floating-point drift at .5 rounding boundaries.
        int change = checked((int)decimal.Round(delta * k, 0, MidpointRounding.AwayFromZero));
        int after = checked(player.Rating + change);
        return new(player.Id, player.Rating, after < 1400 ? null : after, change, k, games.Count);
    }
    public static int? InitialRating(IReadOnlyList<FideInitialGame> games, DateOnly listPeriod)
    {
        if (listPeriod.Day != 1) throw new ArgumentException("Use the first day of the rating-list month.");
        foreach (var g in games)
        {
            ValidateScore(g.Score);
            if (g.OpponentRating < 1400 || string.IsNullOrWhiteSpace(g.EventId) || g.Date >= listPeriod)
                throw new ArgumentException("Initial games need a rated opponent, event ID and a past date.");
        }
        var events = games.GroupBy(g => g.EventId).OrderBy(g => g.Min(x => x.Date)).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
        // Zero-score events before the first scoring event are disregarded.
        var scored = events.SkipWhile(e => e.Sum(g => g.Score) == 0).SelectMany(e => e)
            .Where(g => g.Date >= listPeriod.AddMonths(-26)).ToList();
        if (scored.Count < 5) return null;
        decimal average = (scored.Sum(g => (decimal)g.OpponentRating) + 3600m) / (scored.Count + 2);
        decimal fraction = (scored.Sum(g => g.Score) + 1m) / (scored.Count + 2);
        int percentage = (int)decimal.Round(fraction * 100, 0, MidpointRounding.AwayFromZero);
        int dp = percentage >= 50 ? PerformanceDifferences[percentage - 50] : -PerformanceDifferences[50 - percentage];
        int rating = Math.Min(2200, (int)decimal.Round(average + dp, 0, MidpointRounding.AwayFromZero));
        return rating < 1400 ? null : rating;
    }
    public static string? Exclusion(FideGame game, FidePlayer white, FidePlayer black)
    {
        ValidatePool(game.Pool); ValidateScore(game.WhiteScore);
        if (game.WhiteInitialSeconds < 0 || game.BlackInitialSeconds < 0 || game.IncrementSeconds is < 0 or > 3600)
            throw new ArgumentException("Invalid time control.");
        if (game.Mode != "Classic") return "NotClassic";
        // Registration, arbiter/laws, reporting deadlines, daily limits, hybrid approval and match/event
        // exceptions must be verified externally. A boolean is an admin attestation, not certification.
        if (!game.EventEligibilityVerified) return "EventEligibilityUnverified";
        if (!game.BothMoved) return "Unplayed";
        if (game.FairPlayExcluded) return "FairPlayExcluded";
        if (game.ForceMajeure) return "ForceMajeure";
        long whiteTime = game.WhiteInitialSeconds + 60L * game.IncrementSeconds;
        long blackTime = game.BlackInitialSeconds + 60L * game.IncrementSeconds;
        if (game.Pool == FidePool.Standard)
        {
            int high = Math.Max(white.Rating, black.Rating), minimum = high >= 2400 ? 7200 : high >= 1800 ? 5400 : 3600;
            if (Math.Min(whiteTime, blackTime) < minimum) return "StandardTimeTooShort";
        }
        else
        {
            if (whiteTime != blackTime) return "UnequalTimeControls";
            bool valid = game.Pool == FidePool.Rapid ? whiteTime > 600 && whiteTime < 3600 : whiteTime > 180 && whiteTime <= 600;
            if (!valid) return "WrongTimePool";
            if (Math.Max(white.Rating, black.Rating) > 2600 && Math.Abs((long)white.Rating - black.Rating) >= 600) return "RapidBlitzLargeGap";
        }
        return null;
    }
    public static FideSimulationReport Run(FideSimulationInput input)
    {
        ValidatePool(input.Pool);
        if (input.Period.Day != 1 || input.Period < new DateOnly(2025, 10, 1)) throw new ArgumentException("This policy supports list periods starting October 2025; use the first day of the month.");
        if (input.Players is null || input.Games is null || input.Players.Any(p => p is null) || input.Games.Any(g => g is null))
            throw new ArgumentException("Players and games cannot be null.");
        foreach (var player in input.Players) ValidatePlayer(player, input.Period.Year);
        if (input.Players.Select(p => p.Id).Distinct().Count() != input.Players.Count ||
            input.Games.Any(g => string.IsNullOrWhiteSpace(g.Id)) || input.Games.Select(g => g.Id).Distinct().Count() != input.Games.Count)
            throw new ArgumentException("Player and game IDs must be unique.");
        var players = input.Players.ToDictionary(p => p.Id);
        var eligible = players.Keys.ToDictionary(id => id, _ => new List<FideOpponentResult>());
        var excluded = new List<FideExcludedGame>();
        foreach (var game in input.Games)
        {
            ValidatePool(game.Pool); ValidateScore(game.WhiteScore);
            if (game.WhiteId == game.BlackId || !players.TryGetValue(game.WhiteId, out var white) || !players.TryGetValue(game.BlackId, out var black))
                throw new ArgumentException("Game participants must be distinct and present in the list snapshot.");
            string? reason = game.Pool != input.Pool ? "DifferentPool" : Exclusion(game, white, black);
            if (reason is not null) { excluded.Add(new(game.Id, reason)); continue; }
            eligible[white.Id].Add(new(black.Rating, game.WhiteScore)); eligible[black.Id].Add(new(white.Rating, 1m - game.WhiteScore));
        }
        return new(Policy, input.Period, input.Pool, input.Players.OrderBy(p => p.Id, StringComparer.Ordinal)
            .Select(p => CalculatePeriod(p, eligible[p.Id], input.Pool, input.Period.Year)).ToList(),
            excluded.OrderBy(g => g.GameId, StringComparer.Ordinal).ToList());
    }
    private static void ValidateScore(decimal score) { if (score is not (0m or .5m or 1m)) throw new ArgumentException("A score must be 0, 0.5 or 1."); }
    private static void ValidatePool(FidePool pool) { if (!Enum.IsDefined(pool)) throw new ArgumentException("Unknown rating pool."); }
    private static void ValidatePlayer(FidePlayer player, int year)
    {
        if (string.IsNullOrWhiteSpace(player.Id) || player.Rating < 1400 || player.PeakPublishedRating < player.Rating || player.CompletedRatedGames < 0 ||
            year is < 2025 or > 9999 || player.VerifiedBirthYear is { } birth && (birth < 1900 || birth > year)) throw new ArgumentException("Invalid verified list snapshot.");
    }
}
