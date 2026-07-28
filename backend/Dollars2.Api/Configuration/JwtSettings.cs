using System.Globalization;

namespace Dollars2.Api.Configuration;

/// <summary>
/// The validated <c>Jwt</c> configuration section. Built once at startup by
/// <see cref="FromConfiguration"/> so that a missing or nonsensical value fails the host loudly
/// instead of silently minting tokens that expire the instant they are issued.
/// </summary>
public sealed record JwtSettings(
    string Secret,
    string Issuer,
    string Audience,
    int ExpirationDays,
    int RefreshExpirationDays)
{
    private const string DefaultIssuer = "Dollars2";
    private const string DefaultAudience = "Dollars2";

    /// <summary>
    /// Reads and validates the <c>Jwt</c> section.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A required key is absent or blank, or a day count is not a positive integer.
    /// </exception>
    public static JwtSettings FromConfiguration(IConfiguration config)
    {
        var secret = config["Jwt:Secret"];

        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("Jwt:Secret is not configured.");
        }

        var issuer = config["Jwt:Issuer"];
        var audience = config["Jwt:Audience"];

        return new JwtSettings(
            secret,
            string.IsNullOrWhiteSpace(issuer) ? DefaultIssuer : issuer,
            string.IsNullOrWhiteSpace(audience) ? DefaultAudience : audience,
            ReadPositiveDays(config, "Jwt:ExpirationDays"),
            ReadPositiveDays(config, "Jwt:RefreshExpirationDays"));
    }

    private static int ReadPositiveDays(IConfiguration config, string key)
    {
        var raw = config[key];

        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException($"{key} is not configured.");
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days))
        {
            throw new InvalidOperationException($"{key} must be a whole number of days, but was '{raw}'.");
        }

        if (days <= 0)
        {
            throw new InvalidOperationException($"{key} must be greater than zero, but was {days}.");
        }

        return days;
    }
}
