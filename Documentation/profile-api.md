# Profile API (Hải)

Both endpoints require `Authorization: Bearer <accessToken>`. Identity comes only
from the JWT `sub`/name identifier; clients cannot select another user.

## GET /api/users/me

Returns the existing Unity `UserMeResponse`: `userId`, `username`, `email`,
`profile`, `wallet`, `stats`, `equipped`, and `createdAt`. No password hash,
normalized login fields, tokens or internal account flags are exposed.

## PATCH /api/users/me/profile

Send a JSON object containing at least one of these fields:

```json
{
  "displayName": "TaiChess",
  "avatarId": "avatar_01"
}
```

- `displayName`: a nonblank string, trimmed before storing, 1–18 characters
  after trimming. Control characters (including newlines) are rejected.
- `avatarId`: `avatar_00` through `avatar_05`, or `null` to restore the default.
- Omitted fields keep their current values. `displayName: null` is invalid.
- Unknown fields are rejected with HTTP 400, including `elo`, `wallet`,
  `stats`, `inventory`, `equipped`, `username`, `email` and `userId`. Sending a
  nested `profile` object is also invalid; the request fields are at the root.

HTTP 200 returns a complete updated `UserMeResponse`, with the same shape as
GET. A single MongoDB update sets only the supplied `profile.displayName`,
`profile.avatarId`, and the server-generated `updatedAt`. It filters by both
the authenticated user ID and `isActive: true`; it does not replace the user
document or write wallet, stats, equipped items or inventory.

| Status | Meaning |
| --- | --- |
| 200 | Profile read/updated successfully |
| 400 | Invalid JSON, invalid values, empty patch or unknown fields (PATCH) |
| 401 | Missing/invalid/expired token or missing/invalid user ID claim |
| 403 | Account is disabled |
| 404 | Authenticated account no longer exists |

## Alignment with the Unity project

The implementation was checked against `Assets/Scripts/User/UserMeResponse.cs`,
`Assets/Scripts/User/UserService.cs`, `Assets/Scripts/Network/ApiClient.cs`,
`Assets/Scripts/Chess/Gameplay/UI/PlayerProfileMenuController.cs`, and
`Assets/Scripts/Chess/Profile/PlayerProfileStore.cs` in `Chesss-but-Weird`.

- Unity already calls GET and reads the nested `profile`, `wallet`, `stats`,
  and `equipped` objects. The response shape is preserved.
- `PlayerProfileStore.CleanDisplayName` limits names to 18 characters.
- Unity clamps avatar indexes to 0–5 and contains `Avatar0.png` through
  `Avatar5.png`. This API defines `avatar_00` → `Avatar0.png`, …,
  `avatar_05` → `Avatar5.png`; null uses `Avatar0.png`.
- `ApiClient` already has `PatchAsync<T>`, including token refresh/retry.
  A future Unity edit flow can call
  `client.PatchAsync<UserMeResponse>("/api/users/me/profile", request, true)`
  and pass the result to `PlayerAuthService.ApplyApiUser`.
- The current Unity `UserService` only exposes GET and the profile screen
  currently loads `Avatar0.png` unconditionally. This backend PR does not add
  an editor UI or change Unity avatar rendering.

## Verification

```sh
dotnet build ChessGame.Api.csproj
dotnet test Tests/ChessGame.Api.Tests.csproj
```

Tests exercise the real controllers, JSON binding/validation, JWT bearer
authentication and `UserService` through an ASP.NET Core TestServer. Only the
MongoDB boundary is mocked: tests inspect the generated filter and `$set`,
apply it to a local document and verify PATCH → GET, partial updates, all six
avatars, reset to default, protected fields, identity isolation and
deleted/disabled accounts (including changes between read and write).
They do not connect to a live MongoDB instance or run the Unity editor.
