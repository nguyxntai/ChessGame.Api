using ChessGame.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChessGame.Api.Controllers;

[ApiController]
[Route("api/items")]
public class ItemsController : ControllerBase
{
    private readonly InventoryService _inventoryService;

    public ItemsController(InventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    /// <summary>
    /// GET /api/items: Lấy toàn bộ danh mục các skin/item đang active.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetItems()
    {
        var catalog = await _inventoryService.GetCatalogAsync();
        return Ok(catalog);
    }
}
