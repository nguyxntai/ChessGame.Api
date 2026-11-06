# Kingdom Rush shop release

Three independent shop-exclusive LEGENDARY products use the exact Unity key `kingdom_rush`:

| Code | Type | Gold | Diamonds |
| --- | --- | ---: | ---: |
| CHESS_KINGDOM_RUSH | CHESS_PIECE_SKIN | 60,000 | 3,000 |
| BOARD_KINGDOM_RUSH | BOARD_SKIN | 36,000 | 1,800 |
| BACKGROUND_KINGDOM_RUSH | BACKGROUND_SKIN | 24,000 | 1,200 |

`shop-catalog.json` supplies one featured standalone full-price offer per product, with no discount. The complete twelve-model chess set covers Denas White and Vez'Nan Black. The board and 3D background are sold separately. Classic background is never a sale product.

Startup calls `KingdomRushCatalog.EnsureItemsAsync`. It prechecks code/ID/category/key conflicts, preserves IDs of existing matching records, and idempotently upserts exactly three catalog items. Stable new IDs end in `...000001`, `...000002`, `...000003` under prefix `6b0c4f010000000000`. It never changes wallets, player inventory, existing prices, ratings or banner pools. `shopExclusive` defaults false for old BSON documents. Gacha pool validation and readiness diagnostics reject any shop-exclusive item.

`EquippedSkins.backgroundSkinId` is nullable and defaults null for existing users. `CosmeticLoadoutPolicy.References` includes it with slot `BACKGROUND`, so online snapshots retain the current user's validated background. Unity uses its own player's background rather than copying the opponent's.

Authenticated `PUT /api/inventory/background` accepts `{ "itemId": "<owned ID>" }` or `{ "itemId": null }` to restore Classic. It checks active item, category, ownership and active account. No purchase is made by equipping. The regular equip endpoint also accepts owned background items. Custom/set equipment updates write only set/board/mode/piece fields, preserving background even during concurrent requests.

Validation on 2026-11-06: the local replica-set `Tools/ShopVerification` run passed 24 checks, including all three configured purchases, response retries, ownership rejection, independent equipment/reset, catalog idempotency and gacha exclusion. All database writes used isolated synthetic accounts; no production wallet or ownership was changed. .NET 8 regression suite: 360 pass; one existing `UserProfileTests.PatchThenGetReturnsUnitySnapshotWithoutChangingProtectedData` assertion expects legacy Elo1510 while response uses rating1500. The same failure was reproduced from clean HEAD before this release, outside the worktree. It is not changed here.

Unity content requires the paired client update in `D:/Game_Project/Chesss-but-Weird`. Old clients hide unknown catalog artwork instead of buying a missing skin. Local Unity verifies real built Addressables, models, grid, rails, camera, captures, promotion visuals, release/reload and locked/default versus paid background UI. No authenticated two-client match or production purchase is claimed by these fixtures.
