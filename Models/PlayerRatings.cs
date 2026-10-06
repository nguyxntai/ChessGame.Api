using MongoDB.Bson.Serialization.Attributes;

namespace ChessGame.Api.Models;

public sealed class PlayerRatings
{
    [BsonElement("version")] public int Version { get; set; } = 1;
    [BsonElement("legacyMixedElo")] public int? LegacyMixedElo { get; set; }
    [BsonElement("classic")] public ModeRating Classic { get; set; } = new();
    [BsonElement("aram")] public ModeRating Aram { get; set; } = new();
    public ModeRating For(string mode) => mode switch
    {
        "Classic" => Classic, "Aram" or "ARAM" => Aram,
        _ => throw new ArgumentException("Unsupported rating mode.", nameof(mode))
    };
}

public sealed class ModeRating
{
    [BsonIgnore] public bool Provisional => RatedGames < 10;
    [BsonElement("rating")] public double Rating { get; set; } = 1500;
    [BsonElement("deviation")] public double Deviation { get; set; } = 200;
    [BsonElement("volatility")] public double Volatility { get; set; } = .06;
    [BsonElement("ratedGames")] public int RatedGames { get; set; }
    [BsonElement("peak")] public double Peak { get; set; } = 1500;
    [BsonElement("lastRatedAt")] public DateTime? LastRatedAt { get; set; }
}
