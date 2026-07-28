using Dollars2.Api.Services;

namespace Dollars2.Tests;

/// <summary>
/// <see cref="Money.IsWholeCents"/> is the gate that keeps sub-cent amounts out of the
/// decimal(18,2) columns, so what is stored is exactly what the caller sent (issue #110).
/// </summary>
public class MoneyTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("20")]
    [InlineData("10.9")]
    [InlineData("10.99")]
    [InlineData("-10.99")]
    [InlineData("-0.01")]
    [InlineData("1234567.05")]
    // Trailing zeros carry extra scale but no extra value, so they round-trip unchanged.
    [InlineData("10.9900")]
    public void Accepts_amounts_a_cent_column_can_hold(string amount)
    {
        Assert.True(Money.IsWholeCents(decimal.Parse(amount)));
    }

    [Theory]
    [InlineData("10.999")]
    [InlineData("0.005")]
    [InlineData("0.004")]
    [InlineData("-0.005")]
    [InlineData("-10.999")]
    [InlineData("100.0000000001")]
    public void Rejects_anything_finer_than_a_cent(string amount)
    {
        Assert.False(Money.IsWholeCents(decimal.Parse(amount)));
    }
}
