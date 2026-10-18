namespace ChessGame.Api.Services.Ranks;

public sealed class RankOptions
{
    public string Version { get; set; } = "";
    public int PlacementRequired { get; set; } = 10;
    public Dictionary<string, RankTier[]> Modes { get; set; } = new();
    private static readonly string[] Ids = { "Pawn", "Knight", "Bishop", "Rook", "Queen", "King", "Grandmaster", "Legend", "Celestial" };

    public static bool Valid(RankOptions o) => !string.IsNullOrWhiteSpace(o.Version) && o.PlacementRequired == 10 &&
        o.Modes.Count == 2 && new[] { "Classic", "Chaos" }.All(mode =>
            o.Modes.TryGetValue(mode, out var tiers) && tiers is { Length: 9 } &&
            tiers.OrderBy(t => t.Order).Select(t => t.TierId).SequenceEqual(Ids) &&
            tiers.Select(t => t.Order).Order().SequenceEqual(Enumerable.Range(1, 9)) &&
            tiers.All(t => !string.IsNullOrWhiteSpace(t.DisplayName) && !string.IsNullOrWhiteSpace(t.BadgeKey) &&
                System.Text.RegularExpressions.Regex.IsMatch(t.ColorHex ?? "", "^#[0-9a-fA-F]{6}$")) &&
            tiers.OrderBy(t => t.Order).First().MinRating == 0 &&
            tiers.OrderBy(t => t.Order).Zip(tiers.OrderBy(t => t.Order).Skip(1)).All(pair => pair.First.MinRating < pair.Second.MinRating));
}

public sealed record RankTier(string TierId, int Order, string DisplayName, int MinRating, string ColorHex, string BadgeKey);
public sealed record RankConfig(string Mode, string Version, int PlacementRequired, string RatingRounding, IReadOnlyList<RankTier> Tiers);
