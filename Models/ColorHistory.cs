using MongoDB.Bson.Serialization.Attributes;

namespace ChessGame.Api.Models;

public sealed class ColorHistories
{
    [BsonElement("classic")] public ColorHistory Classic { get; set; } = new();
    [BsonElement("aram")] public ColorHistory Aram { get; set; } = new();
    public ColorHistory For(string mode) => mode == "Classic" ? Classic : Aram;
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
