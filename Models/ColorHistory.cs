using MongoDB.Bson.Serialization.Attributes;
using ChessGame.Api.Online;

namespace ChessGame.Api.Models;

[BsonSerializer(typeof(ChaosDocumentSerializer<ColorHistories>))]
public sealed class ColorHistories
{
    [BsonElement("classic")] public ColorHistory Classic { get; set; } = new();
    [BsonElement("chaos")] public ColorHistory Chaos { get; set; } = new();
    public ColorHistory For(string mode) => ModeNormalization.Require(mode) == "Classic" ? Classic : Chaos;
}
public sealed class ColorHistory
{
    [BsonElement("whiteGames")] public int WhiteGames { get; set; }
    [BsonElement("blackGames")] public int BlackGames { get; set; }
    [BsonElement("lastColor")] public string? LastColor { get; set; }
    [BsonElement("run")] public int Run { get; set; }
    public ColorHistory Played(string color) => new()
    { WhiteGames = WhiteGames + (color == "White" ? 1 : 0), BlackGames = BlackGames + (color == "Black" ? 1 : 0),
        LastColor = color, Run = LastColor == color ? Run + 1 : 1 };
}
