# Custom cosmetic loadout contract v1

`PUT /api/inventory/loadout` is authenticated and accepts:

```json
{
  "pieceMode": "CUSTOM",
  "chessSkinId": "<owned active whole-set ItemId>",
  "boardSkinId": "<owned active board ItemId>",
  "pieces": [
    { "slot": "WHITE_KNIGHT", "itemId": "<owned active matching piece or whole-set ItemId>" }
  ]
}
```

The strings in angle brackets document required inputs; they are not seed data. Up to twelve unique slots are accepted: WHITE/BLACK × KING, QUEEN, ROOK, BISHOP, KNIGHT, PAWN. Unspecified slots inherit the base set. `SET` ignores saved overrides for rendering but keeps them for a future switch. Board selection is independent.

Only exact raw types are supported: `CHESS_PIECE_SKIN`, `BOARD_SKIN`, and `CHESS_{slot}_SKIN`. A single-piece item may equip only its exact side/kind. An owned full set may supply its components without granting new items. Every item must be active and actually owned by the authenticated caller, including inactive-mode saved overrides. Duplicate/unknown slots, invalid mode/IDs, unowned/inactive items and cross-category selections fail before the user update. The write changes only `equipped` and `updatedAt`.

The response uses existing `EquipItemResponse`, with additive `equipped.loadoutVersion: 1`, `pieceMode` and `pieces`. `/api/users/me` supplies the same capability marker. Existing `/equip` supports whole sets/boards plus exact single-piece raw types. Equipping a whole set returns to SET mode; a piece selects its custom slot. Existing BSON without the new fields defaults to SET; no migration is required.

Online loadout snapshots append an optional `slot`, preserve ItemId/type/key and validate each selected item. Historical snapshots without `slot` remain readable. Catalog and gacha APIs continue to return server-owned ItemIds; there is no client-side reward choice.

No economic/catalog data is created. No RNG, pity, wallet, inventory grant, transaction/recovery or V1 receipt implementation is changed. The existing V1 raw type/key snapshot can represent individual pieces; no V2 persistence is enabled. `ItemPresentationPolicy.Current` remains `NO_APPROVED_MANIFEST`. Missing Unity artwork never fails or replaces an economic receipt.

Deployment of this endpoint and creation of a reviewed individual-piece catalog/banner are separate operations. Build/test evidence does not establish production deployment or cloud data readiness.

Run relevant tests with `dotnet test Tests/ChessGame.Api.Tests.csproj -c Release --filter FullyQualifiedName~CustomCosmetic` plus the existing C1/C2/Gacha suites. The inherited ELO assertion failure is unrelated and is not repaired by this feature.
