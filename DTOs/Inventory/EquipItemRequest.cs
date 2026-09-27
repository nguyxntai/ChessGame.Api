using System.ComponentModel.DataAnnotations;

namespace ChessGame.Api.DTOs.Inventory;

public class EquipItemRequest
{
    [Required(ErrorMessage = "ItemId không được để trống.")]
    public string ItemId { get; set; } = string.Empty;
}
