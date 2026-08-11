using Dollars2.Api.Data;
using Dollars2.Api.Models;
using Dollars2.Api.Providers;
using Dollars2.Api.Repositories;

namespace Dollars2.Api.Services;

public class BankSyncService
{
    /// <summary>Signals a manual sync/resync against a provider disabled in configuration, which the controller answers with a 400.</summary>
    public const string ProviderDisabledCode = "PROVIDER_DISABLED";

    private readonly DbSession _dbSession;
    private readonly AccountRepository _accountRepo;
    private readonly TransactionRepository _transactionRepo;
    private readonly SyncLogRepository _syncLogRepo;
    private readonly AccountBalanceRepository _accountBalanceRepo;
    private readonly SyncArchiveRepository _syncArchiveRepo;
    private readonly IReadOnlyDictionary<string, IBankSyncProvider> _providers;
    private readonly SyncLockService _syncLock;
    private readonly ILogger<BankSyncService> _logger;

    public BankSyncService(
        DbSession dbSession,
        AccountRepository accountRepo,
        TransactionRepository transactionRepo,
        SyncLogRepository syncLogRepo,
        AccountBalanceRepository accountBalanceRepo,
        SyncArchiveRepository syncArchiveRepo,
        IEnumerable<IBankSyncProvider> providers,
        SyncLockService syncLock,
        ILogger<BankSyncService> logger)
    {
        _dbSession = dbSession;
        _accountRepo = accountRepo;
        _transactionRepo = transactionRepo;
        _syncLogRepo = syncLogRepo;
        _accountBalanceRepo = accountBalanceRepo;
        _syncArchiveRepo = syncArchiveRepo;
        _providers = providers.ToDictionary(p => p.SourceType, StringComparer.OrdinalIgnoreCase);
        _syncLock = syncLock;
        _logger = logger;
    }

    public async Task<IEnumerable<SyncResult>> SyncForUserAsync(int userId, bool enforceMinInterval = false, CancellationToken cancellationToken = default)
    {
        var accounts = await _accountRepo.GetByUserIdAsync(userId);
        var results = new List<SyncResult>();

        var providerGroups = accounts
            .Where(a => !SyncConstants.IsManual(a.SourceType))
            .GroupBy(a => a.SourceType, StringComparer.OrdinalIgnoreCase);

        foreach (var group in providerGroups)
        {
            var provider = GetProvider(group.Key);

            if (provider is null)
            {
                foreach (var account in group)
                {
                    _logger.LogWarning("No provider found for account {AccountId} ({AccountName}) with source type {SourceType}", account.Id, account.Name, account.SourceType);
                    results.Add(SkippedResult(account));
                }
                continue;
            }

            var sourceType = provider.SourceType;

            if (enforceMinInterval && await IsWithinMinIntervalAsync(userId, sourceType, provider.MinSyncInterval))
            {
                _logger.LogInformation("Skipping scheduled {SourceType} sync for user {UserId} — last successful sync is within the {MinInterval} minimum interval", sourceType, userId, provider.MinSyncInterval);

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
                var syncResults = await SyncConnectionAsync(userId, provider, connectionAccounts, cancellationToken);
                results.AddRange(syncResults);
            }
        }

        return results;
    }

    /// <summary>
    /// Syncs a single connection group (identified by the opaque connectionId emitted by
    /// GET /api/accounts) immediately, bypassing MinSyncInterval — the per-group manual sync. Returns
    /// null when no syncable connection matches (unknown id, or the "manual" group). Callers must hold
    /// the per-user <see cref="SyncLockService"/>.
    /// </summary>
    public async Task<DollarsApiResponse<IReadOnlyList<SyncResult>>?> SyncConnectionForUserAsync(int userId, string connectionId, CancellationToken cancellationToken = default)
    {
        var accounts = (await _accountRepo.GetByUserIdAsync(userId)).ToList();
        var connectionAccounts = ResolveConnectionAccounts(accounts, connectionId, _providers);
        if (connectionAccounts.Count == 0)
        {
            return null;
        }

        var provider = GetProvider(connectionAccounts[0].SourceType);
        if (provider is null)
        {
            // A user-initiated sync on a disabled provider is a dead end, not a background skip —
            // tell the user why rather than silently reporting success with nothing synced.
            return DollarsApiResponse<IReadOnlyList<SyncResult>>.Fail(
                ProviderUnavailableMessage(connectionAccounts[0].SourceType),
                ProviderDisabledCode);
        }

        // Manual sync bypasses MinSyncInterval by design.
        var rv = await SyncConnectionAsync(userId, provider, connectionAccounts, cancellationToken);

        return DollarsApiResponse<IReadOnlyList<SyncResult>>.Success(rv);
    }

