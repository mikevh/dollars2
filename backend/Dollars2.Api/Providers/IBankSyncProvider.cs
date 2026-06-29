using Dollars2.Api.Models;

namespace Dollars2.Api.Providers;

public record SyncedTransaction(
    string ProviderTransactionId,
    DateTime Date,
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
public record ProviderSyncResult(
    IReadOnlyList<SyncedTransaction> Upserts,
    IReadOnlyList<string> RemovedProviderTransactionIds,
    string? UpdatedConnectionDetailsJson);

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

    Task<ProviderSyncResult> FetchTransactionsAsync(Account account, DateTime? since, CancellationToken cancellationToken = default);
}
