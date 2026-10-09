using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ChessGame.Api.Online;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateMatchRequest
{
    [Required, StringLength(24, MinimumLength = 24)]
    public string RoomId { get; init; } = "";
}
