using ChessGame.Api.DTOs.User;

namespace ChessGame.Api.DTOs.Inventory;

public class EquipItemResponse
{
    public bool Success { get; set; } = true;

    public string Message { get; set; } = string.Empty;

    public EquippedSkinsResponse Equipped { get; set; } = new();
}
