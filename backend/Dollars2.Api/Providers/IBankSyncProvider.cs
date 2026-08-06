using Dollars2.Api.Models;

namespace Dollars2.Api.Providers;

/// <param name="RawJson">
/// The provider's transaction object, verbatim. Deliberately exempt from the
/// <see cref="TransactionText.Truncate"/> clamping applied below: that clamp exists because
/// Description/Payee/Memo map to nvarchar(500) columns, and this field is never persisted to MSSQL —
/// it feeds the sync archive, whose entire purpose is keeping what the bank actually sent.
/// </param>
public record SyncedTransaction(
    string ProviderTransactionId,
    DateOnly Date,
    string Description,
    string Payee,
    string Memo,
    decimal Amount,
    bool IsPending,
    string RawJson)
{
    // Clamped here rather than in each provider so that for these three free-text fields no provider —
    // and neither the create- nor the update-from-sync repository path — can route unbounded bank text at
    // the nvarchar(500) columns. ProviderTransactionId is deliberately not clamped: it is the dedup key
    // behind UX_Transactions_Provider, so truncating could collide two distinct transactions. Bounding it
    // means skipping the transaction instead, which is issue #154.
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
/// <param name="AccountMetadataJson">
/// The provider's account object, verbatim — balance, available balance, currency, org, name. Null when
/// the provider reported no matching account. Providers may drop a nested transactions array from it so
/// the archive does not store every transaction twice; every retained property is copied unchanged.
/// </param>
/// <param name="ErrorsJson">
/// Provider-reported errors, verbatim, one raw JSON object per entry. Distinct from <paramref name="Error"/>:
/// that is our own single message explaining why *this* account could not be synced, while these are
/// whatever the upstream response carried, often about the connection as a whole rather than one account.
/// Empty when the provider reported none.
/// </param>
/// <param name="SkippedTransactionsJson">
/// Raw text of provider transactions that could not be mapped into <paramref name="Upserts"/> — today,
/// ones whose amount would not parse. They are deliberately still surfaced: a transaction our parser
/// rejected is exactly what the archive's forensics exist for, and it has nowhere else to go, since a
/// SyncedTransaction cannot represent an unparseable amount without inventing one.
/// </param>
public record ProviderSyncResult(
    IReadOnlyList<SyncedTransaction> Upserts,
    IReadOnlyList<string> RemovedProviderTransactionIds,
    string? UpdatedConnectionDetailsJson,
    string? Error = null,
    decimal? Balance = null,
    string? AccountMetadataJson = null,
    IReadOnlyList<string>? ErrorsJson = null,
    IReadOnlyList<string>? SkippedTransactionsJson = null)
{
    // Coalesced here rather than defaulted in the parameter list: a positional record's default has to
    // be a compile-time constant, so Array.Empty<string>() is not available there. Callers that have
    // nothing to report can omit these entirely and consumers still never see a null list.
    public IReadOnlyList<string> ErrorsJson { get; init; } = ErrorsJson ?? Array.Empty<string>();

    public IReadOnlyList<string> SkippedTransactionsJson { get; init; } = SkippedTransactionsJson ?? Array.Empty<string>();
}

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
    Task<IReadOnlyDictionary<int, ProviderSyncResult>> FetchTransactionsForConnectionAsync(
        IReadOnlyList<Account> accounts,
        DateTime? since,
        bool fullResync = false,
        CancellationToken cancellationToken = default);
}
