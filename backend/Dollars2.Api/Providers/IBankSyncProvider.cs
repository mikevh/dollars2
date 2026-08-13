using Dollars2.Api.Models;

namespace Dollars2.Api.Providers;

public record SyncedTransaction(
    string ProviderTransactionId,
    DateOnly Date,
    string Description,
    string Payee,
    string Memo,
    decimal Amount,
    bool IsPending)
{
    // Clamped here rather than in each provider so that for these three free-text fields no provider —
    // and neither the create- nor the update-from-sync repository path — can route unbounded bank text at
    // the nvarchar(500) columns. ProviderTransactionId is deliberately not clamped: it is the dedup key
    // behind UX_Transactions_Provider, so truncating could collide two distinct transactions. Providers
    // skip an over-length id instead, before it ever reaches this record.
    public string Description { get; init; } = TransactionText.Truncate(Description);

    public string Payee { get; init; } = TransactionText.Truncate(Payee);

    public string Memo { get; init; } = TransactionText.Truncate(Memo);
}

/// <summary>
/// The result of fetching from a provider.
/// </summary>
/// <param name="Upserts">Transactions to create or update (matched by ProviderTransactionId).</param>
/// <param name="RemovedProviderTransactionIds">Provider transaction ids the provider reports as removed; soft-deleted on our side.</param>
/// <param name="UpdatedConnectionDetailsJson">
/// If non-null, the provider's new connection state to persist on the account (e.g. a Plaid sync cursor).
/// Providers that are stateless (e.g. SimpleFIN) return null.
/// </param>
/// <param name="Error">
/// If non-null, this account could not be synced (e.g. misconfigured connection details) even though
/// the shared upstream call succeeded. The account is recorded as a failure with this message instead
/// of being persisted, so a broken account never masquerades as a healthy empty sync. Null on success.
/// </param>
/// <param name="Balance">
/// The account's current balance as reported by the provider, or null if the provider did not report a
/// parseable balance. When non-null it is appended to the AccountBalances history on a successful sync.
/// </param>
public record ProviderSyncResult(
    IReadOnlyList<SyncedTransaction> Upserts,
    IReadOnlyList<string> RemovedProviderTransactionIds,
    string? UpdatedConnectionDetailsJson,
    string? Error = null,
    decimal? Balance = null);

/// <summary>
/// The result of one connection-level fetch: the per-account sync results, plus the raw HTTP response
/// body/bodies the fetch produced for the sync archive.
/// </summary>
/// <param name="Results">Per-account sync results, keyed by <see cref="Account.Id"/>.</param>
/// <param name="RawResponseBodies">
/// The verbatim body of each upstream HTTP response this fetch made, in the order captured — one for a
/// single-response provider (SimpleFIN), one per page for a paginated provider (Plaid).
/// </param>
public record ProviderFetchResult(
    IReadOnlyDictionary<int, ProviderSyncResult> Results,
    IReadOnlyList<string> RawResponseBodies);

public interface IBankSyncProvider
{
    /// <summary>
    /// The Account.SourceType value this provider handles (e.g. "SimpleFIN", "Plaid").
    /// </summary>
    string SourceType { get; }

    /// <summary>
    /// Whether this provider is enabled in configuration.
    /// </summary>
    bool Enabled { get; }

    /// <summary>
    /// Minimum time that must elapse after a successful sync before the scheduled
    /// service will sync this provider again for the same user.
    /// </summary>
    TimeSpan MinSyncInterval { get; }

    /// <summary>
    /// A stable key identifying the upstream connection that a single provider API call covers
    /// (e.g. a Plaid Item access token, or a SimpleFIN access URL). Stored accounts that share a
    /// key are fetched together in one call and the results distributed among them, rather than
    /// making one redundant call per account.
    /// </summary>
    string GetConnectionKey(Account account);

    /// <summary>
    /// Fetch transactions for a set of stored accounts that share one connection (as identified by
    /// <see cref="GetConnectionKey"/>) using a single upstream call, returning a per-account result
    /// keyed by <see cref="Account.Id"/>. Every account in <paramref name="accounts"/> is present in
    /// the returned dictionary — with an empty result if it has no upstream activity.
    /// </summary>
    /// <param name="since">
    /// The earliest point to fetch from, covering all accounts in the group. Providers that track
    /// their own position (e.g. Plaid's sync cursor) may ignore it.
    /// </param>
    /// <param name="fullResync">
    /// When true, this is a user-initiated full resync over an explicit lookback window: providers
    /// that track their own position must ignore it and re-fetch from scratch (e.g. Plaid starts from
    /// a null cursor). Providers that already fetch purely by <paramref name="since"/> (e.g. SimpleFIN)
    /// are unaffected. ProviderTransactionId dedup absorbs the re-fetch.
    /// </param>
    Task<ProviderFetchResult> FetchTransactionsForConnectionAsync(
        IReadOnlyList<Account> accounts,
        DateTime? since,
        bool fullResync = false,
        CancellationToken cancellationToken = default);
}
