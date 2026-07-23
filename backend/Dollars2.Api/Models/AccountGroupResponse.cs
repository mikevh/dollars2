namespace Dollars2.Api.Models;

/// <summary>
/// A set of accounts that sync through one shared connection (e.g. a single SimpleFIN access URL or
/// Plaid Item). Manual accounts, which have no connection, are returned as a single "Manual" group.
/// </summary>
public class AccountGroupResponse
{
    /// <summary>
    /// Stable, opaque identity for the connection. Derived from a one-way hash of the provider's
    /// connection key (which contains secrets and is never exposed); "manual" for the Manual group.
    /// </summary>
    public string ConnectionId { get; set; } = "";

    /// <summary>The provider source type shared by every account in the group ("Plaid", "SimpleFIN", "Manual").</summary>
    public string SourceType { get; set; } = "";

    public IReadOnlyList<AccountInfoResponse> Accounts { get; set; } = Array.Empty<AccountInfoResponse>();
}

public class AccountInfoResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>When this account was last synced (any status), or null if it has never synced / is manual.</summary>
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>Status of the last sync attempt ("Success" / "Failure"), or null if never synced / manual.</summary>
    public string? LastStatus { get; set; }
}
