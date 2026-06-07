using System.Text.Json;
using System.Text.Json.Serialization;

namespace PredictionLeague.Infrastructure.Football.Dtos;

// Generic API-Football envelope. Internal to the client — the typed client unwraps
// `response[]` and surfaces a result record to the Application abstraction.
//
// Tricky bit: `errors` is an ARRAY when present but an empty OBJECT `{}` when absent in
// some API-Sports responses. Deserialize as JsonElement and inspect defensively rather
// than assuming string[].
internal sealed class ApiEnvelope<T>
{
    [JsonPropertyName("errors")]
    public JsonElement Errors { get; set; }

    [JsonPropertyName("results")]
    public int Results { get; set; }

    [JsonPropertyName("response")]
    public List<T> Response { get; set; } = [];

    // Returns the error messages if the envelope signals a logical failure, else empty.
    public IReadOnlyList<string> GetErrorMessages()
    {
        switch (Errors.ValueKind)
        {
            case JsonValueKind.Array when Errors.GetArrayLength() > 0:
                return Errors.EnumerateArray().Select(e => e.ToString()).ToList();

            case JsonValueKind.Object:
                var messages = Errors.EnumerateObject().Select(p => $"{p.Name}: {p.Value}").ToList();
                return messages; // empty object {} → no messages → not an error

            default:
                return [];
        }
    }
}
