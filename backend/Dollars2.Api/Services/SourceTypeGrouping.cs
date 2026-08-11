using Dollars2.Api.Models;
using Dollars2.Api.Providers;

namespace Dollars2.Api.Services;

/// <summary>
/// Shared "group syncable accounts by source type" logic for AccountService.BuildGroups and
/// BankSyncService.ResolveConnectionAccounts, so their casing/canonicalization rules can't drift
/// apart the way the underlying comparisons did before issue #99. Ordered by Account.Id first so the
/// casing chosen for an unregistered source type (no provider match) is deterministic across calls
/// rather than depending on unordered SQL row order.
/// </summary>
internal static class SourceTypeGrouping
{
    public static IEnumerable<(string CanonicalSourceType, IBankSyncProvider? Provider, IEnumerable<Account> Accounts)> BySourceType(
        IEnumerable<Account> accounts,
        IReadOnlyDictionary<string, IBankSyncProvider> providers)
    {
        var syncable = accounts
            .Where(a => !SyncConstants.IsManual(a.SourceType))
            .OrderBy(a => a.Id);

        foreach (var bySource in syncable.GroupBy(a => a.SourceType, StringComparer.OrdinalIgnoreCase))
        {
            providers.TryGetValue(bySource.Key, out var provider);
            var canonicalSourceType = provider?.SourceType ?? bySource.Key;
            yield return (canonicalSourceType, provider, bySource);
        }
    }
}
