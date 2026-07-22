using Dollars2.Api.Data;
using Dollars2.Api.Models;
using Dollars2.Api.Providers;
using Dollars2.Api.Repositories;

namespace Dollars2.Api.Services;

public class BankSyncService
{
    private readonly DbSession _dbSession;
    private readonly AccountRepository _accountRepo;
    private readonly TransactionRepository _transactionRepo;
    private readonly SyncLogRepository _syncLogRepo;
    private readonly IReadOnlyDictionary<string, IBankSyncProvider> _providers;
    private readonly SyncLockService _syncLock;
    private readonly ILogger<BankSyncService> _logger;

    public BankSyncService(
        DbSession dbSession,
        AccountRepository accountRepo,
        TransactionRepository transactionRepo,
        SyncLogRepository syncLogRepo,
        IEnumerable<IBankSyncProvider> providers,
        SyncLockService syncLock,
        ILogger<BankSyncService> logger)
    {
        _dbSession = dbSession;
        _accountRepo = accountRepo;
        _transactionRepo = transactionRepo;
        _syncLogRepo = syncLogRepo;
        _providers = providers.ToDictionary(p => p.SourceType, StringComparer.OrdinalIgnoreCase);
        _syncLock = syncLock;
        _logger = logger;
    }

    public async Task<IEnumerable<SyncResult>> SyncForUserAsync(int userId, bool enforceMinInterval = false, CancellationToken cancellationToken = default)
    {
        var accounts = await _accountRepo.GetByUserIdAsync(userId);
        var results = new List<SyncResult>();

        var providerGroups = accounts
            .Where(a => a.SourceType != SyncConstants.SourceTypeManual)
            .GroupBy(a => a.SourceType);

        foreach (var group in providerGroups)
        {
            var sourceType = group.Key;
            var provider = GetProvider(sourceType);

            if (provider is null)
            {
                foreach (var account in group)
                {
                    _logger.LogWarning("No provider found for account {AccountId} ({AccountName}) with source type {SourceType}", account.Id, account.Name, account.SourceType);
                    results.Add(SkippedResult(account));
                }
                continue;
            }

            if (enforceMinInterval && await IsWithinMinIntervalAsync(userId, sourceType, provider.MinSyncInterval))
            {
                _logger.LogInformation("Skipping scheduled {SourceType} sync for user {UserId} — last successful sync is within the {MinInterval} minimum interval",
                    sourceType, userId, provider.MinSyncInterval);
                foreach (var account in group)
                {
                    results.Add(SkippedResult(account));
                }
                continue;
            }

            // A single upstream call can cover multiple stored accounts (e.g. a Plaid Item or a
            // SimpleFIN access URL), so fetch once per connection and distribute the results.
            var connectionGroups = group.GroupBy(a => provider.GetConnectionKey(a));
            foreach (var connectionGroup in connectionGroups)
            {
                var connectionAccounts = connectionGroup.ToList();
                results.AddRange(await SyncConnectionAsync(userId, provider, connectionAccounts, cancellationToken));
            }
        }

        return results;
    }

    private static SyncResult SkippedResult(Account account) => new()
    {
        AccountId = account.Id,
        AccountName = account.Name,
        Status = SyncConstants.StatusSkipped,
        TransactionCount = 0,
    };

    private async Task<bool> IsWithinMinIntervalAsync(int userId, string sourceType, TimeSpan minInterval)
    {
        var lastSuccess = await _syncLogRepo.GetLastSuccessfulForUserProviderAsync(userId, sourceType);
        if (lastSuccess is null)
        {
            return false;
        }
        return DateTime.UtcNow - lastSuccess.SyncedAt < minInterval;
    }

