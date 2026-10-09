using MongoDB.Driver;

namespace ChessGame.Api.Online;

public sealed partial class OnlineService
{
    public async Task<Page<RoomListingSnapshot>> ListRooms(string userId, int page, int pageSize,
        string? mode, string? region, CancellationToken ct)
    {
        lease.RequireOwner();
        if (page < 1 || page > 10000 || pageSize < 1 || pageSize > 50)
            throw new OnlineException("InvalidPagination", 400);
        string? selectedMode = string.IsNullOrWhiteSpace(mode) ? null : mode.Trim().ToUpperInvariant() switch
        { "CLASSIC" => "Classic", "ARAM" => "Aram", _ => throw new OnlineException("InvalidGameMode", 400) };
        string? selectedRegion = string.IsNullOrWhiteSpace(region) ? null : region.Trim().ToUpperInvariant();
        if (selectedRegion is { Length: > 16 }) throw new OnlineException("InvalidRegion", 400);
        return await store.Transaction(async (s, token) =>
        {
            await UserAsync(s, userId, token);
            var filter = Builders<OnlineRoom>.Filter.Eq(r => r.Status, "Open") &
                Builders<OnlineRoom>.Filter.Gt(r => r.ExpiresAt, DateTime.UtcNow);
            if (selectedMode is not null) filter &= Builders<OnlineRoom>.Filter.Eq(r => r.Settings.Mode, selectedMode);
            if (selectedRegion is not null) filter &= Builders<OnlineRoom>.Filter.Eq(r => r.Settings.Region, selectedRegion);
            long total = await store.Rooms.CountDocumentsAsync(s, filter, cancellationToken: token);
            var rooms = await store.Rooms.Find(s, filter).SortByDescending(r => r.ExpiresAt).ThenBy(r => r.Id)
                .Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync(token);
            return new Page<RoomListingSnapshot>(page, pageSize, total,
                rooms.Select(r => new RoomListingSnapshot(r.Id, r.Code, r.Settings, r.Members.Count, 2, r.ExpiresAt)).ToList());
        }, ct);
    }
}