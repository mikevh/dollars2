using Dollars2.Api.Models;
using Dollars2.Api.Providers;
using Dollars2.Api.Repositories;

namespace Dollars2.Api.Services;

public class AccountService
{
    private readonly AccountRepository _accountRepo;
    private readonly SyncLogRepository _syncLogRepo;
    private readonly AccountBalanceRepository _balanceRepo;
    private readonly IReadOnlyDictionary<string, IBankSyncProvider> _providers;

    public AccountService(
        AccountRepository accountRepo,
        SyncLogRepository syncLogRepo,
        AccountBalanceRepository balanceRepo,
        IEnumerable<IBankSyncProvider> providers)
    {
        _accountRepo = accountRepo;
        _syncLogRepo = syncLogRepo;
        _balanceRepo = balanceRepo;
        _providers = providers.ToDictionary(p => p.SourceType, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<AccountGroupResponse>> GetAccountGroupsAsync(int userId)
    {
        var accounts = (await _accountRepo.GetByUserIdAsync(userId)).ToList();
        if (accounts.Count == 0)
        {
            return Array.Empty<AccountGroupResponse>();
        }

        var accountIds = accounts.Select(a => a.Id).ToList();
        var latestLogs = await _syncLogRepo.GetLatestPerAccountAsync(accountIds);
        var latestBalances = await _balanceRepo.GetLatestPerAccountAsync(accountIds);
        return BuildGroups(accounts, latestLogs, latestBalances, _providers);
    }

    /// <summary>
    /// Groups accounts by the connection they sync through, attaching each account's latest sync info.
    /// Pure (no I/O) so the grouping logic can be unit-tested without a database. Syncable accounts are
    /// grouped by source type then provider connection key; Manual accounts form a single "Manual" group.
    /// </summary>
    public static IReadOnlyList<AccountGroupResponse> BuildGroups(
        IReadOnlyList<Account> accounts,
        IEnumerable<SyncLog> latestLogs,
        IEnumerable<AccountBalance> latestBalances,
        IReadOnlyDictionary<string, IBankSyncProvider> providers)
    {
        var logsByAccount = latestLogs.ToDictionary(l => l.AccountId);
        var balancesByAccount = latestBalances.ToDictionary(b => b.AccountId);
        var groups = new List<AccountGroupResponse>();

        var syncable = accounts.Where(a => a.SourceType != SyncConstants.SourceTypeManual);
        foreach (var bySource in syncable.GroupBy(a => a.SourceType))
        {
            providers.TryGetValue(bySource.Key, out var provider);

            // Accounts that share a connection key are fetched together by the sync service; group them
            // together here too. Fall back to a per-account key when the provider is unknown, mirroring
            // how the sync service isolates such accounts.
            var byConnection = bySource.GroupBy(a =>
                provider is not null ? provider.GetConnectionKey(a) : $"account:{a.Id}");

            foreach (var connection in byConnection)
            {
                groups.Add(new AccountGroupResponse
                {
                    ConnectionId = ConnectionKeyHasher.Hash(bySource.Key, connection.Key),
                    SourceType = bySource.Key,
                    Accounts = connection.Select(a => ToInfo(a, logsByAccount, balancesByAccount)).ToList(),
                });
            }
        }

        var manual = accounts.Where(a => a.SourceType == SyncConstants.SourceTypeManual).ToList();
        if (manual.Count > 0)
        {
            groups.Add(new AccountGroupResponse
            {
                ConnectionId = "manual",
                SourceType = SyncConstants.SourceTypeManual,
                Accounts = manual.Select(a => ToInfo(a, logsByAccount, balancesByAccount)).ToList(),
            });
        }

        return groups;
    }

    private static AccountInfoResponse ToInfo(
        Account account,
        IReadOnlyDictionary<int, SyncLog> logsByAccount,
        IReadOnlyDictionary<int, AccountBalance> balancesByAccount)
    {
        logsByAccount.TryGetValue(account.Id, out var log);
        balancesByAccount.TryGetValue(account.Id, out var balance);
        return new AccountInfoResponse
        {
            Id = account.Id,
            Name = account.Name,
            LastSyncedAt = log?.SyncedAt,
            LastStatus = log?.Status,
            Balance = balance?.Balance,
        };
    }
}
