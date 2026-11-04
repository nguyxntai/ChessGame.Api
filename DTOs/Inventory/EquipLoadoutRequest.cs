namespace ChessGame.Api.DTOs.Inventory;

public sealed class EquipLoadoutRequest
{
    public string PieceMode { get; set; } = "SET";
    public string ChessSkinId { get; set; } = "";
    public string BoardSkinId { get; set; } = "";
    public List<EquipPieceRequest> Pieces { get; set; } = new();
}
public sealed class EquipPieceRequest
{
    public string Slot { get; set; } = "";
    public string ItemId { get; set; } = "";
}