    /// <summary>
    /// User-initiated full resync of a single connection group: re-fetches over an explicit lookback
    /// window of <paramref name="days"/> (default surfaced by the UI is 180) instead of the automatic
    /// "since last successful sync" window. Duplicate imports are still prevented by ProviderTransactionId
    /// dedup, so only genuinely-new transactions are created and counted. Providers that fetch by date
    /// window (SimpleFIN) honor <paramref name="days"/>; cursor-based providers (Plaid) ignore the window
    /// but reset their cursor to re-stream from scratch. Returns null when no syncable connection matches
    /// (unknown id, or the "manual" group). Callers must hold the per-user <see cref="SyncLockService"/>.
    /// </summary>
    public async Task<DollarsApiResponse<IReadOnlyList<SyncResult>>?> ResyncConnectionForUserAsync(int userId, string connectionId, CancellationToken cancellationToken = default)
    {
        var accounts = (await _accountRepo.GetByUserIdAsync(userId)).ToList();
        var connectionAccounts = ResolveConnectionAccounts(accounts, connectionId, _providers);
        if (connectionAccounts.Count == 0)
        {
            return null;
        }

        var provider = GetProvider(connectionAccounts[0].SourceType);
        if (provider is null)
        {
            // A user-initiated resync on a disabled provider is a dead end, not a background skip —
            // tell the user why rather than silently reporting success with nothing synced.
            return DollarsApiResponse<IReadOnlyList<SyncResult>>.Fail(
                ProviderUnavailableMessage(connectionAccounts[0].SourceType),
                ProviderDisabledCode);
        }

        var rv = await SyncConnectionAsync(userId, provider, connectionAccounts, cancellationToken, fullResync: true);

        return DollarsApiResponse<IReadOnlyList<SyncResult>>.Success(rv);
    }

