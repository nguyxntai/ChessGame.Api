using MongoDB.Bson.Serialization.Attributes;

namespace ChessGame.Api.Online;

public sealed class OnlineMatch
{
    [BsonId] public string Id { get; set; } = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
    public GameSettings Settings { get; set; } = new();
    public bool Rated { get; set; }
    public string Status { get; set; } = "AwaitingReady";
    public List<MatchPlayer> Players { get; set; } = new();
    public List<OnlinePiece> Board { get; set; } = new();
    public string Turn { get; set; } = "White";
    public string EnPassantTarget { get; set; } = "-";
    public int HalfMoveClock { get; set; }
    public int FullMoveNumber { get; set; } = 1;
    public long StateVersion { get; set; }
    public long EventSequence { get; set; }
    public double WhiteMilliseconds { get; set; }
    public double BlackMilliseconds { get; set; }
    public DateTime? ClockStartedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ReadyDeadline { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public DrawOffer? DrawOffer { get; set; }
    public OfficialResult? Result { get; set; }
    public AramState? Aram { get; set; }
    public Dictionary<string, int> Repetitions { get; set; } = new();
    public List<string> RematchRequests { get; set; } = new();
    public string? RematchId { get; set; }
}
