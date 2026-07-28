using Dollars2.Api.Configuration;
using Microsoft.Extensions.Configuration;

namespace Dollars2.Tests;

// Locks in the fix for the "0-day token" bug: AuthService used to read Jwt:ExpirationDays with a
// plain GetValue<int>, so a missing, blank, non-numeric or non-positive value all collapsed to 0 and
// login quietly handed the client a token that had already expired. Every one of those inputs must
// now stop the host at startup instead, and Jwt:RefreshExpirationDays must behave identically.
public class JwtSettingsTests
{
    private static IConfiguration BuildConfig(params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "a-secret-long-enough-for-hmac-sha256-signing",
            ["Jwt:Issuer"] = "TestIssuer",
            ["Jwt:Audience"] = "TestAudience",
            ["Jwt:ExpirationDays"] = "30",
            ["Jwt:RefreshExpirationDays"] = "60"
        };

        foreach (var (key, value) in overrides)
        {
            if (value is null)
            {
                values.Remove(key);
            }
            else
            {
                values[key] = value;
            }
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void Fully_populated_section_round_trips()
    {
        var settings = JwtSettings.FromConfiguration(BuildConfig());

        Assert.Equal("a-secret-long-enough-for-hmac-sha256-signing", settings.Secret);
        Assert.Equal("TestIssuer", settings.Issuer);
        Assert.Equal("TestAudience", settings.Audience);
        Assert.Equal(30, settings.ExpirationDays);
        Assert.Equal(60, settings.RefreshExpirationDays);
    }

    // Issuer and audience are the only Jwt keys that may legitimately be absent. A blank value has to
    // fall back too — an empty issuer would otherwise be baked into every token and validated against.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_or_blank_issuer_and_audience_fall_back_to_Dollars2(string? value)
    {
        var settings = JwtSettings.FromConfiguration(
            BuildConfig(("Jwt:Issuer", value), ("Jwt:Audience", value)));

        Assert.Equal("Dollars2", settings.Issuer);
        Assert.Equal("Dollars2", settings.Audience);
    }

    [Theory]
    [InlineData("Jwt:ExpirationDays")]
    [InlineData("Jwt:RefreshExpirationDays")]
    public void Missing_day_count_throws_naming_the_key(string key)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => JwtSettings.FromConfiguration(BuildConfig((key, null))));

        Assert.Contains(key, ex.Message);
    }

    [Theory]
    [InlineData("Jwt:ExpirationDays", "")]
    [InlineData("Jwt:ExpirationDays", "   ")]
    [InlineData("Jwt:ExpirationDays", "0")]
    [InlineData("Jwt:ExpirationDays", "-1")]
    [InlineData("Jwt:ExpirationDays", "thirty")]
    [InlineData("Jwt:ExpirationDays", "1.5")]
    [InlineData("Jwt:RefreshExpirationDays", "")]
    [InlineData("Jwt:RefreshExpirationDays", "   ")]
    [InlineData("Jwt:RefreshExpirationDays", "0")]
    [InlineData("Jwt:RefreshExpirationDays", "-1")]
    [InlineData("Jwt:RefreshExpirationDays", "thirty")]
    [InlineData("Jwt:RefreshExpirationDays", "1.5")]
    public void Unusable_day_count_throws_naming_the_key(string key, string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => JwtSettings.FromConfiguration(BuildConfig((key, value))));

        Assert.Contains(key, ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_or_blank_secret_throws(string? secret)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => JwtSettings.FromConfiguration(BuildConfig(("Jwt:Secret", secret))));

        Assert.Contains("Jwt:Secret", ex.Message);
    }

    // The point of the fix is that the app's own config file can never be the thing that trips the
    // validator — if a key is ever dropped from appsettings.json, this fails before a deployment does.
    [Fact]
    public void Shipped_appsettings_json_satisfies_the_validator()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ApiConfig", "appsettings.json");
        Assert.True(File.Exists(path), $"Expected the API's appsettings.json to be copied to {path}.");

        var config = new ConfigurationBuilder().AddJsonFile(path).Build();
        var settings = JwtSettings.FromConfiguration(config);

        Assert.True(settings.ExpirationDays > 0);
        Assert.True(settings.RefreshExpirationDays > 0);
        Assert.Equal("Dollars2", settings.Issuer);
        Assert.Equal("Dollars2", settings.Audience);
    }
}
