using System.Collections.Concurrent;

namespace ChessGame.Api.Online;

public sealed class LiveConnections
{
    public ConcurrentDictionary<string, string> Users { get; } = new();
}
