# Kingdom Rush faction collections

The public shop sells two independent Legendary, shop-exclusive collections with Unity keys `kingdom_rush_denas` and `kingdom_rush_veznan`.

| Product, per faction | Gold | Diamonds |
| --- | ---: | ---: |
| Chess set | 60,000 | 3,000 |
| Board | 36,000 | 1,800 |
| Background | 6,000 | 300 |
| Complete bundle (25% off 102,000 / 5,100) | **76,500** | **3,825** |

Offer IDs: `kingdom-rush-{denas,veznan}-{pieces,board,background,complete}`. Each complete bundle has exactly the three corresponding faction products. Owned items are excluded before discount using the existing purchase policy. No product enters gacha. The original `kingdom-rush-background` remains 6,000 Gold / 300 Diamonds, still Legendary.

`KingdomRushCatalog.EnsureItemsAsync` idempotently installs nine catalog records: six new faction items, one active legacy background and two inactive mixed items. Stable new IDs use prefix `6b0c4f0100000000000000`, suffixes 11/12/13 for Denas and 21/22/23 for Vez'Nan. Existing matching records retain their IDs. All remain shop-exclusive, including archived records. Complete conflict checks precede mutation.

The user explicitly authorized pre-release revocation of the original combined set and board without compensation. A startup MongoDB transaction archives original ownership documents in `kingdom_rush_retired_ownership`, removes only those ownership rows and clears the corresponding equipped chess/board IDs. It preserves background ownership/equipment, wallets, other inventory and purchase history. Archive IDs are unchanged, so reruns cannot duplicate them. The old catalog records are inactive; mixed offers and the original complete bundle are withdrawn. Unity retains their art/addressables for a future mode outside ordinary shop/inventory.

The three existing item categories, purchase receipts, independent background equipment and frozen online loadout references are reused. No new item type or purchase pipeline is introduced. Classic background remains a locked non-sale default, restored by null background equipment.

Verification on 2026-11-06: `Tools/ShopVerification` passed 41 tests on an isolated MongoDB replica set with synthetic accounts, including repeated retirement, archived ownership, unchanged balances/backgrounds, retired equip rejection, both currencies for both faction bundles, retries, partial ownership and gacha rejection for all seven active products. Related .NET tests passed 37/37. No real-account purchase was used for verification.

The paired Unity update is in `D:/Game_Project/Chesss-but-Weird`; see `Documentation/KingdomRushFactions.md`. Production ownership revocation is an intentional authorized deployment action, separate from synthetic verification.

Full .NET8 regression: 360 passed, excluding the previously reproduced baseline stale Elo assertion in UserProfileTests.PatchThenGetReturnsUnitySnapshotWithoutChangingProtectedData. No rating code was modified.
