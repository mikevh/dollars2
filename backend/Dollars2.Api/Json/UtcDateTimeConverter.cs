using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dollars2.Api.Json;

/// <summary>
/// Serializes every <see cref="DateTime"/> the API returns with an explicit UTC marker.
///
/// Instants are stored in DATETIME2 columns, which carry no offset, so Dapper materializes them
/// with <see cref="DateTimeKind.Unspecified"/> — the value is UTC but .NET no longer knows it.
/// System.Text.Json would then write it with no marker, and per the ECMAScript spec a date-time
/// string without an offset is parsed by the browser as *local* time, shifting every instant by
/// the viewer's UTC offset.
///
/// Since every DateTime written to the database is UTC (SYSUTCDATETIME() / DateTime.UtcNow),
/// treating Unspecified as UTC restores the information the column dropped.
///
/// Calendar dates are deliberately not covered: they are typed <see cref="DateOnly"/>, which this
/// converter never sees. That is what keeps a transaction date from being shifted a day backwards
/// for users west of UTC.
/// </summary>
public class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return AsUtc(reader.GetDateTime());
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(AsUtc(value));
    }

    /// <summary>
    /// Reinterprets an unmarked value as UTC rather than as server-local, so neither direction
    /// depends on the machine's time zone.
    /// </summary>
    private static DateTime AsUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
