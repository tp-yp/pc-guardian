using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Guardian.Core.Storage;

/// <summary>Reads "HH:mm" or "HH:mm:ss"; writes "HH:mm".</summary>
public sealed class HhMmTimeOnlyConverter : JsonConverter<TimeOnly>
{
    private static readonly string[] Formats = ["H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss"];

    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        TimeOnly.TryParseExact(reader.GetString(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t : throw new JsonException($"Bad time '{reader.GetString()}', expected HH:mm.");

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("HH:mm", CultureInfo.InvariantCulture));
}
