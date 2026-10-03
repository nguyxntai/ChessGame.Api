using System.Text.Json;
using System.Text.Json.Nodes;

namespace ChessGame.Api.Online;

public static class OnlineJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(Write(value), Options)!;
    public static object? EventPayload(OnlineEvent e, string userId)
    {
        var node = JsonNode.Parse(e.PayloadJson);
        Redact(node, userId);
        return node;
    }
    private static void Redact(JsonNode? node, string userId)
    {
        if (node is JsonObject obj)
        {
            if (obj["players"] is JsonArray players && obj["aram"] is JsonObject aram && aram["sides"] is JsonArray sides)
            {
                var color = players.OfType<JsonObject>().FirstOrDefault(p => (string?)p["userId"] == userId)?["color"]?.GetValue<string>();
                foreach (var side in sides.OfType<JsonObject>())
                    if ((string?)side["team"] != color) side["draftOptions"] = new JsonArray();
            }
            foreach (var property in obj.ToArray()) Redact(property.Value, userId);
        }
        else if (node is JsonArray arr) foreach (var item in arr) Redact(item, userId);
    }
}