    /// <summary>
    /// Resolves the opaque connectionId back to the syncable accounts in that connection group. Mirrors
    /// <see cref="AccountService.BuildGroups"/> grouping so ids round-trip. Pure (no I/O) so it can be
    /// unit-tested. Returns an empty list when no syncable group matches (unknown id, or "manual").
    /// </summary>
    public static IReadOnlyList<Account> ResolveConnectionAccounts(IReadOnlyList<Account> accounts, string connectionId, IReadOnlyDictionary<string, IBankSyncProvider> providers)
    {
        var syncable = accounts.Where(a => !SyncConstants.IsManual(a.SourceType));
        foreach (var bySource in syncable.GroupBy(a => a.SourceType, StringComparer.OrdinalIgnoreCase))
        {
            providers.TryGetValue(bySource.Key, out var provider);
            var canonicalSourceType = provider?.SourceType ?? bySource.Key;

            // Fall back to a per-account key when the provider is unknown, mirroring how the grouping in
            // AccountService and the sync isolate such accounts.
            var byConnection = bySource.GroupBy(a =>
                provider is not null ? provider.GetConnectionKey(a) : $"account:{a.Id}");

            foreach (var connection in byConnection)
            {
                if (ConnectionKeyHasher.Hash(canonicalSourceType, connection.Key) == connectionId)
                {
                    return connection.ToList();
                }
            }
        }

        return Array.Empty<Account>();
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

    private async Task<IReadOnlyList<SyncResult>> SyncConnectionAsync(int userId, IBankSyncProvider provider, IReadOnlyList<Account> accounts, CancellationToken cancel, bool fullResync = false)
    {
        // One run id and one instant for the whole connection group: the group is what the provider's
        // single upstream call covers, so it is what the archive should be able to reassemble as "this
        // sync". Taken before the fetch so the stamp reflects when the run started, not when each
        // account happened to finish persisting.
        var syncRunId = Guid.NewGuid();
        var syncedAt = DateTime.UtcNow;

        IReadOnlyDictionary<int, ProviderSyncResult> fetched;
        try
        {
            // The provider's single upstream call covers the whole group, so fetch from the earliest
            // point any account in it needs. Overlap by some time to avoid missing transactions posted
            // near the boundary; deduplication via ProviderTransactionId prevents double-imports.
            var since = new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            foreach (var account in accounts)
            {
                var lastSync = await _syncLogRepo.GetLastSuccessfulAsync(account.Id);
                _logger.LogInformation("Last successful sync for account {AccountId} ({AccountName}) was at {LastSyncTime}", account.Id, account.Name, lastSync?.SyncedAt);

                // going back 7 days from last sync time per simplefin support suggestion email on 7/29/2026
                var accountSince = (lastSync?.SyncedAt.AddDays(-7) ?? DateTime.UtcNow.AddDays(-180));
                if (accountSince < since)
                {
                    since = accountSince;
                }
            }

            _logger.LogInformation("Syncing {AccountCount} account(s) for user {UserId} via {SourceType} since {since} (fullResync: {FullResync})", accounts.Count, userId, provider.SourceType, since.ToString("u"), fullResync);
            fetched = await provider.FetchTransactionsForConnectionAsync(accounts, since, fullResync, cancel);
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
            results.Add(await PersistAccountResultAsync(account, syncResult, syncRunId, syncedAt, cancel));
        }
        return results;
    }

    private async Task<SyncResult> PersistAccountResultAsync(Account account, ProviderSyncResult syncResult, Guid syncRunId, DateTime syncedAt, CancellationToken cancel)
    {
        // Ahead of the Error branch below so a failed account still gets its provider errors archived —
        // that is precisely the case the archive exists to explain.
        await ArchiveBestEffortAsync(account, syncResult, syncRunId, syncedAt, cancel);

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
                                   || existing.Date != t.Date;
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

                if (syncResult.Balance is not null)
                {
                    await _accountBalanceRepo.CreateAsync(account.Id, syncResult.Balance.Value);
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

    /// <summary>
    /// Writes this account's provider payloads to the sync archive, swallowing anything that goes wrong.
    /// </summary>
    /// <remarks>
    /// Best-effort by design and deliberately outside the DbSession transaction: DynamoDB cannot join
    /// it, and a DynamoDB outage must never keep a transaction out of the budget or roll back an MSSQL
    /// write that already succeeded. The catch is unfiltered — including cancellation — because there is
    /// no failure here worth failing a sync over.
    /// </remarks>
    private async Task ArchiveBestEffortAsync(Account account, ProviderSyncResult syncResult, Guid syncRunId, DateTime syncedAt, CancellationToken cancel)
    {
        try
        {
            await _syncArchiveRepo.ArchiveAsync(account, syncResult, syncRunId, syncedAt, cancel);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not archive sync payloads for account {AccountId} ({AccountName}) in sync run {SyncRunId}; the sync itself is unaffected", account.Id, account.Name, syncRunId);
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

    /// <summary>
    /// Message for a manual sync/resync whose provider lookup failed. Distinguishes a provider
    /// that's registered but toggled off in config from a source type with no provider at all —
    /// the latter shouldn't claim there's a config toggle that doesn't exist.
    /// </summary>
    private string ProviderUnavailableMessage(string sourceType) =>
        _providers.ContainsKey(sourceType)
            ? $"{sourceType} sync is currently disabled."
            : $"{sourceType} sync is not available.";

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
