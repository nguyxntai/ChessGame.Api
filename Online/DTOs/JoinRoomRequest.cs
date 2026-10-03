using System.ComponentModel.DataAnnotations;

namespace ChessGame.Api.Online;

public sealed record JoinRoomRequest([property: Required, StringLength(12)] string Code);
