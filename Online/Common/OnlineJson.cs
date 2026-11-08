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
        ModeNormalization.NormalizePayload(node);
        Redact(node, userId);
        return node;
    }
    private static void Redact(JsonNode? node, string userId)
    {
        if (node is JsonObject obj)
        {
            if (obj["players"] is JsonArray players && obj["chaos"] is JsonObject chaos && chaos["sides"] is JsonArray sides)
            {
                var color = players.OfType<JsonObject>().FirstOrDefault(p => (string?)p["userId"] == userId)?["color"]?.GetValue<string>();
                bool disguise = (int?)chaos["rulesVersion"] == 3 && sides.OfType<JsonObject>().Any(side => (string?)side["team"] != color && (int?)side["buffId"] == 3);
                var state = chaos.Deserialize<ChaosState>(Options)!;
                ChaosPrivacy.Redact(state, color);
                obj["chaos"] = JsonSerializer.SerializeToNode(state, Options);
                // Ability payloads can reveal a disguised pair even when the snapshot is redacted.
                if (disguise && node.Parent is JsonObject envelope && envelope["command"] is JsonObject command &&
                    (string?)command["kind"] is "SelectTargets" or "ToggleSwap" or "KeepSwap" or "ReselectSwap") envelope["command"] = null;
            }
            foreach (var property in obj.ToArray()) Redact(property.Value, userId);
        }
        else if (node is JsonArray arr) foreach (var item in arr) Redact(item, userId);
    }
}
