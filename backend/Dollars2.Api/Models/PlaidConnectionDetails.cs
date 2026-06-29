namespace Dollars2.Api.Models;

public class PlaidConnectionDetails
{
    /// <summary>Per-Item access token issued by Plaid Link.</summary>
    public string AccessToken { get; set; } = "";

    /// <summary>
    /// Optional Plaid account_id to filter to. A single Item/access token can expose
    /// multiple accounts; if set, only transactions for this account are imported.
    /// If null, all accounts in the Item are imported.
    /// </summary>
    public string? AccountId { get; set; }

    /// <summary>Plaid /transactions/sync cursor, advanced after each successful sync.</summary>
    public string? Cursor { get; set; }
}
