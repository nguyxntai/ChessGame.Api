namespace ChessGame.Api.Online;

public sealed class MatchPlayer
{
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Color { get; set; } = "";
    public int Rating { get; set; }
    public bool Ready { get; set; }
    public bool Accepted { get; set; }
    public bool Connected { get; set; }
    public DateTime? ReconnectDeadline { get; set; }
    public List<LoadoutSnapshot> Loadout { get; set; } = new();
}
