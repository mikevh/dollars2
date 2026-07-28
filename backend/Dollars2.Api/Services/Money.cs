namespace Dollars2.Api.Services;

/// <summary>
/// Money lives in decimal(18,2) columns, so an amount carrying more precision than a cent is
/// silently rounded on write and what gets stored is not what the caller confirmed. Requests
/// carrying such an amount are rejected rather than rounded (issue #110), which keeps the
/// stored value identical to the entered one and keeps split assignments summing exactly.
/// </summary>
public static class Money
{
    public const string SubCentCode = "INVALID_AMOUNT_PRECISION";
    public const string SubCentMessage = "Amount cannot be more precise than one cent.";

    /// <summary>True when the amount survives a decimal(18,2) round-trip unchanged.</summary>
    public static bool IsWholeCents(decimal amount)
    {
        return amount == decimal.Round(amount, 2);
    }
}
