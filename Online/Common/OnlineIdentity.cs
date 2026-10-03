using System.Security.Claims;
using MongoDB.Bson;

namespace ChessGame.Api.Online;

public static class OnlineIdentity
{
    public static string UserId(ClaimsPrincipal? user)
    {
        var id = user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? user?.FindFirstValue("sub");
        if (!ObjectId.TryParse(id, out _)) throw new OnlineException("Unauthorized", 401);
        return id!;
    }
    public static string Id(string? id)
    {
        if (!ObjectId.TryParse(id, out _)) throw new OnlineException("InvalidId", 400);
        return id!;
    }
}
