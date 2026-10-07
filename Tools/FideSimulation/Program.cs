using System.Text.Json;
using System.Text.Json.Serialization;
using ChessGame.Api.Services.Ratings;

if (args.Length != 1) { Console.Error.WriteLine("Usage: FideSimulation <verified-period.json> (report is written to stdout; no database writes)"); return 2; }
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
try
{
    var input = JsonSerializer.Deserialize<FideSimulationInput>(await File.ReadAllTextAsync(args[0]), options)
        ?? throw new ArgumentException("Input cannot be null.");
    if (input.Players is null || input.Games is null) throw new ArgumentException("Players and games are required.");
    Console.WriteLine(JsonSerializer.Serialize(ChessGame.Api.Services.Ratings.FideSimulation.Run(input), options)); return 0;
}
catch (Exception ex) when (ex is JsonException or ArgumentException or IOException or OverflowException)
{ Console.Error.WriteLine(ex.Message); return 1; }
