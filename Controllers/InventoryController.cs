using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ChessGame.Api.DTOs.Inventory;
using ChessGame.Api.DTOs.User;
using ChessGame.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;

namespace ChessGame.Api.Controllers;

[ApiController]
[Route("api/inventory")]
public class InventoryController : ControllerBase
{
    private readonly InventoryService _inventoryService;

    public InventoryController(InventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    /// <summary>
    /// GET /api/inventory: Lấy toàn bộ skin mà user hiện tại sở hữu từ player_items + thông tin từ items.
    /// </summary>
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetMyInventory()
    {
        if (!TryGetUserId(out var userId, out var errorResult))
        {
            return errorResult;
        }

        var inventory = await _inventoryService.GetUserInventoryAsync(userId);
        return Ok(inventory);
    }

    /// <summary>
    /// PUT /api/inventory/equip: Trang bị một vật phẩm (skin quân cờ / skin bàn cờ) cho user hiện tại.
    /// Backend kiểm tra user có sở hữu item rồi mới cho equip. Dựa vào item.type để xác định Chess Piece Skin hay Board Skin.
    /// </summary>
    [Authorize]
    [HttpPut("equip")]
    public async Task<IActionResult> EquipItem([FromBody] EquipItemRequest request)
    {
        if (!TryGetUserId(out var userId, out var errorResult))
        {
            return errorResult;
        }

        if (string.IsNullOrWhiteSpace(request.ItemId) || !ObjectId.TryParse(request.ItemId, out var itemId))
        {
            return BadRequest(new { message = "ItemId không hợp lệ." });
        }

        try
        {
            var equippedSkins = await _inventoryService.EquipItemAsync(userId, itemId);

            var response = new EquipItemResponse
            {
                Success = true,
                Message = "Trang bị vật phẩm thành công.",
                Equipped = EquippedSkinsResponse.From(equippedSkins)
            };

            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message switch
            {
                "ITEM_NOT_FOUND" => NotFound(new { message = "Không tìm thấy vật phẩm." }),
                "ITEM_NOT_OWNED" => BadRequest(new { message = "Bạn chưa sở hữu vật phẩm này." }),
                "ITEM_NOT_EQUIPPABLE" => BadRequest(new { message = "Loại vật phẩm này không thể trang bị." }),
                "USER_NOT_FOUND" => NotFound(new { message = "Không tìm thấy thông tin người dùng." }),
                _ => StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message })
            };
        }
    }

    [Authorize]
    [HttpPut("loadout")]
    public async Task<IActionResult> EquipLoadout([FromBody] EquipLoadoutRequest request)
    {
        if (!TryGetUserId(out var userId, out var error)) return error;
        try
        {
            var equipped = await _inventoryService.EquipLoadoutAsync(userId, request);
            return Ok(new EquipItemResponse { Success = true, Message = "Loadout saved.", Equipped = EquippedSkinsResponse.From(equipped) });
        }
        catch (InvalidOperationException ex) when (ex.Message is "INVALID_LOADOUT" or "ITEM_NOT_FOUND" or "ITEM_NOT_OWNED" or "ITEM_NOT_EQUIPPABLE" or "USER_NOT_FOUND")
        { return BadRequest(new { code = ex.Message, message = "Loadout could not be equipped. Refresh your inventory." }); }
    }

    [Authorize]
    [HttpPut("background")]
    public async Task<IActionResult> EquipBackground([FromBody] BackgroundEquipRequest request)
    {
        if (!TryGetUserId(out var userId, out var error)) return error;
        try
        {
            var equipped = await _inventoryService.EquipBackgroundAsync(userId, request.ItemId);
            return Ok(new EquipItemResponse { Success = true, Message = "Background saved.", Equipped = EquippedSkinsResponse.From(equipped) });
        }
        catch (InvalidOperationException ex) when (ex.Message is "ITEM_NOT_FOUND" or "ITEM_NOT_OWNED" or "ITEM_NOT_EQUIPPABLE" or "USER_NOT_FOUND")
        { return BadRequest(new { code = ex.Message, message = "Background could not be equipped. Refresh your inventory." }); }
    }

    private bool TryGetUserId(out ObjectId userId, out IActionResult errorResult)
    {
        userId = ObjectId.Empty;
        errorResult = Unauthorized();

        string? userIdStr = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userIdStr))
        {
            errorResult = Unauthorized(new { message = "Token không chứa User ID." });
            return false;
        }

        if (!ObjectId.TryParse(userIdStr, out userId))
        {
            errorResult = Unauthorized(new { message = "User ID trong token không hợp lệ." });
            return false;
        }

        return true;
    }
}
