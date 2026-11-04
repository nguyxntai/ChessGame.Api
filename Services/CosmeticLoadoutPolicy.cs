using ChessGame.Api.Models;
using MongoDB.Bson;

namespace ChessGame.Api.Services;

// Exact protocol tokens. No catalog registration, artwork approval or economic grant.
public static class CosmeticLoadoutPolicy
{
    public const string SetType = "CHESS_PIECE_SKIN", BoardType = "BOARD_SKIN";
    public static readonly IReadOnlyList<string> Slots = Array.AsReadOnly(new[] {
        "WHITE_PAWN", "WHITE_ROOK", "WHITE_KNIGHT", "WHITE_BISHOP", "WHITE_QUEEN", "WHITE_KING",
        "BLACK_PAWN", "BLACK_ROOK", "BLACK_KNIGHT", "BLACK_BISHOP", "BLACK_QUEEN", "BLACK_KING" });
    public static bool IsSlot(string? slot) => slot != null && Slots.Contains(slot, StringComparer.Ordinal);
    public static string? SlotForType(string? type) => Slots.FirstOrDefault(s => type == "CHESS_" + s + "_SKIN");
    public static bool Fits(string? type, string slot) => slot switch {
        "SET" => type == SetType, "BOARD" => type == BoardType,
        _ => IsSlot(slot) && (type == SetType || SlotForType(type) == slot)
    };

    public static IEnumerable<(ObjectId Id, string Slot)> References(EquippedSkins e)
    {
        if (e.ChessSkinId is { } set) yield return (set, "SET");
        if (e.BoardSkinId is { } board) yield return (board, "BOARD");
        if (e.PieceMode == "CUSTOM")
            foreach (var piece in e.Pieces ?? new()) yield return (piece.ItemId, piece.Slot);
    }

    public static EquippedSkins Validate(string mode, ObjectId setId, ObjectId boardId,
        IReadOnlyList<EquippedPiece> pieces, IReadOnlyDictionary<ObjectId, Item> items, ISet<ObjectId> owned)
    {
        if (mode != "SET" && mode != "CUSTOM" || pieces.Count > 12 ||
            pieces.Any(p => !IsSlot(p.Slot)) || pieces.Select(p => p.Slot).Distinct(StringComparer.Ordinal).Count() != pieces.Count)
            throw new InvalidOperationException("INVALID_LOADOUT");
        var result = new EquippedSkins { ChessSkinId = setId, BoardSkinId = boardId, PieceMode = mode,
            Pieces = pieces.Select(p => new EquippedPiece { Slot = p.Slot, ItemId = p.ItemId }).ToList() };
        // Validate inactive overrides too, so switching modes never revives unowned data.
        foreach (var (id, slot) in new[] { (setId, "SET"), (boardId, "BOARD") }.Concat(pieces.Select(p => (p.ItemId, p.Slot))))
        {
            if (!items.TryGetValue(id, out var item) || !item.IsActive) throw new InvalidOperationException("ITEM_NOT_FOUND");
            if (!owned.Contains(id)) throw new InvalidOperationException("ITEM_NOT_OWNED");
            if (!Fits(item.Type, slot)) throw new InvalidOperationException("ITEM_NOT_EQUIPPABLE");
        }
        return result;
    }
}
