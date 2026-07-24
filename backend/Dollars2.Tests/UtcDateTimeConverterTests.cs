using System.Text.Json;
using Dollars2.Api.Json;
using Dollars2.Api.Models;

namespace Dollars2.Tests;

/// <summary>
/// Covers issue #68: every instant the API returns must carry a UTC marker, because a browser parses
/// an unmarked date-time string as *local* time. Calendar dates must stay unmarked, so they are typed
/// <see cref="DateOnly"/> and never reach the converter.
/// </summary>
public class UtcDateTimeConverterTests
{
    private static readonly JsonSerializerOptions Options = Dollars2JsonOptions.CreateWebOptions();

    private sealed class Holder
    {
        public DateTime Value { get; set; }
    }

    private sealed class NullableHolder
    {
        public DateTime? Value { get; set; }
    }

    [Fact]
    public void Unspecified_kind_is_written_as_utc()
    {
        // The shape Dapper hands back from a DATETIME2 column: right value, lost kind.
        var value = new DateTime(2026, 7, 20, 8, 0, 0, DateTimeKind.Unspecified);

        var json = JsonSerializer.Serialize(new Holder { Value = value }, Options);

        Assert.Equal("""{"value":"2026-07-20T08:00:00Z"}""", json);
    }

    [Fact]
    public void Utc_kind_passes_through_unchanged()
    {
        var value = new DateTime(2026, 7, 20, 8, 0, 0, DateTimeKind.Utc);

        var json = JsonSerializer.Serialize(new Holder { Value = value }, Options);

        Assert.Equal("""{"value":"2026-07-20T08:00:00Z"}""", json);
    }

    [Fact]
    public void Local_kind_is_converted_to_the_same_instant_in_utc()
    {
        var local = new DateTime(2026, 7, 20, 8, 0, 0, DateTimeKind.Local);

        var json = JsonSerializer.Serialize(new Holder { Value = local }, Options);

        var written = JsonSerializer.Deserialize<Holder>(json, Options)!.Value;
        Assert.Equal(local.ToUniversalTime(), written);
    }

    [Fact]
    public void Nullable_datetime_uses_the_converter_and_keeps_null()
    {
        // System.Text.Json applies a registered DateTime converter to DateTime? automatically —
        // this is what covers LastSyncedAt, the field the issue was reported against.
        var value = new DateTime(2026, 7, 20, 8, 0, 0, DateTimeKind.Unspecified);

        Assert.Equal(
            """{"value":"2026-07-20T08:00:00Z"}""",
            JsonSerializer.Serialize(new NullableHolder { Value = value }, Options));
        Assert.Equal(
            """{"value":null}""",
            JsonSerializer.Serialize(new NullableHolder { Value = null }, Options));
    }

    [Fact]
    public void Round_trips_to_the_same_instant()
    {
        var value = new DateTime(2026, 7, 20, 8, 30, 15, DateTimeKind.Unspecified);

        var json = JsonSerializer.Serialize(new Holder { Value = value }, Options);
        var restored = JsonSerializer.Deserialize<Holder>(json, Options)!.Value;

        Assert.Equal(DateTimeKind.Utc, restored.Kind);
        Assert.Equal(DateTime.SpecifyKind(value, DateTimeKind.Utc), restored);
    }

    [Fact]
    public void Transaction_date_stays_a_bare_calendar_date()
    {
        // The carve-out: a transaction date has no instant to convert. Stamping it with Z would shift
        // it to the previous day for every user west of UTC.
        var response = new TransactionResponse
        {
            Id = 1,
            Date = new DateOnly(2026, 7, 22),
            Description = "Coffee",
            Amount = -4.50m,
        };

        var json = JsonSerializer.Serialize(response, Options);

        using var document = JsonDocument.Parse(json);
        Assert.Equal("2026-07-22", document.RootElement.GetProperty("date").GetString());
    }
}
