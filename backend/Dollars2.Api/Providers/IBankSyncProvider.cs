using Dollars2.Api.Models;

namespace Dollars2.Api.Providers;

public record SyncedTransaction(
    string ProviderTransactionId,
    DateTime Date,
    string Description,
    decimal Amount,
    bool IsPending);

public interface IBankSyncProvider
{
    Task<IEnumerable<SyncedTransaction>> FetchTransactionsAsync(Account account, DateTime? since, CancellationToken cancellationToken = default);
}
