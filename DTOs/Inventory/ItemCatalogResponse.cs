namespace ChessGame.Api.DTOs.Inventory;

public class ItemCatalogResponse
{
    public string ItemId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Rarity { get; set; } = string.Empty;

    public string UnityAssetKey { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; }
}
