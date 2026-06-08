using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daeanne.Shared.Requests;

public class PostTaskResultRequest
{
    /// <summary>"succeeded", "partial", or "failed"</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Accepts either a JSON string or a JSON object — both are stored as a JSON string.
    /// Agents may POST resultJson as a raw object; we normalize it here.
    /// </summary>
    [JsonConverter(typeof(RawOrStringJsonConverter))]
    public string? ResultJson { get; set; }

    public string? Error { get; set; }
}

/// <summary>
/// Accepts a JSON value that is either already a string or a JSON object/array,
/// and always returns it as a JSON string (serialized if it was an object).
/// </summary>
public class RawOrStringJsonConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType == JsonTokenType.String) return reader.GetString();
        // It's an object or array — capture the raw JSON text and store that as the string value
        using var doc = JsonDocument.ParseValue(ref reader);
        return doc.RootElement.GetRawText();
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}
