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

public interface IBankSyncProvider
{
    /// <summary>
    /// Minimum time that must elapse after a successful sync before the scheduled
    /// service will sync this provider again for the same user.
    /// </summary>
    TimeSpan MinSyncInterval { get; }

    Task<IEnumerable<SyncedTransaction>> FetchTransactionsAsync(Account account, DateTime? since, CancellationToken cancellationToken = default);
}
