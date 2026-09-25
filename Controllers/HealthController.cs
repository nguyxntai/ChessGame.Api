using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ChessGame.Api.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    private readonly IMongoDatabase _database;

    public HealthController(IMongoDatabase database)
    {
        _database = database;
    }

    // Check riêng Backend
    // GET /api/health
    [HttpGet]
    public IActionResult CheckHealth()
    {
        return Ok(new
        {
            status = "ok",
            service = "ChessGame.Api",
            environment =
                Environment.GetEnvironmentVariable(
                    "ASPNETCORE_ENVIRONMENT"
                ) ?? "Unknown",
            checkedAt = DateTime.UtcNow
        });
    }

    // Check Backend + MongoDB
    // GET /api/health/database
    [HttpGet("database")]
    public async Task<IActionResult> CheckDatabase()
    {
        try
        {
            // Ping MongoDB
            await _database.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1)
            );

            // Lấy collection items
            var items =
                _database.GetCollection<BsonDocument>("items");

            // Đếm số item
            long itemCount =
                await items.CountDocumentsAsync(
                    FilterDefinition<BsonDocument>.Empty
                );

            return Ok(new
            {
                status = "ok",
                database = "connected",
                databaseName =
                    _database.DatabaseNamespace.DatabaseName,
                items = itemCount,
                checkedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    status = "error",
                    database = "disconnected",
                    message = ex.Message,
                    checkedAt = DateTime.UtcNow
                }
            );
        }
    }
}