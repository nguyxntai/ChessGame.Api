using System.Text.Json.Serialization;

namespace ChessGame.Api.Online;

// Completion resolves persisted server state. No client-supplied result is accepted.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompleteMatchRequest;
