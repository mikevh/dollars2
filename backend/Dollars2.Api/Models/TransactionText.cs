namespace Dollars2.Api.Models;

/// <summary>
/// Transactions.Description, Payee and Memo are nvarchar(500) (migrations 007 and 014), and SQL Server
/// raises error 8152 rather than truncating when a longer value is written. The two inbound paths handle
/// that differently on purpose (issue #93): user-entered text is rejected at the request boundary so the
/// caller can shorten it, while provider-supplied text is truncated — a bank's overlong memo must not be
/// able to fail, and keep failing, an account's whole sync.
/// </summary>
public static class TransactionText
{
    public const int MaxLength = 500;

    /// <summary>
    /// Clamps provider-supplied text to <see cref="MaxLength"/>, leaving shorter values as-is. Null is
    /// normalized to empty: a provider DTO property is declared non-nullable but System.Text.Json still
    /// writes null into it for an explicit JSON null (e.g. SimpleFIN's <c>"memo": null</c>), and throwing
    /// here would fail the provider's whole fetch — and with it every account on that connection.
    /// </summary>
    public static string Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        if (value.Length <= MaxLength)
        {
            return value;
        }

        // Don't split a surrogate pair across the cut — half a pair is an unpaired code unit that
        // round-trips out of the column as a replacement glyph.
        var end = char.IsHighSurrogate(value[MaxLength - 1]) ? MaxLength - 1 : MaxLength;
        return value[..end];
    }
}
