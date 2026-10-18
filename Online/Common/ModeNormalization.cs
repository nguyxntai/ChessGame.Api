using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;

namespace ChessGame.Api.Online;

/// <summary>The only boundary that accepts the retired mode name. Writes always use Chaos.</summary>
public static class ModeNormalization
{
    public const string LegacyMode = "Aram";
    public const string LegacyKey = "aram";
    public static string Normalize(string? mode) => mode?.Trim().ToUpperInvariant() switch
    {
        "CLASSIC" => "Classic",
        "CHAOS" or "ARAM" => "Chaos",
        _ => mode?.Trim() ?? ""
    };
    public static string Require(string? mode, string error = "InvalidMode") => Normalize(mode) switch
    {
        "Classic" => "Classic", "Chaos" => "Chaos", _ => throw new OnlineException(error, 400)
    };
    public static string SettingsFingerprint(GameSettings settings, bool legacy = false)
    {
        var json = OnlineJson.Write(settings);
        if (legacy && settings.Mode == "Chaos")
            json = json.Replace("\"mode\":\"Chaos\"", "\"mode\":\"" + LegacyMode + "\"");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
    public static bool MatchesSettingsFingerprint(string? fingerprint, GameSettings settings) =>
        fingerprint == SettingsFingerprint(settings) || settings.Mode == "Chaos" && fingerprint == SettingsFingerprint(settings, legacy: true);

    // Structural compatibility for persisted event JSON; never rewrite arbitrary user strings or policy IDs.
    public static void NormalizePayload(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj.ContainsKey(LegacyKey))
            {
                if (obj.ContainsKey("chaos") && !JsonNode.DeepEquals(obj[LegacyKey], obj["chaos"]))
                    throw new InvalidOperationException("Conflicting legacy/Chaos payload; migration must resolve it.");
                if (!obj.ContainsKey("chaos")) obj["chaos"] = obj[LegacyKey]?.DeepClone();
                obj.Remove(LegacyKey);
            }
            foreach (var name in new[] { "mode", "ratingMode" })
                if (obj[name] is JsonValue value && value.TryGetValue<string>(out var mode) && Normalize(mode) == "Chaos")
                    obj[name] = "Chaos";
            foreach (var property in obj.ToArray()) NormalizePayload(property.Value);
        }
        else if (node is JsonArray array) foreach (var child in array) NormalizePayload(child);
    }
}

