using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinansApi.Infrastructure;
// Accept legacy ISO timestamps while preserving the calendar day written by the caller.
public sealed class BusinessDateConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (value is null || value.Length < 10
            || !DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            || (value.Length > 10
                && (value[10] != 'T' || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))))
        {
            throw new JsonException("Geçerli bir YYYY-MM-DD tarihi girin.");
        }

        return day.ToDateTime(TimeOnly.MinValue);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

}
