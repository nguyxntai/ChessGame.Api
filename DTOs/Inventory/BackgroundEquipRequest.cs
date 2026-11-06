namespace ChessGame.Api.DTOs.Inventory;

// Null/empty explicitly restores the original, non-sale game background.
public sealed class BackgroundEquipRequest
{
    public string? ItemId { get; set; }
}