    public async Task SyncAllUsersAsync(IEnumerable<int> userIds, CancellationToken cancellationToken = default)
    {
        var userIdList = userIds.ToList();
        _logger.LogInformation("Starting bank sync for {UserCount} users", userIdList.Count);
        foreach (var userId in userIdList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_syncLock.TryAcquire(userId))
            {
                _logger.LogInformation("Skipping scheduled sync for user {UserId} — sync already in progress", userId);
                continue;
            }
            try
            {
                await SyncForUserAsync(userId, enforceMinInterval: true, cancellationToken);
            }
            finally
            {
                _syncLock.Release(userId);
            }
        }
        _logger.LogInformation("Completed bank sync for {UserCount} users", userIdList.Count);
    }

    private async Task<IReadOnlyList<SyncResult>> SyncConnectionAsync(
        int userId, IBankSyncProvider provider, IReadOnlyList<Account> accounts, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<int, ProviderSyncResult> fetched;
        try
        {
            // The provider's single upstream call covers the whole group, so fetch from the earliest
            // point any account in it needs. Overlap by 12 hours to avoid missing transactions posted
            // near the boundary; deduplication via ProviderTransactionId prevents double-imports.
            DateTime? since = null;
            foreach (var account in accounts)
            {
                var lastSync = await _syncLogRepo.GetLastSuccessfulAsync(account.Id);
                _logger.LogInformation("Last successful sync for account {AccountId} ({AccountName}) was at {LastSyncTime}", account.Id, account.Name, lastSync?.SyncedAt);
                var accountSince = (lastSync?.SyncedAt ?? DateTime.UtcNow.AddDays(-30)).AddHours(-12);
                if (since is null || accountSince < since)
                {
                    since = accountSince;
                }
            }

            _logger.LogInformation("Syncing {AccountCount} account(s) for user {UserId} via {SourceType}", accounts.Count, userId, provider.SourceType);
            fetched = await provider.FetchTransactionsForConnectionAsync(accounts, since, cancellationToken);
        }
        catch (Exception ex)
        {
            // A shared fetch failure fails every account in the connection group.
            var failures = new List<SyncResult>();
            foreach (var account in accounts)
            {
                failures.Add(await RecordFailureAsync(account, ex));
            }
            return failures;
        }

        var results = new List<SyncResult>();
        foreach (var account in accounts)
        {
            var syncResult = fetched.TryGetValue(account.Id, out var r)
                ? r
                : new ProviderSyncResult(Array.Empty<SyncedTransaction>(), Array.Empty<string>(), null);
            results.Add(await PersistAccountResultAsync(account, syncResult));
        }
        return results;
    }

    private async Task<SyncResult> PersistAccountResultAsync(Account account, ProviderSyncResult syncResult)
    {
        if (syncResult.Error is not null)
        {
            // The shared upstream call succeeded, but this specific account can't be synced (e.g. its
            // connection details are misconfigured). Record it as a failure so it surfaces instead of
            // silently reporting a healthy empty sync.
            _logger.LogWarning("Account {AccountId} ({AccountName}) could not be synced: {Error}", account.Id, account.Name, syncResult.Error);
            return await RecordFailureAsync(account, syncResult.Error);
        }

        try
        {
            var count = 0;
            _dbSession.BeginTransaction();
            try
            {
                foreach (var t in syncResult.Upserts)
                {
                    var existing = await _transactionRepo.GetByProviderTransactionIdAsync(account.Id, t.ProviderTransactionId);
                    if (existing is null)
                    {
                        await _transactionRepo.CreateFromSyncAsync(
                            account.UserId, account.Id, t.ProviderTransactionId,
                            t.Date, t.Description, t.Payee, t.Memo, t.Amount, t.IsPending);
                        count++;
                    }
                    else if (!existing.IsDeleted)
                    {
                        var changed = (existing.IsPending && !t.IsPending)
                                   || existing.Amount != t.Amount
                                   || existing.Description != t.Description
                                   || existing.Payee != t.Payee
                                   || existing.Memo != t.Memo
                                   || existing.Date.Date != t.Date;
                        if (changed)
                        {
                            await _transactionRepo.UpdateFromSyncAsync(existing.Id, t.Date, t.Description, t.Payee, t.Memo, t.Amount, t.IsPending);
                        }
                    }
                }

                foreach (var removedId in syncResult.RemovedProviderTransactionIds)
                {
                    await _transactionRepo.SoftDeleteByProviderTransactionIdAsync(account.Id, removedId);
                }

                if (syncResult.UpdatedConnectionDetailsJson is not null)
                {
                    await _accountRepo.UpdateConnectionDetailsJsonAsync(account.Id, syncResult.UpdatedConnectionDetailsJson);
                }

                await _syncLogRepo.CreateAsync(account.Id, SyncConstants.StatusSuccess, count, null);
                _dbSession.Commit();
            }
            catch
            {
                _dbSession.Rollback();
                throw;
            }

            _logger.LogInformation("Completed sync for account {AccountId} ({AccountName}): {Status}, {TransactionCount} new transactions", account.Id, account.Name, SyncConstants.StatusSuccess, count);
            return new SyncResult
            {
                AccountId = account.Id,
                AccountName = account.Name,
                Status = SyncConstants.StatusSuccess,
                TransactionCount = count,
            };
        }
        catch (Exception ex)
        {
            return await RecordFailureAsync(account, ex);
        }
    }

    private async Task<SyncResult> RecordFailureAsync(Account account, Exception ex)
    {
        _logger.LogError(ex, "Sync failed for account {AccountId} ({AccountName})", account.Id, account.Name);
        return await RecordFailureAsync(account, ex.Message);
    }

    private async Task<SyncResult> RecordFailureAsync(Account account, string errorMessage)
    {
        try
        {
            await _syncLogRepo.CreateAsync(account.Id, SyncConstants.StatusFailure, 0, errorMessage);
        }
        catch (Exception logEx)
        {
            _logger.LogError(logEx, "Failed to write failure log for account {AccountId}", account.Id);
        }

        return new SyncResult
        {
            AccountId = account.Id,
            AccountName = account.Name,
            Status = SyncConstants.StatusFailure,
            TransactionCount = 0,
            ErrorMessage = errorMessage,
        };
    }

    private IBankSyncProvider? GetProvider(string sourceType)
    {
        if (_providers.TryGetValue(sourceType, out var provider))
        {
            if(!provider.Enabled)
            {
                _logger.LogWarning($"{sourceType} is disabled");
                return null;
            }

            return provider;
        }
        return null;
    }
}
