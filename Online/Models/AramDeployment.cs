namespace ChessGame.Api.Online;

public sealed class AramDeployment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Team { get; set; } = "";
    public string Kind { get; set; } = "Pawn";
    public string? Center { get; set; }
}
