using Dollars2.Api.Models;

namespace Dollars2.Api.Providers;

public record SyncedTransaction(
    string ProviderTransactionId,
    DateOnly Date,
    string Description,
    string Payee,
    string Memo,
    decimal Amount,
    bool IsPending);

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
    Task<IReadOnlyDictionary<int, ProviderSyncResult>> FetchTransactionsForConnectionAsync(
        IReadOnlyList<Account> accounts,
        DateTime? since,
        CancellationToken cancellationToken = default);
}
