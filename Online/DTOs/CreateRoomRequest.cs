using System.ComponentModel.DataAnnotations;

namespace ChessGame.Api.Online;

public sealed record CreateRoomRequest
{
    [Required, StringLength(80, MinimumLength = 1)] public string RequestId { get; init; } = "";
    [Required] public GameSettings Settings { get; init; } = new();
}
