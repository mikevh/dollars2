namespace Dollars2.Api.Models;

/// <summary>
/// One page of an account's sync archive, newest run first.
/// </summary>
public class AccountSyncArchiveResponse
{
    public List<SyncArchiveRunResponse> Runs { get; set; } = new();

    /// <summary>
    /// Cursor for the next (older) page — pass it back as <c>before</c>. It is the <c>syncedAt</c> of the
    /// oldest run on this page and is treated as an *exclusive* upper bound, which is what makes paging
    /// resume with neither a gap nor a repeat. Null when there is nothing older.
    /// </summary>
    /// <remarks>
    /// The server computes this rather than handing back DynamoDB's raw <c>LastEvaluatedKey</c>: the key
    /// is a composite of attributes the client has no business knowing, and it would point at an *item*,
    /// which is exactly the granularity a run must never be split at.
    /// </remarks>
    public DateTime? NextBefore { get; set; }
}

/// <summary>
/// Everything one sync run archived for one account: a summary line plus every raw payload it wrote.
/// </summary>
public class SyncArchiveRunResponse
{
    /// <summary>Shared by every item the run archived, across all accounts in its connection group.</summary>
    public string SyncRunId { get; set; } = "";

    public DateTime SyncedAt { get; set; }

    /// <summary>The provider the payloads came from, e.g. <c>SimpleFIN</c> or <c>Plaid</c>.</summary>
    public string SourceType { get; set; } = "";

    public int TransactionCount { get; set; }

    public int RemovedCount { get; set; }

    public int ErrorCount { get; set; }

    /// <summary>Provider transactions the parser rejected — see <c>ProviderSyncResult.SkippedTransactionsJson</c>.</summary>
    public int SkippedCount { get; set; }

    /// <summary>
    /// The run's account-metadata payload, hoisted out of <see cref="Items"/> so the client can show it as
    /// a header without hunting for it. A hoist, not an exclusion: the item is still in
    /// <see cref="Items"/>, which stays a complete list of what the run archived. Null when the run
    /// archived none.
    /// </summary>
    public string? AccountMetadataJson { get; set; }

    /// <summary>
    /// Every item the run archived, ordered by sort key so the ordering is stable across calls.
    /// Never truncated — a partial archive that looks complete would be worse than a large response.
    /// </summary>
    public List<SyncArchiveItemResponse> Items { get; set; } = new();
}

/// <summary>
/// One archived item. Which fields are populated depends on <see cref="ItemType"/>.
/// </summary>
public class SyncArchiveItemResponse
{
    /// <summary>
    /// <c>Transaction</c>, <c>Removed</c>, <c>AccountMetadata</c>, <c>ProviderError</c> or
    /// <c>SkippedTransaction</c> — the constants on <c>SyncArchiveRepository</c>.
    /// </summary>
    public string ItemType { get; set; } = "";

    /// <summary>Set on <c>Transaction</c> and <c>Removed</c> items; null on the rest, which have no id.</summary>
    public string? ProviderTransactionId { get; set; }

    /// <summary>
    /// The provider's payload, verbatim. Deliberately a string: re-parsing and re-serializing it here
    /// would throw away the exact bytes that are the whole reason the archive exists. Clients
    /// pretty-print it themselves. Null on <c>Removed</c> items, whose entire payload is the id.
    /// </summary>
    public string? RawJson { get; set; }
}
