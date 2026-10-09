namespace ChessGame.Api.Online;

public sealed record PlayerSnapshot(string UserId, string Username, string DisplayName, string Color,
    int Rating, bool Ready, bool Connected, DateTime? ReconnectDeadline, List<LoadoutSnapshot> Loadout, bool Accepted = false);
